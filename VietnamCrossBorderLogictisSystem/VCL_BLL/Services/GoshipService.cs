using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class GoshipService : IGoshipService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoshipService> _logger;
        private readonly IUnitOfWork _unitOfWork;

        public GoshipService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GoshipService> logger,
            IUnitOfWork unitOfWork)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _unitOfWork = unitOfWork;
        }

        public async Task<List<GoshipRateOption>> GetRatesAsync(GoshipRateRequest request, string token)
        {
            var baseUrl = _configuration["GoshipSettings:ApiUrl"] ?? "https://api.goship.io/api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/rates";

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Content = JsonContent.Create(request);

            _logger.LogInformation("Sending Get Rates request to GoShip API: {Endpoint}", endpoint);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GoShip Rates API error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GoShip Rates Error: {responseContent}");
            }

            var result = JsonSerializer.Deserialize<GoshipRateResponse>(responseContent);
            if (result == null || result.Data == null)
            {
                throw new InvalidOperationException("GoShip Rates API returned invalid response.");
            }

            return result.Data;
        }

        public async Task<GoshipOrderResponseData> CreateOrderAsync(GoshipOrderRequest request, string token)
        {
            var baseUrl = _configuration["GoshipSettings:ApiUrl"] ?? "https://api.goship.io/api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/shipments";

            // Đơn trong hệ thống: lấy sẵn người nhận và danh sách kiện trước khi gọi sang GoShip.
            var context = await ResolveOrderContextAsync(request);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Chỉ đẩy phần "shipment" sang GoShip. orderId/parcelIds là tham số nội bộ của mình,
            // gửi kèm sẽ làm GoShip từ chối payload.
            httpRequest.Content = JsonContent.Create(new { shipment = request.Shipment });

            _logger.LogInformation("Sending Create Shipment request to GoShip API: {Endpoint}", endpoint);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GoShip Create Order API error {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GoShip Create Order Error: {responseContent}");
            }

            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var orderData = new GoshipOrderResponseData();

            // 1. Try to read from "data" property if it's a JSON Object
            if (root.TryGetProperty("data", out var dataElem) && dataElem.ValueKind == JsonValueKind.Object)
            {
                if (dataElem.TryGetProperty("id", out var idElem)) orderData.Id = GetJsonStringValue(idElem);
                if (dataElem.TryGetProperty("code", out var codeElem)) orderData.Code = GetJsonStringValue(codeElem);
                if (dataElem.TryGetProperty("carrier_name", out var cnElem)) orderData.CarrierName = GetJsonStringValue(cnElem);
                else if (dataElem.TryGetProperty("carrier", out var cElem)) orderData.CarrierName = GetJsonStringValue(cElem);
                if (dataElem.TryGetProperty("fee", out var feeElem) && feeElem.ValueKind == JsonValueKind.Number) orderData.Fee = feeElem.GetDecimal();
            }
            else
            {
                // 2. Fallback to reading directly from root object (Goship v2 shipments response format)
                if (root.TryGetProperty("id", out var idElem)) orderData.Id = GetJsonStringValue(idElem);

                if (root.TryGetProperty("code", out var codeElem))
                {
                    var codeVal = GetJsonStringValue(codeElem);
                    orderData.Code = (codeVal == "200" || string.IsNullOrEmpty(codeVal)) ? orderData.Id : codeVal;
                }
                else
                {
                    orderData.Code = orderData.Id;
                }

                if (root.TryGetProperty("carrier_name", out var cnElem)) orderData.CarrierName = GetJsonStringValue(cnElem);
                else if (root.TryGetProperty("carrier", out var cElem)) orderData.CarrierName = GetJsonStringValue(cElem);

                if (root.TryGetProperty("fee", out var feeElem) && feeElem.ValueKind == JsonValueKind.Number) orderData.Fee = feeElem.GetDecimal();
            }

            if (string.IsNullOrEmpty(orderData.Id) && string.IsNullOrEmpty(orderData.Code))
            {
                var msg = root.TryGetProperty("message", out var mElem) ? GetJsonStringValue(mElem) : "Unknown Error";
                _logger.LogError("GoShip Order Creation failed: {Message}", msg);
                throw new InvalidOperationException($"GoShip Create Order Failed: {msg}");
            }

            _logger.LogInformation("GoShip Order Created Successfully: {Code} (ID: {Id})", orderData.Code, orderData.Id);

            await SaveWaybillsAsync(context, orderData);

            return orderData;
        }

        /// <summary>
        /// Nối yêu cầu tạo vận đơn với đơn hàng trong hệ thống: điền người nhận còn thiếu và
        /// chốt danh sách kiện đem giao. Không truyền orderId thì trả về ngữ cảnh rỗng.
        /// </summary>
        private async Task<GoshipOrderContext> ResolveOrderContextAsync(GoshipOrderRequest request)
        {
            var context = new GoshipOrderContext();
            if (request.OrderId is not Guid orderId || orderId == Guid.Empty)
            {
                return context;
            }

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new InvalidOperationException($"Không tìm thấy đơn hàng {orderId} để tạo vận đơn giao nội địa.");

            context.Order = order;

            await EnsureFinalPaymentSettledAsync(order);

            // Người nhận lấy thẳng từ đơn, khỏi bắt người dùng gõ lại. FE gửi lên thì tôn trọng
            // giá trị FE gửi (có trường hợp khách đổi địa chỉ nhận vào phút chót).
            var to = request.Shipment.AddressTo;
            if (string.IsNullOrWhiteSpace(to.Name)) to.Name = order.ReceiverName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(to.Phone)) to.Phone = order.ReceiverPhone ?? string.Empty;
            if (string.IsNullOrWhiteSpace(to.Street)) to.Street = order.ReceiverAddress ?? string.Empty;

            // Mã đơn của mình để sau này tra ngược vận đơn từ phía GoShip.
            if (string.IsNullOrWhiteSpace(request.Shipment.OrderId))
            {
                request.Shipment.OrderId = order.ConsignmentCode;
            }

            var allParcels = await _unitOfWork.Repository<Parcel>().GetAllAsync();
            var orderParcels = allParcels.Where(p => p.ConsignmentId == order.Id).ToList();

            if (request.ParcelIds != null && request.ParcelIds.Count > 0)
            {
                var wanted = request.ParcelIds.ToHashSet();
                var picked = orderParcels.Where(p => wanted.Contains(p.Id)).ToList();

                var missing = wanted.Except(picked.Select(p => p.Id)).ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Các kiện sau không thuộc đơn {order.ConsignmentCode}: {string.Join(", ", missing)}.");
                }

                context.Parcels = picked;
            }
            else
            {
                context.Parcels = orderParcels;
            }

            if (context.Parcels.Count == 0)
            {
                throw new InvalidOperationException($"Đơn {order.ConsignmentCode} chưa có kiện hàng nào để giao.");
            }

            return context;
        }

        /// <summary>
        /// Cổng chặn: khách phải trả xong đợt cuối thì mới gọi GoShip đến lấy hàng.
        /// Không có chốt này thì hàng rời kho trong khi tiền chưa về.
        /// </summary>
        private async Task EnsureFinalPaymentSettledAsync(Order order)
        {
            var quotationIds = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == order.Id)
                .Select(q => q.Id)
                .ToHashSet();

            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();

            if (invoiceIds.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Đơn {order.ConsignmentCode} chưa có hoá đơn nên chưa thể gọi GoShip đến lấy hàng.");
            }

            var payments = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Where(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value))
                .ToList();

            var finalSettled = payments.Any(p =>
                string.Equals(p.InstallmentType, "FINAL_PAYMENT", StringComparison.OrdinalIgnoreCase)
                && IsSettled(p.PaymentStatus));

            if (finalSettled)
            {
                return;
            }

            var hasPendingFinal = payments.Any(p =>
                string.Equals(p.InstallmentType, "FINAL_PAYMENT", StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.PaymentStatus, "PENDING", StringComparison.OrdinalIgnoreCase));

            throw new InvalidOperationException(hasPendingFinal
                ? $"Đơn {order.ConsignmentCode} còn đợt thanh toán cuối đang chờ khách trả. Trả xong mới gọi GoShip được."
                : $"Đơn {order.ConsignmentCode} chưa có thanh toán cuối. Tạo đợt thanh toán cuối và đợi khách trả trước đã.");
        }

        private static bool IsSettled(string? paymentStatus) =>
            paymentStatus?.Trim().ToUpperInvariant() is "PAID" or "SUCCESS" or "COMPLETED";

        /// <summary>
        /// Lưu vận đơn nội địa cho từng kiện. Thiếu bước này thì webhook và nút đồng bộ đều
        /// tra không ra vận đơn, kiện không bao giờ chuyển sang đã giao.
        /// </summary>
        private async Task SaveWaybillsAsync(GoshipOrderContext context, GoshipOrderResponseData orderData)
        {
            if (context.Order == null || context.Parcels.Count == 0)
            {
                return;
            }

            var trackingCode = !string.IsNullOrWhiteSpace(orderData.Code) ? orderData.Code : orderData.Id;
            if (string.IsNullOrWhiteSpace(trackingCode))
            {
                _logger.LogWarning("GoShip không trả về mã vận đơn cho đơn {OrderCode}, bỏ qua bước lưu vận đơn.",
                    context.Order.ConsignmentCode);
                return;
            }

            var waybillRepo = _unitOfWork.Repository<DomesticWaybill>();
            var existing = (await waybillRepo.GetAllAsync())
                .Where(w => w.TrackingCode == trackingCode)
                .Select(w => w.ParcelId)
                .ToHashSet();

            var now = DateTime.UtcNow;
            // Phí GoShip trả về là phí của cả lần giao, chia đều cho từng kiện để cộng vào
            // báo giá không bị nhân lên theo số kiện.
            var feePerParcel = context.Parcels.Count == 0
                ? 0m
                : Math.Round(orderData.Fee / context.Parcels.Count, 0);

            foreach (var parcel in context.Parcels)
            {
                if (existing.Contains(parcel.Id))
                {
                    continue;
                }

                await waybillRepo.AddAsync(new DomesticWaybill
                {
                    Id = Guid.NewGuid(),
                    ParcelId = parcel.Id,
                    CarrierName = orderData.CarrierName,
                    ActualWeight = parcel.ActualWeight,
                    TrackingCode = trackingCode,
                    DeliveryFee = feePerParcel,
                    CreatedAt = now
                });
            }

            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Đã lưu {Count} vận đơn nội địa cho đơn {OrderCode}, mã GoShip {TrackingCode}.",
                context.Parcels.Count, context.Order.ConsignmentCode, trackingCode);
        }

        private sealed class GoshipOrderContext
        {
            public Order? Order { get; set; }
            public List<Parcel> Parcels { get; set; } = new();
        }

        private static string GetJsonStringValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                _ => string.Empty
            };
        }

        public async Task<bool> ProcessWebhookAsync(GoshipWebhookPayload payload)
        {
            _logger.LogInformation("Received GoShip Webhook update - TrackingCode: {Code}, Status: {StatusText} ({Status})",
                payload.Code, payload.StatusText, payload.Status);

            if (string.IsNullOrEmpty(payload.Code))
            {
                return false;
            }

            var result = await ApplyShipmentStatusAsync(payload.Code, payload.Status, payload.StatusText);
            return result != null;
        }

        public async Task<GoshipSyncResultDto> SyncShipmentStatusAsync(string trackingCode, string token)
        {
            if (string.IsNullOrWhiteSpace(trackingCode))
            {
                throw new ArgumentException("Vui lòng nhập mã vận đơn cần đồng bộ.");
            }

            trackingCode = trackingCode.Trim();

            var baseUrl = _configuration["GoshipSettings:ApiUrl"] ?? "https://api.goship.io/api/v2/";
            var endpoint = $"{baseUrl.TrimEnd('/')}/shipments/search?code={Uri.EscapeDataString(trackingCode)}";

            var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GoShip tra trạng thái lỗi {StatusCode}: {Content}", response.StatusCode, responseContent);
                throw new InvalidOperationException($"GoShip không tra được vận đơn {trackingCode}: {responseContent}");
            }

            var (statusCode, statusText) = ReadStatusFromSearchResponse(responseContent);
            if (statusCode == 0)
            {
                throw new InvalidOperationException(
                    $"GoShip không trả về trạng thái cho vận đơn {trackingCode}. Kiểm tra lại mã vận đơn.");
            }

            var applied = await ApplyShipmentStatusAsync(trackingCode, statusCode, statusText)
                ?? throw new InvalidOperationException(
                    $"Không tìm thấy vận đơn {trackingCode} trong hệ thống. Vận đơn phải được tạo qua /api/goship/orders thì mới đồng bộ được.");

            applied.SyncedAt = DateTime.UtcNow;
            return applied;
        }

        /// <summary>
        /// Đọc <c>status</c>/<c>status_code</c> và <c>status_text</c> từ kết quả tra cứu.
        /// GoShip trả khi thì object, khi thì mảng trong <c>data</c> nên phải dò cả hai kiểu.
        /// </summary>
        private static (int StatusCode, string StatusText) ReadStatusFromSearchResponse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement target = root;
            if (root.TryGetProperty("data", out var data))
            {
                target = data.ValueKind == JsonValueKind.Array
                    ? (data.GetArrayLength() > 0 ? data[0] : default)
                    : data;
            }

            if (target.ValueKind != JsonValueKind.Object)
            {
                return (0, string.Empty);
            }

            int code = 0;
            foreach (var name in new[] { "status_code", "status" })
            {
                if (!target.TryGetProperty(name, out var el)) continue;

                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsed)) { code = parsed; break; }
                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var parsedText)) { code = parsedText; break; }
            }

            var text = target.TryGetProperty("status_text", out var textEl) ? GetJsonStringValue(textEl) : string.Empty;
            return (code, text);
        }

        /// <summary>
        /// Bảng mã trạng thái GoShip → trạng thái kiện hàng trong hệ thống.
        /// <para>
        /// Trước đây code coi 901 là đã giao, nhưng 901 là "Chờ lấy hàng" — GoShip mới nhận đơn,
        /// chưa cho xe đi lấy. Mã giao thành công là 905 (và 913 khi đã hoàn tất đối soát).
        /// </para>
        /// </summary>
        private static readonly Dictionary<int, string> GoshipStatusToPackageStatus = new()
        {
            [900] = "AWAITING_PICKUP",       // Đơn mới
            [901] = "AWAITING_PICKUP",       // Chờ lấy hàng
            [902] = "PICKING_UP",            // Đang lấy hàng
            [903] = "PICKED_UP",             // Đã lấy hàng
            [904] = "OUT_FOR_DELIVERY",      // Đang giao
            [905] = "DELIVERED",             // Giao thành công
            [906] = "DELIVERY_FAILED",       // Giao thất bại
            [907] = "RETURNING",             // Đang chuyển hoàn
            [908] = "RETURNED",              // Đã chuyển hoàn
            [909] = "DELIVERED",             // Đã đối soát với hãng — chỉ xảy ra sau khi giao xong
            [910] = "DELIVERED",             // Đã đối soát với khách
            [911] = "DELIVERED",             // Đã trả COD
            [912] = "DELIVERED",             // Chờ thanh toán COD
            [913] = "DELIVERED",             // Hoàn thành
            [914] = "DELIVERY_CANCELLED",    // Đơn huỷ
            [915] = "DELIVERY_DELAYED",      // Chậm lấy/giao
            [916] = "PARTIALLY_DELIVERED",   // Giao một phần
            [917] = "LOST",                  // Thất lạc
            [918] = "AT_CARRIER_WAREHOUSE",  // Đang lưu kho hãng
            [919] = "IN_TRANSIT",            // Đang vận chuyển
            [1000] = "DELIVERY_ERROR"        // Đơn lỗi
        };

        /// <summary>Những trạng thái cần người thật vào xử lý, không tự chạy tiếp được.</summary>
        private static readonly HashSet<string> NeedsHumanAttention = new()
        {
            "DELIVERY_FAILED", "RETURNING", "RETURNED", "LOST", "DELIVERY_ERROR", "DELIVERY_CANCELLED"
        };

        /// <summary>
        /// Áp trạng thái GoShip lên toàn bộ kiện của một vận đơn. Dùng chung cho webhook và cho
        /// nút đồng bộ để hai đường không bao giờ hiểu khác nhau về cùng một mã trạng thái.
        /// </summary>
        private async Task<GoshipSyncResultDto?> ApplyShipmentStatusAsync(string trackingCode, int statusCode, string? statusText)
        {
            var waybillRepo = _unitOfWork.Repository<DomesticWaybill>();
            var waybills = (await waybillRepo.GetAllAsync())
                .Where(w => w.TrackingCode == trackingCode && w.ParcelId.HasValue)
                .ToList();

            if (waybills.Count == 0)
            {
                _logger.LogWarning("Không tìm thấy vận đơn nội địa nào mang mã {TrackingCode}.", trackingCode);
                return null;
            }

            if (!GoshipStatusToPackageStatus.TryGetValue(statusCode, out var packageStatus))
            {
                // Mã lạ thì giữ nguyên trạng thái kiện, chỉ ghi log — thà không biết còn hơn
                // đoán sai rồi đóng đơn khi hàng chưa tới tay khách.
                _logger.LogWarning("GoShip trả mã trạng thái lạ {StatusCode} ({StatusText}) cho vận đơn {TrackingCode}.",
                    statusCode, statusText, trackingCode);
                return new GoshipSyncResultDto
                {
                    TrackingCode = trackingCode,
                    StatusCode = statusCode,
                    StatusText = statusText ?? string.Empty,
                    PackageStatus = "UNCHANGED",
                    UpdatedParcels = 0
                };
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var touchedOrderIds = new HashSet<Guid>();
            var updated = 0;

            foreach (var waybill in waybills)
            {
                var parcel = allParcels.FirstOrDefault(p => p.Id == waybill.ParcelId!.Value);
                if (parcel == null) continue;

                parcel.PackageStatus = packageStatus;
                parcelRepo.Update(parcel);
                updated++;

                if (parcel.ConsignmentId.HasValue)
                {
                    touchedOrderIds.Add(parcel.ConsignmentId.Value);
                }
            }

            string? orderStatus = null;
            var orderRepo = _unitOfWork.Repository<Order>();

            foreach (var orderId in touchedOrderIds)
            {
                var order = await orderRepo.GetByIdAsync(orderId);
                if (order == null) continue;

                var orderParcels = allParcels.Where(p => p.ConsignmentId == order.Id).ToList();

                // Giao xong thì đơn dừng ở DELIVERED. COMPLETED là của bước OM xác nhận cuối
                // luồng, không phải của hãng vận chuyển.
                if (orderParcels.Count > 0 && orderParcels.All(p =>
                        string.Equals(p.PackageStatus, "DELIVERED", StringComparison.OrdinalIgnoreCase)))
                {
                    order.Status = "DELIVERED";
                    // Mốc bắt đầu đếm thời hạn chờ khách xác nhận; hết hạn thì OM chốt thay.
                    order.DeliveredAt ??= DateTime.UtcNow;
                    orderRepo.Update(order);
                    orderStatus = order.Status;
                }
                else if (NeedsHumanAttention.Contains(packageStatus))
                {
                    await NotifyStaffAsync(order, packageStatus, statusText);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Vận đơn {TrackingCode}: {StatusCode} ({StatusText}) → {PackageStatus}, cập nhật {Count} kiện.",
                trackingCode, statusCode, statusText, packageStatus, updated);

            return new GoshipSyncResultDto
            {
                TrackingCode = trackingCode,
                StatusCode = statusCode,
                StatusText = statusText ?? string.Empty,
                PackageStatus = packageStatus,
                OrderStatus = orderStatus,
                UpdatedParcels = updated
            };
        }

        /// <summary>
        /// Giao hỏng, thất lạc, chuyển hoàn thì hệ thống dừng lại và gọi người thật vào xử lý —
        /// phần đấu giá / hoàn hàng là nghiệp vụ riêng, cố tình không tự động hoá.
        /// </summary>
        private async Task NotifyStaffAsync(Order order, string packageStatus, string? statusText)
        {
            var recipients = new List<Guid>();
            if (order.CreatedById.HasValue) recipients.Add(order.CreatedById.Value);

            var customer = order.CustomerId.HasValue
                ? await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value)
                : null;
            if (customer?.UserId != null && customer.UserId != Guid.Empty) recipients.Add(customer.UserId.Value);

            var notificationRepo = _unitOfWork.Repository<Notification>();
            foreach (var userId in recipients.Distinct())
            {
                await notificationRepo.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = $"Giao hàng cần xử lý — đơn {order.ConsignmentCode}",
                    Content = $"GoShip báo trạng thái \"{statusText}\" ({packageStatus}) cho đơn {order.ConsignmentCode}. "
                            + "Đơn đang dừng chờ nhân viên xử lý thủ công.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }
}
