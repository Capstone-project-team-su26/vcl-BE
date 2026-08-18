using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Hai hàng đợi việc của Sale ở chặng cuối: đơn chờ tất toán và đơn đã tất toán chờ xuất kho.
    /// </summary>
    /// <remarks>
    /// Trước đây Sale phải tự lọc theo trạng thái đơn để đoán xem việc của mình nằm ở đâu, mà
    /// trạng thái đơn thì không nói được "kho đã kiểm xong chưa" hay "khách trả nốt chưa". Hai
    /// hàng đợi này trả lời thẳng hai câu đó, và trả cùng một kiểu dữ liệu cho cả ký gửi lẫn
    /// mua hộ để màn hình không phải xử lý hai nhánh.
    /// </remarks>
    public partial class OrderService
    {
        /// <summary>
        /// Chỉ đợt cuối mới là khoản tất toán. Không nhận <c>FULL_PAYMENT</c> — bên mua hộ đó là
        /// tên đời đầu của ĐỢT CỌC, nhận nó vào đây là bày nhầm tiền cọc thành tiền còn phải trả.
        /// </summary>
        private const string FinalInstallment = "FINAL_PAYMENT";

        /// <summary>Hai giá trị chia đôi số phận kiện khi về tới kho VN.</summary>
        public const string DirectDeliveryHandling = "DIRECT_DELIVERY";
        public const string StoreAtVnHandling = "STORE_AT_VN";

        /// <summary>Mốc kho đã mở lô kiểm đếm xong từng kiện.</summary>
        public const string ReceivedAtDestinationStatus = "RECEIVED_AT_DESTINATION";
        public const string StoredStatus = "STORED";

        private static readonly string[] StaffRolesForQueue =
            { "SALE", "SALES", "ADMIN", "OPERATIONSMANAGER", "OPERATIONS" };

        public async Task<SettlementQueueResponse> GetSettlementQueueAsync(
            string userRole, bool settled, Guid? customerUserId = null)
        {
            var normalizedRole = userRole?.Trim().ToUpperInvariant();
            var isStaff = !string.IsNullOrWhiteSpace(normalizedRole)
                          && StaffRolesForQueue.Contains(normalizedRole);

            // Khách cũng đọc được hàng đợi này, nhưng chỉ phần đơn của chính mình: đó chính là
            // danh sách "đơn chờ tôi tất toán" bên màn khách, khỏi phải dựng thêm một API nữa.
            Guid? ownCustomerId = null;
            if (!isStaff)
            {
                if (customerUserId == null)
                {
                    throw new UnauthorizedAccessException("Vai trò hiện tại không được xem hàng đợi tất toán.");
                }

                ownCustomerId = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .FirstOrDefault(c => c.UserId == customerUserId.Value)?.Id;

                if (ownCustomerId == null)
                {
                    return new SettlementQueueResponse();
                }
            }

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => ownCustomerId == null || o.CustomerId == ownCustomerId)
                .ToList();
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToList();
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .ToDictionary(c => c.Id);

            var settledOrderIds = await _settlementQueryService.GetSettledOrderIdsAsync(orders);
            var releasedOrderIds = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync())
                .Where(d => !string.Equals(d.Status, "DELIVERY_REJECTED", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.OrderId)
                .ToHashSet();

            var inspections = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync())
                .GroupBy(i => i.ParcelId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.InspectedAt).First());

            var pendingPayments = await GetPendingFinalPaymentsAsync(orders);

            var parcelsByOrder = parcels
                .Where(p => p.ConsignmentId.HasValue)
                .GroupBy(p => p.ConsignmentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Ô kệ thuộc kho Việt Nam. Cần để phân biệt kiện STORED ở kho đích với kiện STORED
            // còn nằm kho nguồn — hai cái cùng một trạng thái nhưng chỉ cái đầu mới tất toán được.
            var destinationWarehouseIds = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .Where(w => string.Equals(w.WarehouseType, "DESTINATION", StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Id)
                .ToHashSet();

            var destinationBinIds = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.WarehouseId.HasValue && destinationWarehouseIds.Contains(b.WarehouseId.Value))
                .Select(b => b.Id)
                .ToHashSet();

            var items = new List<SettlementQueueItem>();

            foreach (var order in orders)
            {
                if (!parcelsByOrder.TryGetValue(order.Id, out var orderParcels)) continue;

                // Kho chốt kiểm rồi mới tới lượt Sale. Kiện chưa về tới nơi thì chưa có gì để chốt.
                var arrived = orderParcels
                    .Where(p => IsAtDestination(p, destinationBinIds))
                    .ToList();
                if (arrived.Count == 0) continue;

                var isSettled = settledOrderIds.Contains(order.Id);
                if (isSettled != settled) continue;

                // Đơn đã lập phiếu giao thì việc của Sale coi như xong, không nằm hàng đợi nữa.
                if (settled && releasedOrderIds.Contains(order.Id)) continue;

                var customer = order.CustomerId.HasValue && customers.TryGetValue(order.CustomerId.Value, out var c)
                    ? c
                    : null;

                var withDiscrepancy = arrived.Count(p =>
                    inspections.TryGetValue(p.Id, out var ins) && ins.HasDiscrepancy);

                items.Add(new SettlementQueueItem
                {
                    OrderId = order.Id,
                    OrderCode = order.ConsignmentCode,
                    OrderType = string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase)
                        ? "PURCHASE"
                        : "CONSIGNMENT",
                    PurchaseRequestId = order.PurchaseRequestId,
                    Status = order.Status,
                    CustomerId = customer?.Id,
                    CustomerName = customer?.FullName,
                    CustomerPhone = customer?.Phone,
                    ReceiverName = order.ReceiverName,
                    ReceiverPhone = order.ReceiverPhone,
                    ReceiverAddress = order.ReceiverAddress,
                    Route = MapRouteToDisplayName(order.Route),
                    ParcelCount = arrived.Count,
                    TotalWeight = arrived.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m),
                    DiscrepancyParcelCount = withDiscrepancy,
                    ArrivedAt = arrived.Max(p => p.CheckInTime ?? p.PutAwayTime),
                    WarehouseNotifiedAt = order.WarehouseNotifiedAt,
                    ReadyToDeliverParcelCount = arrived.Count(IsReadyForRelease),
                    PendingPaymentAmount = pendingPayments.TryGetValue(order.Id, out var due)
                        ? due.Amount
                        : null,
                    PendingCheckoutUrl = pendingPayments.TryGetValue(order.Id, out var link)
                        ? link.CheckoutUrl
                        : null,
                });
            }

            return new SettlementQueueResponse
            {
                Items = items
                    .OrderByDescending(i => i.ArrivedAt ?? DateTime.MinValue)
                    .ToList(),
            };
        }

        /// <summary>
        /// Đợt cuối Sale đã phát hành mà khách chưa trả, cho cả hai bộ bảng tiền.
        /// Có nó thì màn khách mới bày được đúng số tiền và nút trả ngay.
        /// </summary>
        private async Task<Dictionary<Guid, (decimal Amount, string? CheckoutUrl)>>
            GetPendingFinalPaymentsAsync(IReadOnlyCollection<Order> orders)
        {
            var pending = new Dictionary<Guid, (decimal, string?)>();

            var quotationToOrder = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId.HasValue)
                .GroupBy(q => q.Id)
                .ToDictionary(g => g.Key, g => g.First().OrderId!.Value);

            var invoiceToOrder = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationToOrder.ContainsKey(i.QuotationId.Value))
                .ToDictionary(i => i.Id, i => quotationToOrder[i.QuotationId!.Value]);

            foreach (var payment in (await _unitOfWork.Repository<Payment>().GetAllAsync())
                         .OrderByDescending(p => p.CreatedAt))
            {
                if (!payment.InvoiceId.HasValue) continue;
                if (!string.Equals(payment.PaymentStatus?.Trim(), "PENDING", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(payment.InstallmentType?.Trim(), FinalInstallment,
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (!invoiceToOrder.TryGetValue(payment.InvoiceId.Value, out var orderId)) continue;

                pending.TryAdd(orderId, (payment.Amount ?? 0m, payment.CheckoutUrl));
            }

            var orderByPurchaseRequest = orders
                .Where(o => o.PurchaseRequestId.HasValue)
                .GroupBy(o => o.PurchaseRequestId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Id);

            foreach (var payment in (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                         .OrderByDescending(p => p.CreatedAt))
            {
                if (!string.Equals(payment.Status?.Trim(), "PENDING", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(payment.PaymentType?.Trim(), FinalInstallment,
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (!orderByPurchaseRequest.TryGetValue(payment.PurchaseRequestId, out var orderId)) continue;

                pending.TryAdd(orderId, (payment.Amount, payment.CheckoutUrl));
            }

            return pending;
        }

        /// <summary>
        /// Kiện đã thực sự về tới kho Việt Nam.
        /// </summary>
        /// <remarks>
        /// Chốt này phải khớp với điều kiện của <c>CreateFinalPaymentAsync</c>. Nếu chỉ nhìn
        /// trạng thái, kiện STORED trên kệ kho nguồn cũng lọt vào hàng đợi, Sale bấm chốt tất
        /// toán thì bị API từ chối — danh sách bày ra việc không làm được.
        /// </remarks>
        private static bool IsAtDestination(Parcel parcel, IReadOnlySet<Guid> destinationBinIds)
        {
            var status = parcel.PackageStatus?.Trim().ToUpperInvariant();

            if (status is "ARRIVED_DESTINATION" or "RECEIVED_AT_DESTINATION") return true;

            return parcel.BinId.HasValue && destinationBinIds.Contains(parcel.BinId.Value);
        }

        /// <summary>
        /// Kiện lập được phiếu giao — cùng luật với <c>DeliveryRequestService.IsReadyToDeliver</c>,
        /// để con số Sale nhìn thấy trên hàng đợi khớp với thứ bấm được ở màn lập phiếu.
        /// </summary>
        private static bool IsReadyForRelease(Parcel parcel)
        {
            var status = parcel.PackageStatus?.Trim().ToUpperInvariant();
            if (status == "STORED") return true;

            return status == "RECEIVED_AT_DESTINATION"
                && string.Equals(parcel.DestinationHandling, "DIRECT_DELIVERY", StringComparison.OrdinalIgnoreCase);
        }
    }
}
