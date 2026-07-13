using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        // 4. Lấy danh sách layout items hợp lệ của kho
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

        var suggestions = new List<SuggestedLocationDto>();
        bool preferredZoneHasAvailable = false;

        foreach (var layout in validLayouts)
        {
            if (!layout.BinId.HasValue || !activeBins.TryGetValue(layout.BinId.Value, out var bin))
            {
                continue; // Skip layout items not linked to an active Bin
            }

            // A. Kiểm tra thể tích khả dụng
            decimal availableVolume = decimal.MaxValue;
            if (bin.MaxVolume.HasValue)
            {
                availableVolume = Math.Max(0m, bin.MaxVolume.Value - (bin.CurrentVolume ?? 0m));
                if (hasDimensions && parcelVolume > availableVolume)
                {
                    continue; // Không đủ thể tích
                }
            }

            // B. Kiểm tra tải trọng khả dụng
            decimal availableWeight = decimal.MaxValue;
            var parcelsInBin = storedParcels.Where(p => p.BinId == bin.Id).ToList();
            decimal currentWeightInBin = parcelsInBin.Sum(p => p.ActualWeight ?? 0m);

            if (bin.MaxWeight.HasValue)
            {
                availableWeight = Math.Max(0m, bin.MaxWeight.Value - currentWeightInBin);
                if (parcelWeight > 0 && parcelWeight > availableWeight)
                {
                    continue; // Không đủ tải trọng
                }
            }

            // C. Xác định xem ô có trống hoàn toàn không
            bool isEntirelyEmpty = (bin.CurrentVolume ?? 0m) == 0m && parcelsInBin.Count == 0;

            // D. Tính Confidence Score
            decimal confidenceScore = isEntirelyEmpty ? 0.9m : 0.7m;
            string reason = isEntirelyEmpty ? "Vị trí trống hoàn toàn." : "Vị trí còn đủ sức chứa khả dụng.";

            // E. Ưu tiên khu vực mong muốn (Preferred Zone)
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

        // 5. Thêm cảnh báo nếu khu vực ưu tiên không có chỗ
        if (request.PreferredZoneId.HasValue && !preferredZoneHasAvailable)
        {
            response.Warnings.Add("Khu vực yêu cầu (preferredZoneId) không có vị trí trống hoặc khả dụng. Đã đề xuất các khu vực khác.");
        }

        // 6. Sắp xếp giảm dần theo ConfidenceScore và lấy Top 5
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
