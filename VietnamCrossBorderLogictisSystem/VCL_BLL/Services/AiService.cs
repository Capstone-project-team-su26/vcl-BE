using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.ML;
using Microsoft.ML.Data;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services;

public class AiService : IAiService
{
    private readonly IUnitOfWork _unitOfWork;

    public AiService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PutawaySuggestionResponseDto> GetPutawaySuggestionsAsync(PutawaySuggestionRequest request)
    {
        var response = new PutawaySuggestionResponseDto();

        if (request.ParcelId == Guid.Empty)
        {
            throw new ArgumentException("Mã kiện hàng (parcelId) không hợp lệ.");
        }
        if (request.WarehouseId == Guid.Empty)
        {
            throw new ArgumentException("Mã kho hàng (warehouseId) không hợp lệ.");
        }

        // 1. Kiểm tra Parcel
        var parcelRepo = _unitOfWork.Repository<Parcel>();
        var parcel = await parcelRepo.GetByIdAsync(request.ParcelId);
        if (parcel == null)
        {
            throw new NotFoundException($"Không tìm thấy kiện hàng với ID: {request.ParcelId}");
        }

        // 2. Kiểm tra Warehouse
        var warehouseRepo = _unitOfWork.Repository<Warehouse>();
        var warehouse = await warehouseRepo.GetByIdAsync(request.WarehouseId);
        if (warehouse == null)
        {
            throw new NotFoundException($"Không tìm thấy kho hàng với ID: {request.WarehouseId}");
        }

        // 3. Kiểm tra thông số kiện hàng & thêm Cảnh báo nếu thiếu thông tin
        decimal parcelVolume = 0m;
        bool hasDimensions = parcel.Length.HasValue && parcel.Length.Value > 0 &&
                             parcel.Width.HasValue && parcel.Width.Value > 0 &&
                             parcel.Height.HasValue && parcel.Height.Value > 0;

        if (!hasDimensions)
        {
            response.Warnings.Add("Kiện hàng thiếu thông tin kích thước (Dài, Rộng, Cao) để tính toán chính xác thể tích.");
        }
        else
        {
            parcelVolume = parcel.Length.Value * parcel.Width.Value * parcel.Height.Value;
        }

        decimal parcelWeight = 0m;
        if (!parcel.ActualWeight.HasValue || parcel.ActualWeight.Value <= 0)
        {
            response.Warnings.Add("Kiện hàng thiếu thông tin trọng lượng thực tế (ActualWeight) để tính toán chính xác tải trọng.");
        }
        else
        {
            parcelWeight = parcel.ActualWeight.Value;
        }

        // 4. Lấy danh sách layout items hợp lệ của kho và các Bin hoạt động
        var layoutRepo = _unitOfWork.Repository<WarehouseLocationLayout>();
        var allLayouts = await layoutRepo.GetAllAsync();
        var validLayouts = allLayouts.Where(l => l.WarehouseId == request.WarehouseId &&
                                                string.Equals(l.LayoutType, "BIN", StringComparison.OrdinalIgnoreCase) &&
                                                !string.Equals(l.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase) &&
                                                !string.Equals(l.Status, "LOCKED", StringComparison.OrdinalIgnoreCase) &&
                                                !string.Equals(l.Status, "DISABLED", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

        var binRepo = _unitOfWork.Repository<Bin>();
        var bins = await binRepo.GetAllAsync();
        var activeBins = bins.Where(b => b.WarehouseId == request.WarehouseId && b.IsActive).ToDictionary(b => b.Id);

        var allParcels = await parcelRepo.GetAllAsync();
        var storedParcels = allParcels.Where(p => p.BinId.HasValue &&
                                                  string.Equals(p.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase))
                                      .ToList();

        // 5. Xác định Category của kiện hàng để làm đầu vào mô hình
        string category = "General";
        if (parcel.ConsignmentId.HasValue)
        {
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var orderItems = await orderItemRepo.GetAllAsync();
            var item = orderItems.FirstOrDefault(oi => oi.OrderId == parcel.ConsignmentId.Value);
            if (item != null && !string.IsNullOrEmpty(item.ProductName))
            {
                category = item.ProductName;
            }
        }

        // 6. Lấy lịch sử xếp dỡ và huấn luyện mô hình ML.NET in-process
        var historyRepo = _unitOfWork.Repository<PutAwayHistory>();
        var histories = await historyRepo.GetAllAsync();

        var trainingData = histories.Select(h => new PutAwayModelInput
        {
            Weight = (float)h.ParcelWeight,
            Volume = (float)h.Volume,
            Category = h.Category,
            BinId = h.BinId.ToString()
        }).ToList();

        var distinctBinsInHistory = trainingData.Select(x => x.BinId).Distinct().ToList();

        // Bảo đảm có ít nhất 2 nhãn phân loại khác nhau để ML.NET phân loại
        if (distinctBinsInHistory.Count < 2)
        {
            var activeBinIds = activeBins.Keys.Select(k => k.ToString()).ToList();
            if (activeBinIds.Count >= 2)
            {
                trainingData.Add(new PutAwayModelInput { Weight = 5.0f, Volume = 0.01f, Category = "Fragile", BinId = activeBinIds[0] });
                trainingData.Add(new PutAwayModelInput { Weight = 1.0f, Volume = 0.002f, Category = "Shoes", BinId = activeBinIds[1] });
            }
            else if (activeBinIds.Count == 1)
            {
                trainingData.Add(new PutAwayModelInput { Weight = 5.0f, Volume = 0.01f, Category = "Fragile", BinId = activeBinIds[0] });
                trainingData.Add(new PutAwayModelInput { Weight = 1.0f, Volume = 0.001f, Category = "Dummy", BinId = Guid.Empty.ToString() });
            }
        }

        var suggestions = new List<SuggestedLocationDto>();
        bool preferredZoneHasAvailable = false;

        try
        {
            var mlContext = new MLContext(seed: 42);
            var trainingDataView = mlContext.Data.LoadFromEnumerable(trainingData);

            // Pipeline huấn luyện ML.NET
            var pipeline = mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(PutAwayModelInput.BinId))
                .Append(mlContext.Transforms.Categorical.OneHotEncoding("CategoryEncoded", nameof(PutAwayModelInput.Category)))
                .Append(mlContext.Transforms.Concatenate("Features", nameof(PutAwayModelInput.Weight), nameof(PutAwayModelInput.Volume), "CategoryEncoded"))
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            var mlModel = pipeline.Fit(trainingDataView);

            // Đọc danh sách nhãn BinId đã ánh xạ sang chỉ số của mảng Score
            VBuffer<ReadOnlyMemory<char>> keys = default;
            var labelColumn = mlModel.Transform(trainingDataView).Schema["Label"];
            labelColumn.GetKeyValues(ref keys);
            var binIdLabels = keys.DenseValues().Select(x => x.ToString()).ToArray();

            // Tạo engine dự báo
            var predEngine = mlContext.Model.CreatePredictionEngine<PutAwayModelInput, PutAwayModelPrediction>(mlModel);

            var modelInput = new PutAwayModelInput
            {
                Weight = (float)parcelWeight,
                Volume = (float)parcelVolume,
                Category = category
            };

            var prediction = predEngine.Predict(modelInput);

            // Đọc phân phối xác suất
            for (int i = 0; i < prediction.Score.Length; i++)
            {
                string binIdStr = binIdLabels[i];
                if (!Guid.TryParse(binIdStr, out var binGuid) || binGuid == Guid.Empty)
                    continue;

                // Tìm vị trí ô sơ đồ trong kho
                var layout = validLayouts.FirstOrDefault(l => l.BinId == binGuid);
                if (layout == null || !activeBins.TryGetValue(binGuid, out var bin))
                    continue;

                // A. Kiểm tra thể tích khả dụng
                decimal availableVolume = decimal.MaxValue;
                if (bin.MaxVolume.HasValue)
                {
                    availableVolume = Math.Max(0m, bin.MaxVolume.Value - (bin.CurrentVolume ?? 0m));
                    if (hasDimensions && parcelVolume > availableVolume)
                        continue;
                }

                // B. Kiểm tra tải trọng khả dụng
                decimal availableWeight = decimal.MaxValue;
                var parcelsInBin = storedParcels.Where(p => p.BinId == bin.Id).ToList();
                decimal currentWeightInBin = parcelsInBin.Sum(p => p.ActualWeight ?? 0m);

                if (bin.MaxWeight.HasValue)
                {
                    availableWeight = Math.Max(0m, bin.MaxWeight.Value - currentWeightInBin);
                    if (parcelWeight > 0 && parcelWeight > availableWeight)
                        continue;
                }

                decimal probability = (decimal)prediction.Score[i];
                string reason = $"AI (ML.NET SDCA Maximum Entropy) dự đoán với xác suất là {probability * 100:0.0}% dựa trên lịch sử xếp dỡ.";

                bool isPreferred = request.PreferredZoneId.HasValue && layout.ZoneId == request.PreferredZoneId.Value;
                if (isPreferred)
                {
                    probability += 0.1m;
                    reason = $"Vị trí thuộc khu vực ưu tiên và được AI đánh giá xác suất tin cậy {(decimal)prediction.Score[i] * 100:0.0}%.";
                    preferredZoneHasAvailable = true;
                }

                suggestions.Add(new SuggestedLocationDto
                {
                    LocationId = layout.Id,
                    WarehouseId = layout.WarehouseId,
                    ZoneId = layout.ZoneId,
                    ShelfId = layout.ShelfId,
                    BinId = layout.BinId,
                    DisplayLabel = layout.DisplayLabel,
                    ConfidenceScore = Math.Min(1.0m, probability),
                    Reason = reason
                });
            }
        }
        catch (Exception ex)
        {
            response.Warnings.Add($"Không thể chạy mô hình AI ML.NET: {ex.Message}. Đang chuyển sang đề xuất dự phòng.");
            
            // Fallback sang Heuristic nếu mô hình lỗi
            foreach (var layout in validLayouts)
            {
                if (!layout.BinId.HasValue || !activeBins.TryGetValue(layout.BinId.Value, out var bin))
                    continue;

                decimal availableVolume = decimal.MaxValue;
                if (bin.MaxVolume.HasValue)
                {
                    availableVolume = Math.Max(0m, bin.MaxVolume.Value - (bin.CurrentVolume ?? 0m));
                    if (hasDimensions && parcelVolume > availableVolume)
                        continue;
                }

                decimal availableWeight = decimal.MaxValue;
                var parcelsInBin = storedParcels.Where(p => p.BinId == bin.Id).ToList();
                decimal currentWeightInBin = parcelsInBin.Sum(p => p.ActualWeight ?? 0m);

                if (bin.MaxWeight.HasValue)
                {
                    availableWeight = Math.Max(0m, bin.MaxWeight.Value - currentWeightInBin);
                    if (parcelWeight > 0 && parcelWeight > availableWeight)
                        continue;
                }

                bool isEntirelyEmpty = (bin.CurrentVolume ?? 0m) == 0m && parcelsInBin.Count == 0;
                decimal confidenceScore = isEntirelyEmpty ? 0.9m : 0.7m;
                string reason = isEntirelyEmpty ? "Vị trí trống hoàn toàn." : "Vị trí còn đủ sức chứa khả dụng.";

                bool isPreferred = request.PreferredZoneId.HasValue && layout.ZoneId == request.PreferredZoneId.Value;
                if (isPreferred)
                {
                    confidenceScore += 0.1m;
                    reason = isEntirelyEmpty
                        ? "Vị trí trống hoàn toàn và thuộc khu vực ưu tiên của bạn."
                        : "Vị trí thuộc khu vực ưu tiên, còn đủ sức chứa khả dụng.";
                    preferredZoneHasAvailable = true;
                }

                suggestions.Add(new SuggestedLocationDto
                {
                    LocationId = layout.Id,
                    WarehouseId = layout.WarehouseId,
                    ZoneId = layout.ZoneId,
                    ShelfId = layout.ShelfId,
                    BinId = layout.BinId,
                    DisplayLabel = layout.DisplayLabel,
                    ConfidenceScore = Math.Min(1.0m, confidenceScore),
                    Reason = reason
                });
            }
        }

        // 7. Thêm cảnh báo nếu khu vực ưu tiên không có chỗ
        if (request.PreferredZoneId.HasValue && !preferredZoneHasAvailable)
        {
            response.Warnings.Add("Khu vực yêu cầu (preferredZoneId) không có vị trí trống hoặc khả dụng. Đã đề xuất các khu vực khác.");
        }

        // 8. Sắp xếp giảm dần theo ConfidenceScore và lấy Top 5
        response.SuggestedLocations = suggestions
            .OrderByDescending(s => s.ConfidenceScore)
            .Take(5)
            .ToList();

        if (!response.SuggestedLocations.Any())
        {
            response.Warnings.Add("Không tìm thấy vị trí ô chứa (Bin) nào có đủ sức chứa khả dụng trong kho.");
        }

        return response;
    }
}
