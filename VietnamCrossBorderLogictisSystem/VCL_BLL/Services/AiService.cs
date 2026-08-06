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

        // 5. Xác định Category của kiện hàng
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
                IsEntirelyEmpty = parcelsInBin.Count == 0
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
                response.Warnings.Add("Kho có sơ đồ layout nhưng không có ô kệ nào đang hoạt động (IsActive).");
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

        // 8. Gọi Gemini AI để lấy đánh giá & xếp hạng gợi ý
        try
        {
            var geminiRecommendations = await CallGeminiForPutawayAsync(parcel, category, candidateBins, request.PreferredZoneId);

            foreach (var rec in geminiRecommendations)
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

            // Nếu Gemini trả về danh sách có vị trí gợi ý thì sắp xếp Top 5
            if (response.SuggestedLocations.Any())
            {
                response.SuggestedLocations = response.SuggestedLocations
                    .OrderByDescending(s => s.ConfidenceScore)
                    .Take(5)
                    .ToList();
            }
            else
            {
                // Trường hợp Gemini trả rỗng: Fallback sang Heuristic
                response.Warnings.Add("Gemini AI không trả về gợi ý cụ thể. Đã sử dụng thuật toán tính toán sức chứa còn trống.");
                response.SuggestedLocations = BuildFallbackSuggestions(candidateBins, request.PreferredZoneId);
            }
        }
        catch (Exception ex)
        {
            // Fallback an toàn nếu Gemini bị lỗi (timeout, hết quota, mất mạng)
            response.Warnings.Add($"Không thể kết nối Gemini AI ({ex.Message}). Đang sử dụng thuật toán tính toán sức chứa còn trống.");
            response.SuggestedLocations = BuildFallbackSuggestions(candidateBins, request.PreferredZoneId);
        }

        // 9. Cảnh báo nếu PreferredZoneId không có ô khả dụng
        if (request.PreferredZoneId.HasValue && !candidateBins.Any(c => c.ZoneId == request.PreferredZoneId.Value))
        {
            response.Warnings.Add("Khu vực yêu cầu (preferredZoneId) không có vị trí trống hoặc khả dụng. Đã đề xuất các khu vực khác.");
        }

        return response;
    }

    private async Task<List<GeminiBinRecommendation>> CallGeminiForPutawayAsync(
        Parcel parcel,
        string category,
        List<CandidateBinDto> candidateBins,
        Guid? preferredZoneId)
    {
        var apiKey = _configuration["GeminiSettings:ApiKey"];
        var model = _configuration["GeminiSettings:Model"] ?? "gemini-1.5-flash";
        var baseUrl = _configuration["GeminiSettings:BaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta/models/";

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("YOUR_"))
        {
            throw new InvalidOperationException("Gemini API Key chưa được cấu hình hợp lệ.");
        }

        var url = $"{baseUrl}{model}:generateContent?key={apiKey}";

        // Tối ưu Payload gọn nhẹ để gửi Gemini
        var promptPayload = new
        {
            Parcel = new
            {
                ParcelId = parcel.Id.ToString(),
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
                FreeSpacePercent = Math.Round((1m - b.OccupancyRatio) * 100, 1)
            })
        };

        string systemInstruction = @"Bạn là Chuyên gia AI Quản lý Kho Vận (Logistics WMS Expert).
