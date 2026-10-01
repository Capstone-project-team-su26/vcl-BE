using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services;

public class AiService : IAiService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public AiService(IUnitOfWork unitOfWork, HttpClient httpClient, IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _httpClient = httpClient;
        _configuration = configuration;
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
        var validLayouts = (await layoutRepo.FindAsync(l => l.WarehouseId == request.WarehouseId &&
                                                           l.LayoutType != null && l.LayoutType.ToUpper() == "BIN" &&
                                                           l.Status != "INACTIVE" && l.Status != "LOCKED" && l.Status != "DISABLED"))
                                            .ToList();

        // Chỉ gợi ý ô thuộc khu lưu kho đang dùng — cùng luật put-away kiểm, để gợi ý không trỏ vào khu nhận/khu xuất.
        var activeBins = (await BinPlacementPolicy.StorageBinsAsync(_unitOfWork, request.WarehouseId))
                                       .ToDictionary(x => x.Bin.Id, x => x.Bin);

        var storedParcels = (await parcelRepo.FindAsync(p => p.BinId.HasValue && p.PackageStatus != null && p.PackageStatus.ToUpper() == "STORED"))
                                            .ToList();

        // 5. Xác định Đơn hàng, Loại hình nghiệp vụ, Khách hàng & Category
        string businessType = "Hàng Ký Gửi (CONSIGNMENT)";
        Guid? customerId = null;
        string category = "General";

        if (parcel.ConsignmentId.HasValue)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(parcel.ConsignmentId.Value);
            if (order != null)
            {
                customerId = order.CustomerId;
                if (!string.IsNullOrEmpty(order.OrderType) && order.OrderType.ToUpper() == "PURCHASE")
                {
                    businessType = "Hàng Mua Hộ (PURCHASE)";
                }
            }

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var item = await orderItemRepo.FirstOrDefaultAsync(oi => oi.OrderId == parcel.ConsignmentId.Value);
            if (item != null && !string.IsNullOrEmpty(item.ProductName))
            {
                category = item.ProductName;
            }
        }

        // 6. Lọc danh sách Candidate Bins đủ sức chứa thể tích & tải trọng
        var candidateBins = new List<CandidateBinDto>();
        var rejectedByVolume = 0;
        var rejectedByWeight = 0;
        var binsWithoutLayout = 0;

        foreach (var layout in validLayouts)
        {
            if (!layout.BinId.HasValue || !activeBins.TryGetValue(layout.BinId.Value, out var bin))
            {
                binsWithoutLayout++;
                continue;
            }

            var parcelsInBin = storedParcels.Where(p => p.BinId == bin.Id).ToList();
            decimal occupiedVolume = parcelsInBin.Sum(GetParcelVolume);

            decimal availableVolume = decimal.MaxValue;
            decimal occupancyRatio = 0m;

            if (bin.MaxVolume.HasValue && bin.MaxVolume.Value > 0)
            {
                availableVolume = Math.Max(0m, bin.MaxVolume.Value - occupiedVolume);
                occupancyRatio = Math.Min(1m, occupiedVolume / bin.MaxVolume.Value);

                if (hasDimensions && parcelVolume > availableVolume)
                {
                    rejectedByVolume++;
                    continue;
                }
            }

            decimal currentWeightInBin = parcelsInBin.Sum(p => p.ActualWeight ?? 0m);
            decimal availableWeight = decimal.MaxValue;

            if (bin.MaxWeight.HasValue)
            {
                availableWeight = Math.Max(0m, bin.MaxWeight.Value - currentWeightInBin);
                if (parcelWeight > 0 && parcelWeight > availableWeight)
                {
                    rejectedByWeight++;
                    continue;
                }
            }

            // Thống kê số kiện của cùng khách hàng trong ô này
            int sameCustomerCount = customerId.HasValue
                ? parcelsInBin.Count(p => p.Consignment != null && p.Consignment.CustomerId == customerId.Value)
                : 0;

            int levelIndex = layout.RowIndex;
            string levelDesc = levelIndex <= 1 ? "Tầng thấp (Ground/Low Level)" : (levelIndex <= 3 ? "Tầng giữa (Mid Level)" : "Tầng cao (High Level)");

            candidateBins.Add(new CandidateBinDto
            {
                LayoutId = layout.Id,
                WarehouseId = layout.WarehouseId,
                ZoneId = layout.ZoneId,
                ShelfId = layout.ShelfId,
                BinId = bin.Id,
                DisplayLabel = layout.DisplayLabel ?? $"Bin {bin.Id}",
                OccupiedVolume = occupiedVolume,
                AvailableVolume = availableVolume == decimal.MaxValue ? 999999m : availableVolume,
                OccupancyRatio = occupancyRatio,
                CurrentWeight = currentWeightInBin,
                AvailableWeight = availableWeight == decimal.MaxValue ? 9999m : availableWeight,
                IsEntirelyEmpty = parcelsInBin.Count == 0,
                SameCustomerParcelCount = sameCustomerCount,
                ShelfRowIndex = levelIndex,
                BinLevelDescription = levelDesc
            });
        }

        // 7. Xử lý trường hợp không có ô nào phù hợp
        if (!candidateBins.Any())
        {
            if (validLayouts.Count == 0)
            {
                response.Warnings.Add("Kho chưa khai báo ô kệ (Bin) nào trong sơ đồ layout, hoặc tất cả đang ở trạng thái khoá/ngừng dùng. Cần cấu hình sơ đồ kho trước khi dùng gợi ý.");
            }
            else if (activeBins.Count == 0)
            {
                response.Warnings.Add("Kho chưa có ô kệ nào đang hoạt động thuộc khu lưu kho (STORAGE). Cần khai loại khu cho kho trước khi dùng gợi ý.");
            }
            else if (binsWithoutLayout == validLayouts.Count)
            {
                response.Warnings.Add("Các ô trong sơ đồ layout không trỏ tới ô kệ hợp lệ nào đang hoạt động. Cần kiểm tra lại liên kết giữa layout và bin.");
            }
            else
            {
                response.Warnings.Add(
                    $"Không còn ô kệ nào đủ chỗ cho kiện này. Đã xét {validLayouts.Count} ô: " +
                    $"{rejectedByVolume} ô không đủ thể tích (kiện cần {parcelVolume:N0} cm³), " +
                    $"{rejectedByWeight} ô không đủ tải trọng (kiện nặng {parcelWeight:N2} kg).");
            }
            return response;
        }

        // 8. Gọi OpenRouter AI để lấy đánh giá & xếp hạng gợi ý theo 4 tiêu chí nghiệp vụ
        try
        {
            var recommendations = await CallOpenRouterForPutawayAsync(parcel, businessType, customerId, category, candidateBins, request.PreferredZoneId);

            foreach (var rec in recommendations)
            {
                var cand = candidateBins.FirstOrDefault(c => c.BinId == rec.BinId);
                if (cand == null) continue;

                response.SuggestedLocations.Add(new SuggestedLocationDto
                {
                    LocationId = cand.LayoutId,
                    WarehouseId = cand.WarehouseId,
                    ZoneId = cand.ZoneId,
                    ShelfId = cand.ShelfId,
                    BinId = cand.BinId,
                    DisplayLabel = cand.DisplayLabel,
                    ConfidenceScore = Math.Round(Math.Min(1.0m, Math.Max(0.01m, rec.ConfidenceScore)), 4),
                    Reason = rec.Reason
                });
            }

            // Nếu AI trả về danh sách có vị trí gợi ý thì sắp xếp Top 5
            if (response.SuggestedLocations.Any())
            {
                response.SuggestedLocations = response.SuggestedLocations
                    .OrderByDescending(s => s.ConfidenceScore)
                    .Take(5)
                    .ToList();
            }
            else
            {
                // Trường hợp AI trả rỗng: Fallback sang Heuristic
                response.Warnings.Add("OpenRouter AI không trả về gợi ý cụ thể. Đã sử dụng thuật toán tính toán sức chứa còn trống.");
                response.SuggestedLocations = BuildFallbackSuggestions(parcel, businessType, candidateBins, request.PreferredZoneId);
            }
        }
        catch (Exception ex)
        {
            // Fallback an toàn nếu OpenRouter bị lỗi (timeout, chưa nhập key, hết quota, mất mạng)
            response.Warnings.Add($"Không thể kết nối OpenRouter AI ({ex.Message}). Đang sử dụng thuật toán tính toán sức chứa còn trống.");
            response.SuggestedLocations = BuildFallbackSuggestions(parcel, businessType, candidateBins, request.PreferredZoneId);
        }

        // 9. Cảnh báo nếu PreferredZoneId không có ô khả dụng
        if (request.PreferredZoneId.HasValue && !candidateBins.Any(c => c.ZoneId == request.PreferredZoneId.Value))
        {
            response.Warnings.Add("Khu vực yêu cầu (preferredZoneId) không có vị trí trống hoặc khả dụng. Đã đề xuất các khu vực khác.");
        }

        return response;
    }

    private async Task<List<GeminiBinRecommendation>> CallOpenRouterForPutawayAsync(
        Parcel parcel,
        string businessType,
        Guid? customerId,
        string category,
        List<CandidateBinDto> candidateBins,
        Guid? preferredZoneId)
    {
        var apiKey = _configuration["OpenRouterSettings:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var model = _configuration["OpenRouterSettings:Model"] ?? "google/gemini-2.0-flash-001";
        var baseUrl = _configuration["OpenRouterSettings:BaseUrl"] ?? "https://openrouter.ai/api/v1/chat/completions";

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("YOUR_"))
        {
            throw new InvalidOperationException("OpenRouter API Key chưa được cấu hình hợp lệ trong appsettings.json hoặc môi trường.");
        }

        var promptPayload = new
        {
            Parcel = new
            {
                ParcelId = parcel.Id.ToString(),
                PackageCode = parcel.PackageCode ?? "N/A",
                BusinessType = businessType, // "Hàng Mua Hộ (PURCHASE)" hoặc "Hàng Ký Gửi (CONSIGNMENT)"
                CustomerId = customerId?.ToString() ?? "N/A",
                WeightKg = parcel.ActualWeight ?? 0m,
                DimensionsCm = $"{parcel.Length ?? 0}x{parcel.Width ?? 0}x{parcel.Height ?? 0}",
                VolumeCm3 = (parcel.Length ?? 0m) * (parcel.Width ?? 0m) * (parcel.Height ?? 0m),
                Category = category
            },
            PreferredZoneId = preferredZoneId?.ToString(),
            AvailableBins = candidateBins.Select(b => new
            {
                BinId = b.BinId.ToString(),
                Label = b.DisplayLabel,
                ZoneId = b.ZoneId?.ToString(),
                AvailableVolumeCm3 = b.AvailableVolume,
                AvailableWeightKg = b.AvailableWeight,
                FreeSpacePercent = Math.Round((1m - b.OccupancyRatio) * 100, 1),
                ShelfLevelDesc = b.BinLevelDescription,
                SameCustomerParcelsCount = b.SameCustomerParcelCount
            })
        };

        string systemInstruction = @"Bạn là Chuyên gia AI Quản lý Kho Vận (Logistics WMS Expert).
Nhiệm vụ của bạn là phân tích kiện hàng và danh sách các ô kệ khả dụng trong kho, sau đó chọn ra các ô vị trí xếp dỡ tối ưu nhất dựa theo 4 QUY TẮC NGHIỆP VỤ BẮT BUỘC:

1. PHÂN LOẠI THEO LOẠI HÌNH NGHIỆP VỤ (BusinessType):
   - Hàng Mua Hộ (PURCHASE): Thường vận chuyển thẳng/nhanh về Việt Nam -> Ưu tiên đề xuất xếp ở Tầng thấp (Ground/Low Level), vị trí dễ tiếp cận/ngoài cùng gần khu vực đóng gói/xuất hàng.
   - Hàng Ký Gửi (CONSIGNMENT): Đưa vào các vị trí lưu kho tiêu chuẩn theo chuẩn WMS.

2. PHÂN LOẠI THEO KÍCH THƯỚC (Dimensions & Volume Fit):
   - Phân tích kích thước Dài x Rộng x Cao và Thể tích (VolumeCm3) của kiện hàng với thể tích trống của ô kệ (AvailableVolumeCm3).
   - Ưu tiên chọn ô kệ có thể tích vừa vặn thích hợp nhất với kiện hàng, tránh lãng phí ô kệ quá lớn cho kiện hàng nhỏ.

3. PHÂN BỔ THEO TẢI TRỌNG & AN TOÀN KỆ (Weight & Safety):
   - Hàng nặng (> 10kg): TUYỆT ĐỐI KHÔNG xếp ở Tầng cao. Chỉ xếp ở Tầng thấp (Ground/Low Level) hoặc Tầng giữa để đảm bảo an toàn lao động và tải trọng kệ.
   - Hàng nhẹ (< 5kg): Ưu tiên đưa lên Tầng giữa hoặc Tầng cao để nhường tầng thấp cho hàng nặng.

4. PHÂN BỔ GẦN KIỆN CÙNG KHÁCH HÀNG (Customer Clustering):
   - Nếu ô kệ đã có chứa sẵn các kiện hàng khác của CÙNG KHÁCH HÀNG (SameCustomerParcelsCount > 0), ƯU TIÊN CỘNG ĐIỂM ĐỀ XUẤT xếp kiện mới vào cùng ô kệ đó hoặc ô kế bên để thuận tiện gộp đơn, gom hàng khi xuất kho.

BẮT BUỘC trả về đúng định dạng JSON theo cấu trúc sau:
{
  ""recommendations"": [
    {
      ""binId"": ""guid-string-trùng-với-AvailableBins"",
      ""confidenceScore"": 0.95,
      ""reason"": ""Lý do chi tiết bằng tiếng Việt giải thích rõ theo các quy tắc trên""
    }
  ]
}";

        var requestBody = new
        {
            model = model,
            messages = new object[]
            {
                new { role = "system", content = systemInstruction },
                new { role = "user", content = $"Dữ liệu đầu vào:\n{JsonSerializer.Serialize(promptPayload)}" }
            },
            response_format = new { type = "json_object" },
            temperature = 0.2
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        requestMessage.Headers.TryAddWithoutValidation("HTTP-Referer", "https://logictic.site");
        requestMessage.Headers.TryAddWithoutValidation("X-Title", "VCL System");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(requestMessage, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("OpenRouter API phản hồi quá thời hạn cho phép (8 giây).");
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errText = await httpResponse.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Lỗi kết nối OpenRouter API (Mã {httpResponse.StatusCode}): {errText}");
        }

        var responseString = await httpResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return new List<GeminiBinRecommendation>();
        }

        var textContent = choices[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(textContent))
        {
            return new List<GeminiBinRecommendation>();
        }

        var result = JsonSerializer.Deserialize<GeminiResponseDto>(
            textContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return result?.Recommendations ?? new List<GeminiBinRecommendation>();
    }

    private static List<SuggestedLocationDto> BuildFallbackSuggestions(
        Parcel parcel,
        string businessType,
        List<CandidateBinDto> candidateBins,
        Guid? preferredZoneId)
    {
        var suggestions = new List<SuggestedLocationDto>();
        decimal parcelWeight = parcel.ActualWeight ?? 0m;

        foreach (var cand in candidateBins)
        {
            decimal spaceScore = 1m - cand.OccupancyRatio;
            decimal confidenceScore = 0.30m + (0.50m * spaceScore);
            var reasons = new List<string>();

            if (cand.IsEntirelyEmpty)
            {
                reasons.Add("Ô kệ trống hoàn toàn");
            }
            else
            {
                reasons.Add($"Còn trống {spaceScore * 100:0.0}% sức chứa");
            }

            // Quy tắc 1: Loại hình nghiệp vụ
            if (businessType.Contains("PURCHASE") && cand.ShelfRowIndex <= 1)
            {
                confidenceScore += 0.10m;
                reasons.Add("Đơn mua hộ ưu tiên tầng thấp dễ lấy hàng đóng gói xuất kho");
            }

            // Quy tắc 3: Tải trọng & Tầng cao
            if (parcelWeight > 10m && cand.ShelfRowIndex > 2)
            {
                confidenceScore -= 0.40m; // Trừ điểm vì hàng nặng ở tầng cao
                reasons.Add("Cảnh báo: Kiện hàng nặng ở tầng cao");
            }
            else if (parcelWeight > 10m && cand.ShelfRowIndex <= 1)
            {
                confidenceScore += 0.15m;
                reasons.Add("Hàng nặng xếp ở tầng thấp an toàn");
            }

            // Quy tắc 4: Gom kiện cùng khách hàng
            if (cand.SameCustomerParcelCount > 0)
            {
                confidenceScore += 0.20m;
                reasons.Add($"Có sẵn {cand.SameCustomerParcelCount} kiện cùng khách hàng để gom đơn");
            }

            if (preferredZoneId.HasValue && cand.ZoneId == preferredZoneId.Value)
            {
                confidenceScore += 0.10m;
                reasons.Add("Thuộc khu vực ưu tiên");
            }

            suggestions.Add(new SuggestedLocationDto
            {
                LocationId = cand.LayoutId,
                WarehouseId = cand.WarehouseId,
                ZoneId = cand.ZoneId,
                ShelfId = cand.ShelfId,
                BinId = cand.BinId,
                DisplayLabel = cand.DisplayLabel,
                ConfidenceScore = Math.Round(Math.Min(1.0m, Math.Max(0.01m, confidenceScore)), 4),
                Reason = string.Join(". ", reasons) + "."
            });
        }

        return suggestions
            .OrderByDescending(s => s.ConfidenceScore)
            .Take(5)
            .ToList();
    }

    /// <summary>Thể tích một kiện theo cm³; thiếu kích thước thì coi như không chiếm chỗ.</summary>
    private static decimal GetParcelVolume(Parcel parcel) =>
        (parcel.Length ?? 0m) * (parcel.Width ?? 0m) * (parcel.Height ?? 0m);

    public async Task<SalesOrderStatusQueryResponse> QuerySalesOrderStatusAsync(SalesOrderStatusQueryRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Câu hỏi tra cứu (message) không được để trống.");
        }

        var message = request.Message.Trim();

        var orderRepo = _unitOfWork.Repository<Order>();
        var purchaseRequestRepo = _unitOfWork.Repository<PurchaseRequest>();
        var customerRepo = _unitOfWork.Repository<Customer>();
        var parcelRepo = _unitOfWork.Repository<Parcel>();
        var invoiceRepo = _unitOfWork.Repository<Invoice>();
        var paymentRepo = _unitOfWork.Repository<Payment>();
        var purchasePaymentRepo = _unitOfWork.Repository<PurchasePayment>();
        var receivingNoteRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
        var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
        var shipmentRepo = _unitOfWork.Repository<Shipment>();
        var quotationRepo = _unitOfWork.Repository<Quotation>();

        Order? matchedOrder = null;
        PurchaseRequest? matchedPurchaseRequest = null;
        Customer? matchedCustomer = null;
        Parcel? matchedParcel = null;

        var dataSources = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.OrderCode))
        {
            var code = request.OrderCode.Trim();
            matchedOrder = await orderRepo.FirstOrDefaultAsync(o => o.ConsignmentCode == code);
            matchedPurchaseRequest = await purchaseRequestRepo.FirstOrDefaultAsync(p => p.PurchaseCode == code);
            matchedParcel = await parcelRepo.FirstOrDefaultAsync(p => p.PackageCode == code);
        }

        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            matchedCustomer = await customerRepo.GetByIdAsync(request.CustomerId.Value);
        }

        if (request.RelatedId.HasValue && request.RelatedId.Value != Guid.Empty)
        {
            if (string.Equals(request.RelatedType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase))
            {
                matchedOrder = await orderRepo.GetByIdAsync(request.RelatedId.Value);
            }
            else if (string.Equals(request.RelatedType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
            {
                matchedPurchaseRequest = await purchaseRequestRepo.GetByIdAsync(request.RelatedId.Value);
            }
            else if (string.Equals(request.RelatedType, "PARCEL", StringComparison.OrdinalIgnoreCase))
            {
                matchedParcel = await parcelRepo.GetByIdAsync(request.RelatedId.Value);
            }
            else if (string.Equals(request.RelatedType, "CUSTOMER", StringComparison.OrdinalIgnoreCase))
            {
                matchedCustomer = await customerRepo.GetByIdAsync(request.RelatedId.Value);
            }
        }

        var searchTokens = message.Split(new[] { ' ', ',', '.', ':', ';', '?', '!', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(t => t.Trim())
                                  .Where(t => t.Length >= 2)
                                  .ToList();

        if (matchedOrder == null && matchedPurchaseRequest == null && matchedParcel == null && matchedCustomer == null)
        {
            foreach (var token in searchTokens)
            {
                matchedOrder ??= await orderRepo.FirstOrDefaultAsync(o =>
                    (o.ConsignmentCode != null && o.ConsignmentCode == token) ||
                    (o.ReceiverPhone != null && o.ReceiverPhone.Contains(token)) ||
                    (o.ReceiverName != null && o.ReceiverName.Contains(token)));

                matchedPurchaseRequest ??= await purchaseRequestRepo.FirstOrDefaultAsync(p =>
                    (p.PurchaseCode != null && p.PurchaseCode == token) ||
                    (p.ReceiverPhone != null && p.ReceiverPhone.Contains(token)) ||
                    (p.ReceiverName != null && p.ReceiverName.Contains(token)));

                matchedParcel ??= await parcelRepo.FirstOrDefaultAsync(p =>
                    p.PackageCode != null && p.PackageCode == token);

                matchedCustomer ??= await customerRepo.FirstOrDefaultAsync(c =>
                    (c.Phone != null && c.Phone.Contains(token)) ||
                    (c.FullName != null && c.FullName.Contains(token)) ||
                    (c.Email != null && c.Email.Contains(token)));

                if (matchedOrder != null || matchedPurchaseRequest != null || matchedParcel != null || matchedCustomer != null)
                    break;
            }
        }

        if (matchedParcel != null && matchedOrder == null && matchedParcel.ConsignmentId.HasValue)
        {
            matchedOrder = await orderRepo.GetByIdAsync(matchedParcel.ConsignmentId.Value);
        }

        if (matchedCustomer != null && matchedOrder == null && matchedPurchaseRequest == null)
        {
            var custOrders = await orderRepo.FindAsync(o => o.CustomerId == matchedCustomer.Id);
            matchedOrder = custOrders.OrderByDescending(o => o.CreatedAt).FirstOrDefault();

            var custPurchases = await purchaseRequestRepo.FindAsync(p => p.CustomerId == matchedCustomer.Id);
            matchedPurchaseRequest = custPurchases.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        }

        if (matchedOrder == null && matchedPurchaseRequest == null && matchedParcel == null && matchedCustomer == null)
        {
            throw new NotFoundException("Không tìm thấy dữ liệu đơn hàng, kiện hàng hoặc khách hàng liên quan đến câu hỏi.");
        }

        var response = new SalesOrderStatusQueryResponse();

        if (matchedOrder != null)
        {
            dataSources.Add("Orders");
            matchedCustomer ??= matchedOrder.CustomerId.HasValue
                ? await customerRepo.GetByIdAsync(matchedOrder.CustomerId.Value)
                : null;

            response.RelatedOrders.Add(new SalesQueryOrderSummaryDto
            {
                OrderId = matchedOrder.Id,
                OrderCode = matchedOrder.ConsignmentCode ?? string.Empty,
                OrderType = matchedOrder.OrderType ?? "CONSIGNMENT",
                CustomerName = matchedCustomer?.FullName ?? matchedOrder.ReceiverName ?? "N/A",
                CustomerPhone = matchedCustomer?.Phone ?? matchedOrder.ReceiverPhone ?? "N/A",
                Status = matchedOrder.Status ?? "PENDING",
                CreatedAt = matchedOrder.CreatedAt ?? DateTime.UtcNow
            });

            response.CurrentStatus = matchedOrder.Status ?? "PENDING";

            var orderParcels = (await parcelRepo.FindAsync(p => p.ConsignmentId == matchedOrder.Id)).ToList();
            if (orderParcels.Any())
            {
                dataSources.Add("Parcels");
                foreach (var p in orderParcels)
                {
                    response.RelatedParcels.Add(new SalesQueryParcelSummaryDto
                    {
                        ParcelId = p.Id,
                        PackageCode = p.PackageCode ?? string.Empty,
                        Status = p.PackageStatus ?? string.Empty,
                        LocalTrackingCode = string.Empty,
                        WeightKg = p.ActualWeight ?? 0m
                    });
                }
            }

            var orderQuotations = (await quotationRepo.FindAsync(q => q.OrderId == matchedOrder.Id)).ToList();
            var orderQuotationIds = orderQuotations.Select(q => q.Id).ToList();
            Invoice? orderInvoice = null;
            if (orderQuotationIds.Any())
            {
                orderInvoice = await invoiceRepo.FirstOrDefaultAsync(i => i.QuotationId.HasValue && orderQuotationIds.Contains(i.QuotationId.Value));
            }

            if (orderInvoice != null)
            {
                dataSources.Add("Invoices");
                var invoicePayments = (await paymentRepo.FindAsync(p => p.InvoiceId == orderInvoice.Id && p.PaymentStatus != null && p.PaymentStatus.ToUpper() == "SUCCESS")).ToList();
                var totalPaid = invoicePayments.Sum(p => p.Amount);
                var totalInvoiceAmount = orderInvoice.TotalAmount ?? 0m;

                if (totalPaid >= totalInvoiceAmount && totalInvoiceAmount > 0)
                {
                    response.PaymentStatus = "Đã thanh toán đủ";
                }
                else if (totalPaid > 0)
                {
                    response.PaymentStatus = $"Đã thanh toán một phần ({totalPaid:N0}/{totalInvoiceAmount:N0}đ)";
                }
                else
                {
                    response.PaymentStatus = $"Chưa thanh toán ({StatusNames.Of(orderInvoice.InvoiceStatus ?? "UNPAID")})";
                }
            }
            else
            {
                response.PaymentStatus = "Chưa phát hành hóa đơn thanh toán";
            }

            var note = await receivingNoteRepo.FirstOrDefaultAsync(n => n.ConsignmentOrderId == matchedOrder.Id);
            var release = matchedCustomer != null ? await wroRequestRepo.FirstOrDefaultAsync(r => r.CustomerId == matchedCustomer.Id) : null;
            if (release != null)
            {
                dataSources.Add("WroRequests");
                response.WarehouseStatus = $"Yêu cầu xuất kho ({release.WroCode}): {StatusNames.Quote(release.Status)}";
            }
            else if (note != null)
            {
                dataSources.Add("WarehouseReceivingNotes");
                response.WarehouseStatus = $"Đã tiếp nhận kho ({note.ReceivingNoteCode}): {StatusNames.Quote(note.Status)}";
            }
            else
            {
                response.WarehouseStatus = "Chưa nhập kho";
            }

            var parcelIds = orderParcels.Select(p => p.Id).ToList();
            Shipment? shipment = null;
            if (parcelIds.Any())
            {
                var shipmentParcelRepo = _unitOfWork.Repository<ShipmentParcel>();
                var spMatch = await shipmentParcelRepo.FirstOrDefaultAsync(sp => parcelIds.Contains(sp.ParcelId));
                if (spMatch != null)
                {
                    shipment = await shipmentRepo.GetByIdAsync(spMatch.ShipmentId);
                }
            }

            if (shipment != null)
            {
                dataSources.Add("Shipments");
                response.ShipmentStatus = $"Lô vận chuyển quốc tế ({shipment.ShipmentCode}): {StatusNames.Of(shipment.Status ?? shipment.ShipmentStage)}";
            }
            else
            {
                response.ShipmentStatus = "Chưa ghép lô vận chuyển quốc tế";
            }

            var st = matchedOrder.Status ?? string.Empty;
            if (st.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
            {
                response.NextActionSuggestion = "Tạo báo giá tạm tính và gửi báo giá cho khách hàng.";
            }
            else if (st.Contains("QUOTATION", StringComparison.OrdinalIgnoreCase))
            {
                response.NextActionSuggestion = "Theo dõi phản hồi báo giá của khách hàng hoặc nhắc khách xác nhận báo giá.";
            }
            else if (st.Contains("APPROVED", StringComparison.OrdinalIgnoreCase) || st.Contains("CONFIRMED", StringComparison.OrdinalIgnoreCase))
            {
                response.NextActionSuggestion = "Theo dõi quá trình nhập kho hàng hoặc kiểm tra hóa đơn thanh toán.";
            }
            else if (st.Contains("ARRIVED", StringComparison.OrdinalIgnoreCase) || st.Contains("WAREHOUSE", StringComparison.OrdinalIgnoreCase))
            {
                response.NextActionSuggestion = "Hỗ trợ khách hàng tạo/duyệt yêu cầu xuất kho.";
            }
            else
            {
                response.NextActionSuggestion = "Kiểm tra chi tiết phản hồi của khách hàng hoặc lịch sử xử lý đơn hàng.";
            }
        }
        else if (matchedPurchaseRequest != null)
        {
            dataSources.Add("PurchaseRequests");
            matchedCustomer ??= matchedPurchaseRequest.CustomerId.HasValue
                ? await customerRepo.GetByIdAsync(matchedPurchaseRequest.CustomerId.Value)
                : null;

            response.RelatedOrders.Add(new SalesQueryOrderSummaryDto
            {
                OrderId = matchedPurchaseRequest.Id,
                OrderCode = matchedPurchaseRequest.PurchaseCode ?? string.Empty,
                OrderType = "PURCHASE",
                CustomerName = matchedCustomer?.FullName ?? matchedPurchaseRequest.ReceiverName ?? "N/A",
                CustomerPhone = matchedCustomer?.Phone ?? matchedPurchaseRequest.ReceiverPhone ?? "N/A",
                Status = matchedPurchaseRequest.Status ?? "PENDING",
                CreatedAt = matchedPurchaseRequest.CreatedAt ?? DateTime.UtcNow
            });

            response.CurrentStatus = matchedPurchaseRequest.Status ?? "PENDING";

            var pPayments = (await purchasePaymentRepo.FindAsync(p => p.PurchaseRequestId == matchedPurchaseRequest.Id)).ToList();
            if (pPayments.Any(p => p.Status != null && (p.Status.ToUpper() == "PAID" || p.Status.ToUpper() == "SUCCESS")))
            {
                dataSources.Add("PurchasePayments");
                response.PaymentStatus = "Đã thanh toán";
            }
            else if (pPayments.Any())
            {
                dataSources.Add("PurchasePayments");
                response.PaymentStatus = $"Đang chờ thanh toán ({StatusNames.Of(pPayments.First().Status)})";
            }
            else
            {
                response.PaymentStatus = "Chưa tạo thanh toán mua hộ";
            }

            response.WarehouseStatus = "Không áp dụng (đơn mua hộ)";
            response.ShipmentStatus = "Không áp dụng (đơn mua hộ)";

            var pst = matchedPurchaseRequest.Status ?? string.Empty;
            if (pst.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
            {
                response.NextActionSuggestion = "Duyệt yêu cầu mua hộ và tạo báo giá mua hộ cho khách hàng.";
            }
            else if (pst.Contains("QUOTED", StringComparison.OrdinalIgnoreCase))
            {
                response.NextActionSuggestion = "Nhắc khách hàng xác nhận báo giá và thực hiện thanh toán.";
            }
            else
            {
                response.NextActionSuggestion = "Theo dõi tiến độ thu mua sản phẩm cho khách hàng.";
            }
        }
        else if (matchedParcel != null)
        {
            dataSources.Add("Parcels");
            response.RelatedParcels.Add(new SalesQueryParcelSummaryDto
            {
                ParcelId = matchedParcel.Id,
                PackageCode = matchedParcel.PackageCode ?? string.Empty,
                Status = matchedParcel.PackageStatus ?? string.Empty,
                LocalTrackingCode = string.Empty,
                WeightKg = matchedParcel.ActualWeight ?? 0m
            });

            response.CurrentStatus = matchedParcel.PackageStatus ?? "PENDING";
            response.PaymentStatus = "Tham chiếu theo đơn hàng gốc";
            response.WarehouseStatus = $"Trạng thái kiện: {StatusNames.Quote(matchedParcel.PackageStatus)}";
            response.ShipmentStatus = "Vận chuyển theo đơn ký gửi liên quan";
            response.NextActionSuggestion = "Kiểm tra thông tin chi tiết kiện hàng và đơn ký gửi liên quan.";
        }

        var mainCode = response.RelatedOrders.FirstOrDefault()?.OrderCode ?? response.RelatedParcels.FirstOrDefault()?.PackageCode ?? "N/A";
        var custName = response.RelatedOrders.FirstOrDefault()?.CustomerName ?? matchedCustomer?.FullName ?? "Khách hàng";

        response.Answer = $"Thông tin tra cứu cho đơn/kiện hàng {mainCode} của khách hàng {custName}:\n" +
                          $"- Trạng thái hiện tại: {StatusNames.Of(response.CurrentStatus)}\n" +
                          $"- Trạng thái thanh toán: {StatusNames.Quote(response.PaymentStatus)}\n" +
                          $"- Trạng thái kho: {response.WarehouseStatus}\n" +
                          $"- Trạng thái vận chuyển: {response.ShipmentStatus}\n" +
                          $"- Gợi ý bước xử lý: {response.NextActionSuggestion}";

        response.DataSources = dataSources.Distinct().ToList();

        return response;
    }

    public async Task<ExtractOrderIntentResponseDto> ExtractCustomerOrderIntentAsync(CustomerOrderIntentRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException("Nội dung tin nhắn yêu cầu (message) không được để trống.");
        }

        var message = request.Message.Trim();

        Customer? customer = null;
        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            customer = await customerRepo.GetByIdAsync(request.CustomerId.Value);
        }

        try
        {
            var result = await CallOpenRouterForOrderIntentAsync(message, customer, request.PreferredShippingRoute);
            if (result != null && (!string.IsNullOrWhiteSpace(result.ProductName) || !string.IsNullOrWhiteSpace(result.ChinaTrackingCode) || !string.IsNullOrWhiteSpace(result.ProductLink)))
            {
                if (customer != null)
                {
                    result.ReceiverName = string.IsNullOrWhiteSpace(result.ReceiverName) ? customer.FullName : result.ReceiverName;
                    result.ReceiverPhone = string.IsNullOrWhiteSpace(result.ReceiverPhone) ? customer.Phone : result.ReceiverPhone;
                }
                return result;
            }
        }
        catch (Exception)
        {
            // Silent catch for fallback
        }

        var fallbackResult = BuildFallbackOrderIntent(message, customer, request.PreferredShippingRoute);
        fallbackResult.Warnings.Add("OpenRouter AI phản hồi không khả dụng. Đã tự động bóc tách dữ liệu theo bộ quy tắc Heuristic.");
        return fallbackResult;
    }

    private async Task<ExtractOrderIntentResponseDto?> CallOpenRouterForOrderIntentAsync(
        string message,
        Customer? customer,
        string? preferredRoute)
    {
        var apiKey = _configuration["OpenRouterSettings:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var model = _configuration["OpenRouterSettings:Model"] ?? "google/gemini-2.0-flash-001";
        var baseUrl = _configuration["OpenRouterSettings:BaseUrl"] ?? "https://openrouter.ai/api/v1/chat/completions";

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("YOUR_"))
        {
            return null;
        }

        string systemInstruction = @"Bạn là AI Agent Trợ lý Đặt hàng Logistics Xuyên biên giới Trung - Việt (VCL Logistics AI Assistant).
Nhiệm vụ của bạn là phân tích câu văn, tin nhắn của khách hàng và tự động bóc tách dữ liệu thành thông tin tạo đơn chuẩn hoá theo 2 LOẠI ĐƠN NGHIỆP VỤ:

1. ĐƠN KÝ GỬI (CONSIGNMENT):
   - Dấu hiệu: Khách cung cấp Mã vận đơn nội địa Trung Quốc (Mã tracking TQ dạng chữ/số như SF..., YT..., 78..., ST..., ZTO...), hoặc có các từ khoá 'ký gửi', 'gửi về kho TQ', 'mã tracking', 'mã vận đơn'.
   - Trường cần bóc tách: orderType = 'CONSIGNMENT', chinaTrackingCode, declaredValueCny, shippingRoute ('CN-VN-ROAD' hoặc 'CN-VN-AIR'), destinationWarehouse.

2. ĐƠN MUA HỘ (PURCHASE):
   - Dấu hiệu: Khách gửi Link sản phẩm (Taobao, 1688, Tmall, JD, Pinduoduo) hoặc các từ khoá 'mua hộ', 'order giúp', 'mua từ link', 'tìm hàng'.
   - Trường cần bóc tách: orderType = 'PURCHASE', productLink, variant (màu sắc/size), quantity, unitPriceCny.

3. THÔNG TIN CHUNG CẦN TRÍCH XUẤT:
   - productName: Tên sản phẩm chính (tiếng Việt hoặc tiếng Trung).
   - receiverName, receiverPhone, receiverAddress: Thông tin người nhận tại Việt Nam nếu khách có nhắc tới.
   - explanation: Tóm tắt 1-2 câu ngắn gọn bằng tiếng Việt giải thích AI đã hiểu yêu cầu của khách như thế nào.

BẮT BUỘC trả về đúng định dạng JSON theo cấu trúc sau:
{
  ""orderType"": ""CONSIGNMENT"",
  ""confidenceScore"": 0.95,
  ""explanation"": ""AI nhận diện khách hàng muốn tạo đơn Ký gửi với mã vận đơn TQ YT789654321..."",
  ""chinaTrackingCode"": ""YT789654321"",
  ""declaredValueCny"": 500,
  ""shippingRoute"": ""CN-VN-ROAD"",
  ""destinationWarehouse"": ""Kho Hà Nội"",
  ""productLink"": null,
  ""variant"": null,
  ""quantity"": 1,
  ""unitPriceCny"": null,
  ""productName"": ""Mỹ phẩm"",
  ""category"": ""Cosmetics"",
  ""receiverName"": ""Trần Văn A"",
  ""receiverPhone"": ""0987654321"",
  ""receiverAddress"": ""123 Nguyễn Trãi, Hà Nội"",
  ""note"": ""Giao giờ hành chính""
}";

        var promptPayload = new
        {
            CustomerMessage = message,
            CustomerProfile = customer != null ? new { customer.FullName, customer.Phone } : null,
            PreferredShippingRoute = preferredRoute
        };

        var requestBody = new
        {
            model = model,
            messages = new object[]
            {
                new { role = "system", content = systemInstruction },
                new { role = "user", content = $"Dữ liệu yêu cầu của khách hàng:\n{JsonSerializer.Serialize(promptPayload)}" }
            },
            response_format = new { type = "json_object" },
            temperature = 0.1
        };

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        requestMessage.Headers.TryAddWithoutValidation("HTTP-Referer", "https://logictic.site");
        requestMessage.Headers.TryAddWithoutValidation("X-Title", "VCL System");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        HttpResponseMessage httpResponse = await _httpClient.SendAsync(requestMessage, cts.Token);

        if (!httpResponse.IsSuccessStatusCode)
        {
            return null;
        }

        var responseString = await httpResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var textContent = choices[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(textContent))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<ExtractOrderIntentResponseDto>(
            textContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return result;
    }

    private static ExtractOrderIntentResponseDto BuildFallbackOrderIntent(
        string message,
        Customer? customer,
        string? preferredRoute)
    {
        var dto = new ExtractOrderIntentResponseDto
        {
            ReceiverName = customer?.FullName,
            ReceiverPhone = customer?.Phone,
            ShippingRoute = preferredRoute ?? "CN-VN-ROAD"
        };

        // Regex phát hiện Link sản phẩm (Taobao, 1688, Tmall...)
        var urlMatch = System.Text.RegularExpressions.Regex.Match(
            message,
            @"(https?://[^\s]+(taobao|1688|tmall|jd|pinduoduo)[^\s]*|https?://[^\s]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Regex phát hiện Mã vận đơn TQ (VD: SF123456789, YT987654321, 78654321...)
        var trackingMatch = System.Text.RegularExpressions.Regex.Match(
            message,
            @"\b(SF|YT|ST|ZTO|J&T|78\d|33\d)[A-Za-z0-9]{8,18}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Regex phát hiện Số điện thoại
        var phoneMatch = System.Text.RegularExpressions.Regex.Match(
            message,
            @"\b(0[3|5|7|8|9][0-9]{8})\b");

        if (phoneMatch.Success)
        {
            dto.ReceiverPhone = phoneMatch.Value;
        }

        // Regex phát hiện Số lượng
        var qtyMatch = System.Text.RegularExpressions.Regex.Match(
            message,
            @"(\d+)\s*(cái|chiếc|bộ|hộp|sp|món|item|kg|kiện)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (qtyMatch.Success && int.TryParse(qtyMatch.Groups[1].Value, out int qty))
        {
            dto.Quantity = qty;
        }

        // Regex phát hiện Giá tiền CNY (Tệ)
        var priceMatch = System.Text.RegularExpressions.Regex.Match(
            message,
            @"(\d+(\.\d+)?)\s*(tệ|cny|yüan|¥)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, out decimal price))
        {
            dto.UnitPriceCny = price;
            dto.DeclaredValueCny = price;
        }

        if (urlMatch.Success || message.Contains("mua hộ", StringComparison.OrdinalIgnoreCase) || message.Contains("order", StringComparison.OrdinalIgnoreCase))
        {
            dto.OrderType = "PURCHASE";
            dto.ProductLink = urlMatch.Success ? urlMatch.Value : null;
            dto.ProductName = "Sản phẩm mua hộ từ " + (dto.ProductLink != null ? "link web" : "yêu cầu khách");
            dto.Explanation = "Hệ thống tự động phát hiện yêu cầu Mua hộ qua link sản phẩm/từ khoá mua hộ.";
        }
        else
        {
            dto.OrderType = "CONSIGNMENT";
            dto.ChinaTrackingCode = trackingMatch.Success ? trackingMatch.Value : null;
            dto.ProductName = "Hàng ký gửi " + (dto.ChinaTrackingCode != null ? $"({dto.ChinaTrackingCode})" : "");
            dto.Explanation = "Hệ thống tự động phát hiện yêu cầu Ký gửi dựa trên mã vận đơn TQ/từ khoá ký gửi.";
        }

        return dto;
    }
}