Nhiệm vụ của bạn là phân tích kiện hàng và danh sách các ô kệ khả dụng trong kho, sau đó chọn ra các ô vị trí xếp dỡ tối ưu nhất.
Quy tắc đề xuất:
1. Hàng nặng (>10kg) ưu tiên xếp ô kệ có tải trọng dư dả, tầng thấp hoặc tầng giữa.
2. Hàng nhẹ/nhỏ ưu tiên tối ưu khoảng trống ô kệ.
3. Nếu kiện hàng thuộc PreferredZoneId, cộng thêm điểm ưu tiên.
4. Giải thích ngắn gọn, rõ ràng lý do đề xuất bằng tiếng Việt tự nhiên cho nhân viên kho.
5. BẮT BUỘC trả về đúng định dạng JSON theo cấu trúc sau:
{
  ""recommendations"": [
    {
      ""binId"": ""guid-string-trùng-với-AvailableBins"",
      ""confidenceScore"": 0.95,
      ""reason"": ""Lý do chi tiết bằng tiếng Việt""
    }
  ]
}";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = $"{systemInstruction}\n\nDữ liệu đầu vào:\n{JsonSerializer.Serialize(promptPayload)}" }
                    }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.2
            }
        };

        var client = _httpClient;

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var httpResponse = await client.PostAsync(url, jsonContent);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errText = await httpResponse.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Lỗi kết nối Gemini API (Mã {httpResponse.StatusCode}): {errText}");
        }

        var responseString = await httpResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        var textContent = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
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
        List<CandidateBinDto> candidateBins,
        Guid? preferredZoneId)
    {
        var suggestions = new List<SuggestedLocationDto>();

        foreach (var cand in candidateBins)
        {
            decimal spaceScore = 1m - cand.OccupancyRatio;
            decimal confidenceScore = 0.30m + (0.60m * spaceScore);
            string reason = cand.IsEntirelyEmpty
                ? "Ô kệ trống hoàn toàn, sức chứa dư dả để xếp dỡ."
                : $"Ô kệ còn trống {spaceScore * 100:0.0}% sức chứa.";

            if (preferredZoneId.HasValue && cand.ZoneId == preferredZoneId.Value)
            {
                confidenceScore += 0.1m;
                reason = $"Thuộc khu vực ưu tiên. {reason}";
            }

            suggestions.Add(new SuggestedLocationDto
            {
                LocationId = cand.LayoutId,
                WarehouseId = cand.WarehouseId,
                ZoneId = cand.ZoneId,
                ShelfId = cand.ShelfId,
                BinId = cand.BinId,
                DisplayLabel = cand.DisplayLabel,
                ConfidenceScore = Math.Round(Math.Min(1.0m, confidenceScore), 4),
                Reason = reason
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

        var allOrders = await orderRepo.GetAllAsync();
        var allPurchaseRequests = await purchaseRequestRepo.GetAllAsync();
        var allCustomers = await customerRepo.GetAllAsync();
        var allParcels = await parcelRepo.GetAllAsync();
        var allInvoices = await invoiceRepo.GetAllAsync();
        var allPayments = await paymentRepo.GetAllAsync();
        var allPurchasePayments = await purchasePaymentRepo.GetAllAsync();
        var allReceivingNotes = await receivingNoteRepo.GetAllAsync();
        var allWroRequests = await wroRequestRepo.GetAllAsync();
        var allShipments = await shipmentRepo.GetAllAsync();
        var allQuotations = await quotationRepo.GetAllAsync();

        Order? matchedOrder = null;
        PurchaseRequest? matchedPurchaseRequest = null;
        Customer? matchedCustomer = null;
        Parcel? matchedParcel = null;

        var dataSources = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.OrderCode))
        {
            var code = request.OrderCode.Trim();
            matchedOrder = allOrders.FirstOrDefault(o => string.Equals(o.ConsignmentCode, code, StringComparison.OrdinalIgnoreCase));
            matchedPurchaseRequest = allPurchaseRequests.FirstOrDefault(p => string.Equals(p.PurchaseCode, code, StringComparison.OrdinalIgnoreCase));
            matchedParcel = allParcels.FirstOrDefault(p => string.Equals(p.PackageCode, code, StringComparison.OrdinalIgnoreCase));
        }

        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            matchedCustomer = allCustomers.FirstOrDefault(c => c.Id == request.CustomerId.Value);
        }

        if (request.RelatedId.HasValue && request.RelatedId.Value != Guid.Empty)
        {
            if (string.Equals(request.RelatedType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase))
            {
                matchedOrder = allOrders.FirstOrDefault(o => o.Id == request.RelatedId.Value);
            }
            else if (string.Equals(request.RelatedType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
            {
                matchedPurchaseRequest = allPurchaseRequests.FirstOrDefault(p => p.Id == request.RelatedId.Value);
            }
            else if (string.Equals(request.RelatedType, "PARCEL", StringComparison.OrdinalIgnoreCase))
            {
                matchedParcel = allParcels.FirstOrDefault(p => p.Id == request.RelatedId.Value);
            }
            else if (string.Equals(request.RelatedType, "CUSTOMER", StringComparison.OrdinalIgnoreCase))
            {
                matchedCustomer = allCustomers.FirstOrDefault(c => c.Id == request.RelatedId.Value);
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
                matchedOrder ??= allOrders.FirstOrDefault(o =>
                    (!string.IsNullOrEmpty(o.ConsignmentCode) && string.Equals(o.ConsignmentCode, token, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(o.ReceiverPhone) && o.ReceiverPhone.Contains(token)) ||
                    (!string.IsNullOrEmpty(o.ReceiverName) && o.ReceiverName.Contains(token, StringComparison.OrdinalIgnoreCase)));

                matchedPurchaseRequest ??= allPurchaseRequests.FirstOrDefault(p =>
                    (!string.IsNullOrEmpty(p.PurchaseCode) && string.Equals(p.PurchaseCode, token, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(p.ReceiverPhone) && p.ReceiverPhone.Contains(token)) ||
                    (!string.IsNullOrEmpty(p.ReceiverName) && p.ReceiverName.Contains(token, StringComparison.OrdinalIgnoreCase)));

                matchedParcel ??= allParcels.FirstOrDefault(p =>
                    (!string.IsNullOrEmpty(p.PackageCode) && string.Equals(p.PackageCode, token, StringComparison.OrdinalIgnoreCase)));

                matchedCustomer ??= allCustomers.FirstOrDefault(c =>
                    (!string.IsNullOrEmpty(c.Phone) && c.Phone.Contains(token)) ||
                    (!string.IsNullOrEmpty(c.FullName) && c.FullName.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(c.Email) && c.Email.Contains(token, StringComparison.OrdinalIgnoreCase)));

                if (matchedOrder != null || matchedPurchaseRequest != null || matchedParcel != null || matchedCustomer != null)
                    break;
            }
        }

        if (matchedParcel != null && matchedOrder == null && matchedParcel.ConsignmentId.HasValue)
        {
            matchedOrder = allOrders.FirstOrDefault(o => o.Id == matchedParcel.ConsignmentId.Value);
        }

        if (matchedCustomer != null && matchedOrder == null && matchedPurchaseRequest == null)
        {
            matchedOrder = allOrders.Where(o => o.CustomerId == matchedCustomer.Id)
                                    .OrderByDescending(o => o.CreatedAt)
                                    .FirstOrDefault();

            matchedPurchaseRequest ??= allPurchaseRequests.Where(p => p.CustomerId == matchedCustomer.Id)
                                                          .OrderByDescending(p => p.CreatedAt)
                                                          .FirstOrDefault();
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
                ? allCustomers.FirstOrDefault(c => c.Id == matchedOrder.CustomerId.Value)
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

            var orderParcels = allParcels.Where(p => p.ConsignmentId == matchedOrder.Id).ToList();
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

            var orderQuotationIds = allQuotations.Where(q => q.OrderId == matchedOrder.Id).Select(q => q.Id).ToList();
            var orderInvoice = allInvoices.FirstOrDefault(i => i.QuotationId.HasValue && orderQuotationIds.Contains(i.QuotationId.Value));
            if (orderInvoice != null)
            {
                dataSources.Add("Invoices");
                var invoicePayments = allPayments.Where(p => p.InvoiceId == orderInvoice.Id && string.Equals(p.PaymentStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase)).ToList();
                var totalPaid = invoicePayments.Sum(p => p.Amount);
                var totalInvoiceAmount = orderInvoice.TotalAmount ?? 0m;

                if (totalPaid >= totalInvoiceAmount && totalInvoiceAmount > 0)
                {
                    response.PaymentStatus = "Đã thanh toán đủ (PAID)";
                }
                else if (totalPaid > 0)
                {
                    response.PaymentStatus = $"Đã thanh toán một phần ({totalPaid:N0}/{totalInvoiceAmount:N0}đ)";
                }
                else
                {
                    response.PaymentStatus = $"Chưa thanh toán ({orderInvoice.InvoiceStatus ?? "UNPAID"})";
                }
            }
            else
            {
                response.PaymentStatus = "Chưa phát hành hóa đơn thanh toán";
            }

            var note = allReceivingNotes.FirstOrDefault(n => n.ConsignmentOrderId == matchedOrder.Id);
            var release = matchedCustomer != null ? allWroRequests.FirstOrDefault(r => r.CustomerId == matchedCustomer.Id) : null;
            if (release != null)
            {
                dataSources.Add("WroRequests");
                response.WarehouseStatus = $"Yêu cầu xuất kho ({release.WroCode}): {release.Status}";
            }
            else if (note != null)
            {
                dataSources.Add("WarehouseReceivingNotes");
                response.WarehouseStatus = $"Đã tiếp nhận kho ({note.ReceivingNoteCode}): {note.Status}";
            }
            else
            {
                response.WarehouseStatus = "Chưa nhập kho";
            }

            var parcelIds = orderParcels.Select(p => p.Id).ToList();
            var shipment = allShipments.FirstOrDefault(s => s.ShipmentParcels.Any(sp => parcelIds.Contains(sp.ParcelId)));
            if (shipment != null)
            {
                dataSources.Add("Shipments");
                response.ShipmentStatus = $"Lô vận chuyển quốc tế ({shipment.ShipmentCode}): {shipment.ShipmentStage ?? shipment.Status}";
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
                ? allCustomers.FirstOrDefault(c => c.Id == matchedPurchaseRequest.CustomerId.Value)
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

            var pPayments = allPurchasePayments.Where(p => p.PurchaseRequestId == matchedPurchaseRequest.Id).ToList();
            if (pPayments.Any(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase)))
            {
                dataSources.Add("PurchasePayments");
                response.PaymentStatus = "Đã thanh toán (PAID)";
            }
            else if (pPayments.Any())
            {
                dataSources.Add("PurchasePayments");
                response.PaymentStatus = $"Đang chờ thanh toán ({pPayments.First().Status})";
            }
            else
            {
                response.PaymentStatus = "Chưa tạo thanh toán mua hộ";
            }

            response.WarehouseStatus = "N/A (Đơn mua hộ)";
            response.ShipmentStatus = "N/A (Đơn mua hộ)";

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
            response.WarehouseStatus = $"Trạng thái kiện: {matchedParcel.PackageStatus}";
            response.ShipmentStatus = "Vận chuyển theo đơn ký gửi liên quan";
            response.NextActionSuggestion = "Kiểm tra thông tin chi tiết kiện hàng và đơn ký gửi liên quan.";
        }

        var mainCode = response.RelatedOrders.FirstOrDefault()?.OrderCode ?? response.RelatedParcels.FirstOrDefault()?.PackageCode ?? "N/A";
        var custName = response.RelatedOrders.FirstOrDefault()?.CustomerName ?? matchedCustomer?.FullName ?? "Khách hàng";

        response.Answer = $"Thông tin tra cứu cho đơn/kiện hàng {mainCode} của khách hàng {custName}:\n" +
                          $"- Trạng thái hiện tại: {response.CurrentStatus}\n" +
                          $"- Trạng thái thanh toán: {response.PaymentStatus}\n" +
                          $"- Trạng thái kho: {response.WarehouseStatus}\n" +
                          $"- Trạng thái vận chuyển: {response.ShipmentStatus}\n" +
                          $"- Gợi ý bước xử lý: {response.NextActionSuggestion}";

        response.DataSources = dataSources.Distinct().ToList();

        return response;
    }
}
