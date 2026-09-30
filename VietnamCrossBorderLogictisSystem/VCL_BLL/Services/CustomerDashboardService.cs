using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Gom "việc cần làm" của MỘT khách từ cả hai luồng ký gửi và mua hộ.
    ///
    /// Vì sao tính ở backend chứ không để màn hình tự cộng: mỗi con số dưới đây cần đọc
    /// từ ba đến bốn bảng (đơn, báo giá, hoá đơn, khoản thu, kiện, phiếu giao). Làm ở
    /// trình duyệt thì phải kéo cả trăm bản ghi về máy khách, mà vẫn không đủ dữ liệu để
    /// tính đúng — ví dụ "chờ khách xác nhận đã nhận" phụ thuộc trạng thái TỪNG KIỆN chứ
    /// không phải trạng thái đơn.
    /// </summary>
    public class CustomerDashboardService : ICustomerDashboardService
    {
        private readonly IUnitOfWork _unitOfWork;

        /// <summary>Số dòng ví dụ kèm theo mỗi việc — đủ để khách nhận ra đơn, không thành bảng.</summary>
        private const int PreviewSize = 5;

        public CustomerDashboardService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<CustomerDashboardResponse> GetAsync(Guid userId)
        {
            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .FirstOrDefault(c => c.UserId == userId);

            /*
             * Tài khoản chưa gắn hồ sơ khách (nhân viên bấm nhầm, hoặc khách mới tạo chưa
             * đủ hồ sơ): trả bảng RỖNG chứ không ném lỗi. Màn hình vẫn dựng được, khách
             * thấy "không có việc nào" thay vì một trang lỗi đỏ.
             */
            if (customer == null)
            {
                return new CustomerDashboardResponse
                {
                    Actions = EmptyActions(),
                    GeneratedAt = DateTime.UtcNow,
                };
            }

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => o.CustomerId == customer.Id)
                .ToList();
            var orderIds = orders.Select(o => o.Id).ToHashSet();

            var purchaseRequests = (await _unitOfWork.Repository<PurchaseRequest>().GetAllAsync())
                .Where(p => p.CustomerId == customer.Id)
                .ToList();
            var requestIds = purchaseRequests.Select(p => p.Id).ToHashSet();

            var actions = new List<CustomerDashboardActionDto>
            {
                await BuildQuotationActionAsync(orders, purchaseRequests),
                await BuildPaymentActionAsync(orderIds, orders, requestIds, purchaseRequests),
                await BuildPurchaseDecisionActionAsync(requestIds, purchaseRequests),
                await BuildDeliveryConfirmActionAsync(orders),
                await BuildRefundActionAsync(requestIds, purchaseRequests),
            };

            return new CustomerDashboardResponse
            {
                Actions = actions,
                InProgress = await BuildProgressAsync(orders, purchaseRequests),
                Stats = await BuildStatsAsync(orderIds, requestIds),
                TotalAmountDue = actions.First(a => a.Key == ActionKeys.PaymentDue).Amount,
                TotalRefundIncoming = actions.First(a => a.Key == ActionKeys.RefundIncoming).Amount,
                GeneratedAt = DateTime.UtcNow,
            };
        }

        /* ── ① Báo giá chờ khách xác nhận ─────────────────────────────────────────── */

        private async Task<CustomerDashboardActionDto> BuildQuotationActionAsync(
            List<Order> orders, List<PurchaseRequest> purchaseRequests)
        {
            var items = new List<CustomerDashboardItemDto>();

            /* Ký gửi: đơn đang ở QUOTATION_SENT. Hạn lấy từ báo giá mới nhất của đơn. */
            var sentOrders = orders
                .Where(o => Is(o.Status, "QUOTATION_SENT"))
                .ToList();

            if (sentOrders.Count > 0)
            {
                var orderIds = sentOrders.Select(o => o.Id).ToHashSet();
                var quotations = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                    .Where(q => q.OrderId.HasValue && orderIds.Contains(q.OrderId.Value))
                    .GroupBy(q => q.OrderId!.Value)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.CreatedAt).First());

                items.AddRange(sentOrders.Select(o =>
                {
                    quotations.TryGetValue(o.Id, out var quotation);
                    return ForOrder(o, o.Id, new CustomerDashboardItemDto
                    {
                        Status = o.Status ?? string.Empty,
                        StatusText = "Báo giá chờ bạn xác nhận",
                        Amount = TotalOf(quotation),
                        DueAt = quotation?.ExpiredAt,
                        UpdatedAt = quotation?.CreatedAt,
                    });
                }));
            }

            /*
             * Mua hộ: yêu cầu đã có báo giá nhưng khách chưa chấp nhận.
             * Trạng thái của BÁO GIÁ mới là thứ quyết định, không phải trạng thái yêu cầu —
             * yêu cầu vẫn nằm ở PENDING_REVIEW cho tới khi khách bấm chấp nhận.
             */
            var requestIds = purchaseRequests.Select(p => p.Id).ToHashSet();
            if (requestIds.Count > 0)
            {
                var pendingQuotes = (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                    .Where(q => requestIds.Contains(q.PurchaseRequestId) && !Is(q.Status, "ACCEPTED")
                                && !Is(q.Status, "REJECTED") && !Is(q.Status, "CANCELLED"))
                    .ToList();

                var byRequest = purchaseRequests.ToDictionary(p => p.Id);

                items.AddRange(pendingQuotes
                    .Where(q => byRequest.ContainsKey(q.PurchaseRequestId))
                    .Select(q => new CustomerDashboardItemDto
                    {
                        Kind = ItemKinds.Purchase,
                        Id = q.PurchaseRequestId,
                        Code = byRequest[q.PurchaseRequestId].PurchaseCode ?? string.Empty,
                        Status = q.Status ?? string.Empty,
                        StatusText = "Báo giá mua hộ chờ bạn xác nhận",
                        Amount = q.PrepayAmount ?? q.TotalAmount,
                        UpdatedAt = q.CreatedAt,
                    }));
            }

            return Build(ActionKeys.QuotationConfirm, items);
        }

        /* ── ② Khoản chờ khách trả ────────────────────────────────────────────────── */

        private async Task<CustomerDashboardActionDto> BuildPaymentActionAsync(
            HashSet<Guid> orderIds, List<Order> orders,
            HashSet<Guid> requestIds, List<PurchaseRequest> purchaseRequests)
        {
            var items = new List<CustomerDashboardItemDto>();

            /*
             * Ký gửi: Payment (chưa trả) → Invoice → Quotation → Order.
             * Đi ngược ba bảng vì khoản thu không gắn thẳng vào đơn.
             */
            if (orderIds.Count > 0)
            {
                var quotationsOfOrders = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                    .Where(q => q.OrderId.HasValue && orderIds.Contains(q.OrderId.Value))
                    .ToDictionary(q => q.Id, q => q.OrderId!.Value);

                var invoices = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                    .Where(i => i.QuotationId.HasValue && quotationsOfOrders.ContainsKey(i.QuotationId.Value))
                    .ToDictionary(i => i.Id, i => quotationsOfOrders[i.QuotationId!.Value]);

                var orderById = orders.ToDictionary(o => o.Id);

                var duePayments = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                    .Where(p => p.InvoiceId.HasValue && invoices.ContainsKey(p.InvoiceId.Value)
                                && IsUnpaid(p.PaymentStatus))
                    .ToList();

                /*
                 * Đơn kho PUR-xxx-n của mua hộ cũng thu tiền qua Payment → Invoice → Quotation → Order
                 * (tất toán chặng vận chuyển), nhưng với khách đó là tiền của ĐƠN MUA HỘ: gắn về yêu cầu
                 * sở hữu (kind PURCHASE) thay vì đếm thành một đơn ký gửi không tồn tại.
                 */
                items.AddRange(duePayments.Select(p =>
                {
                    var orderId = invoices[p.InvoiceId!.Value];
                    orderById.TryGetValue(orderId, out var order);
                    var isPurchase = order != null && IsPurchaseWarehouseOrder(order);
                    return ForOrder(order, orderId, new CustomerDashboardItemDto
                    {
                        Status = p.PaymentStatus ?? string.Empty,
                        StatusText = isPurchase
                            ? $"{DescribeInstalment(p.InstallmentType)} (vận chuyển đơn mua hộ)"
                            : DescribeInstalment(p.InstallmentType),
                        Amount = p.Amount ?? 0m,
                        UpdatedAt = p.CreatedAt,
                    });
                }));
            }

            /* Mua hộ: khoản thu gắn thẳng vào yêu cầu; bỏ các dòng HOÀN (tiền đi ngược). */
            if (requestIds.Count > 0)
            {
                var byRequest = purchaseRequests.ToDictionary(p => p.Id);

                var duePurchase = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                    .Where(p => requestIds.Contains(p.PurchaseRequestId)
                                && !PurchasePaymentTypes.IsRefund(p)
                                && IsUnpaid(p.Status))
                    .ToList();

                items.AddRange(duePurchase.Select(p => new CustomerDashboardItemDto
                {
                    Kind = ItemKinds.Purchase,
                    Id = p.PurchaseRequestId,
                    Code = byRequest.TryGetValue(p.PurchaseRequestId, out var r) ? r.PurchaseCode ?? string.Empty : string.Empty,
                    Status = p.Status ?? string.Empty,
                    StatusText = DescribePurchasePayment(p),
                    Amount = p.Amount,
                    UpdatedAt = p.CreatedAt,
                }));
            }

            return Build(ActionKeys.PaymentDue, items);
        }

        /* ── ③ Giá mua thực vượt giá báo — chờ khách quyết ────────────────────────── */

        private async Task<CustomerDashboardActionDto> BuildPurchaseDecisionActionAsync(
            HashSet<Guid> requestIds, List<PurchaseRequest> purchaseRequests)
        {
            var items = new List<CustomerDashboardItemDto>();

            if (requestIds.Count > 0)
            {
                var byRequest = purchaseRequests.ToDictionary(p => p.Id);

                var waiting = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                    .Where(po => requestIds.Contains(po.PurchaseRequestId)
                                 && PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.AwaitingCustomer))
                    .ToList();

                items.AddRange(waiting.Select(po => new CustomerDashboardItemDto
                {
                    Kind = ItemKinds.Purchase,
                    Id = po.PurchaseRequestId,
                    Code = byRequest.TryGetValue(po.PurchaseRequestId, out var r) ? r.PurchaseCode ?? string.Empty : string.Empty,
                    Status = po.Status ?? string.Empty,
                    StatusText = "Giá mua thực cao hơn giá đã báo",
                    Amount = po.PriceDifferenceAmount ?? 0m,
                    UpdatedAt = po.SubmittedAt ?? po.UpdatedAt,
                }));
            }

            return Build(ActionKeys.PurchasePriceDecision, items);
        }

        /* ── ④ Hàng đã tới tay — chờ khách xác nhận ───────────────────────────────── */

        private async Task<CustomerDashboardActionDto> BuildDeliveryConfirmActionAsync(List<Order> orders)
        {
            var items = new List<CustomerDashboardItemDto>();

            var openOrders = orders
                .Where(o => !o.CustomerConfirmedAt.HasValue && !Is(o.Status, "CANCELLED"))
                .ToList();

            if (openOrders.Count > 0)
            {
                var openIds = openOrders.Select(o => o.Id).ToHashSet();

                var parcelsByOrder = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                    .Where(p => p.ConsignmentId.HasValue && openIds.Contains(p.ConsignmentId.Value))
                    .GroupBy(p => p.ConsignmentId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                /*
                 * ĐÚNG LUẬT CỦA BACKEND: nút xác nhận chỉ bật khi MỌI kiện của đơn đã
                 * DELIVERED và khách chưa bấm (DeliveryRequestService.CanConfirmReceipt).
                 * Bản cũ của màn hình đi tìm đơn có status "DELIVERED" — trạng thái mà hệ
                 * thống không bao giờ ghi vào đơn — nên thẻ này luôn hiện 0.
                 */
                foreach (var order in openOrders)
                {
                    if (!parcelsByOrder.TryGetValue(order.Id, out var parcels) || parcels.Count == 0)
                        continue;

                    if (!parcels.All(p => Is(p.PackageStatus, "DELIVERED")))
                        continue;

                    items.Add(ForOrder(order, order.Id, new CustomerDashboardItemDto
                    {
                        Status = order.Status ?? string.Empty,
                        StatusText = $"Đã giao đủ {parcels.Count} kiện",
                        UpdatedAt = order.DeliveredAt ?? order.CreatedAt,
                    }));
                }
            }

            return Build(ActionKeys.DeliveryConfirm, items);
        }

        /* ── ⑤ Tiền công ty sẽ hoàn lại khách ─────────────────────────────────────── */

        private async Task<CustomerDashboardActionDto> BuildRefundActionAsync(
            HashSet<Guid> requestIds, List<PurchaseRequest> purchaseRequests)
        {
            var items = new List<CustomerDashboardItemDto>();

            if (requestIds.Count > 0)
            {
                var byRequest = purchaseRequests.ToDictionary(p => p.Id);

                var refunds = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                    .Where(p => requestIds.Contains(p.PurchaseRequestId)
                                && PurchasePaymentTypes.IsRefund(p)
                                && Is(p.Status, PurchaseRefundStatuses.Pending))
                    .ToList();

                items.AddRange(refunds.Select(p => new CustomerDashboardItemDto
                {
                    Kind = ItemKinds.Purchase,
                    Id = p.PurchaseRequestId,
                    Code = byRequest.TryGetValue(p.PurchaseRequestId, out var r) ? r.PurchaseCode ?? string.Empty : string.Empty,
                    Status = p.Status ?? string.Empty,
                    StatusText = PurchasePaymentTypes.RefundText(p.PaymentType),
                    Amount = p.Amount,
                    UpdatedAt = p.CreatedAt,
                }));
            }

            return Build(ActionKeys.RefundIncoming, items);
        }

        /* ── Số liệu để vẽ biểu đồ ───────────────────────────────────────────────── */

        /// <summary>
        /// Hai con số khách hay hỏi lại sau khi đơn đã xong: "mấy tháng nay tôi tiêu bao
        /// nhiêu" và "tiền của tôi đi đâu".
        ///
        /// Chi tiêu tính theo khoản ĐÃ TRẢ (có mốc trả tiền), không phải theo ngày tạo đơn:
        /// đơn tạo tháng này mà trả tháng sau thì tiền thuộc tháng sau. Cơ cấu chi phí lấy
        /// từ báo giá của đơn, gộp cả ký gửi lẫn mua hộ.
        /// </summary>
        private async Task<CustomerDashboardStatsDto> BuildStatsAsync(
            HashSet<Guid> orderIds, HashSet<Guid> requestIds)
        {
            var stats = new CustomerDashboardStatsDto();

            /* Sáu tháng gần nhất, kể cả tháng rỗng — bỏ tháng rỗng là biểu đồ nói dối. */
            var now = DateTime.UtcNow;
            var buckets = Enumerable.Range(0, 6)
                .Select(offset => new DateTime(now.Year, now.Month, 1).AddMonths(-5 + offset))
                .ToList();

            var spendByMonth = buckets.ToDictionary(
                month => month,
                _ => (Amount: 0m, Count: 0));

            void AddSpend(DateTime? paidAt, decimal amount)
            {
                if (!paidAt.HasValue || amount <= 0m) return;

                var key = new DateTime(paidAt.Value.Year, paidAt.Value.Month, 1);
                if (!spendByMonth.TryGetValue(key, out var current)) return;

                spendByMonth[key] = (current.Amount + amount, current.Count + 1);
            }

            /* Ký gửi: Payment → Invoice → Quotation → Order. */
            var quotations = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId.HasValue && orderIds.Contains(q.OrderId.Value))
                .ToList();
            var quotationIds = quotations.Select(q => q.Id).ToHashSet();

            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();

            foreach (var payment in (await _unitOfWork.Repository<Payment>().GetAllAsync())
                         .Where(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value)
                                     && string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase)))
            {
                AddSpend(payment.PaidAt, payment.Amount ?? 0m);
            }

            /* Mua hộ: khoản thu gắn thẳng vào yêu cầu; bỏ dòng HOÀN vì tiền đi ngược chiều. */
            foreach (var payment in (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                         .Where(p => requestIds.Contains(p.PurchaseRequestId)
                                     && !PurchasePaymentTypes.IsRefund(p)
                                     && PurchasePaymentTypes.IsPaid(p)))
            {
                AddSpend(payment.PaidAt, payment.Amount);
            }

            stats.MonthlySpend = buckets
                .Select(month => new CustomerMonthlySpendDto
                {
                    Month = month.ToString("yyyy-MM"),
                    Paid = spendByMonth[month].Amount,
                    PaymentCount = spendByMonth[month].Count,
                })
                .ToList();

            /*
             * Cơ cấu chi phí — mỗi khoản chỉ đếm MỘT lần:
             *  - mỗi đơn chỉ lấy một báo giá (đã chấp nhận, không có thì mới nhất; bỏ báo giá đã huỷ);
             *  - đơn kho mua hộ (PUR-xxx-n) mang cước + VAT cước + thuế NK theo giá mua thật, nên báo
             *    giá mua hộ của yêu cầu đó chỉ còn góp phí mua hộ + VAT phần phí. Trước đây cộng cả
             *    hai báo giá → cước và thuế của mua hộ bị đếm hai lần.
             */
            var ordersById = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id))
                .ToDictionary(o => o.Id);
            var requestsBilledByWarehouse = new HashSet<Guid>();
            var consignmentQuoted = 0;
            foreach (var group in quotations
                         .Where(q => !Is(q.Status, "CANCELLED") && !Is(q.Status, "SUPERSEDED") && !Is(q.Status, "REJECTED"))
                         .GroupBy(q => q.OrderId!.Value))
            {
                if (!ordersById.TryGetValue(group.Key, out var order)) continue;
                var isPurchaseOrder = order.PurchaseRequestId.HasValue;
                if (isPurchaseOrder && Is(order.Status, "CANCELLED")) continue;

                var quotation = group.FirstOrDefault(q => Is(q.Status, "ACCEPTED"))
                    ?? group.OrderByDescending(q => q.CreatedAt).First();
                stats.TotalFreight += quotation.EstimatedFreightCharge ?? 0m;
                stats.TotalServiceFee += quotation.ServiceFee ?? 0m;
                stats.TotalTax += TaxOf(quotation);

                if (isPurchaseOrder) requestsBilledByWarehouse.Add(order.PurchaseRequestId!.Value);
                else consignmentQuoted++;
            }
            stats.QuotedOrderCount = consignmentQuoted;

            foreach (var group in (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                         .Where(q => requestIds.Contains(q.PurchaseRequestId) && !Is(q.Status, "REJECTED") && !Is(q.Status, "CANCELLED"))
                         .GroupBy(q => q.PurchaseRequestId))
            {
                var quotation = group.FirstOrDefault(q => Is(q.Status, "ACCEPTED"))
                    ?? group.OrderByDescending(q => q.CreatedAt).First();
                stats.TotalServiceFee += quotation.PurchaseFee;

                if (requestsBilledByWarehouse.Contains(group.Key))
                {
                    // Chỉ VAT phần phí dịch vụ; VAT cước + thuế NK đã nằm ở báo giá đơn kho.
                    var vatOnFreight = Math.Round(quotation.ShippingFee * (quotation.VatRate ?? 0m) / 100m, 0);
                    stats.TotalTax += Math.Max(0m, (quotation.Vat ?? 0m) - vatOnFreight);
                }
                else
                {
                    stats.TotalFreight += quotation.ShippingFee;
                    stats.TotalTax += (quotation.Vat ?? 0m) + (quotation.ImportTax ?? 0m);
                }
                stats.QuotedOrderCount++;
            }

            return stats;
        }

        /* ── Hàng đang chạy ───────────────────────────────────────────────────────── */

        private enum Stage
        {
            None,
            WaitingStaff,
            WaitingGoods,
            AtOriginWarehouse,
            InTransit,
            AtVietnamWarehouse,
            OutForDelivery,
        }

        /// <summary>Chặng của một đơn (ký gửi hoặc đơn kho mua hộ) theo trạng thái đơn.</summary>
        private static Stage StageOfOrder(string? status) =>
            (status ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "PENDING_REVIEW" or "NEED_MORE_INFO" or "QUOTATION_SENT" or "QUOTATION_CONFIRMED" or "APPROVED"
                    => Stage.WaitingStaff,
                "WAITING_DEPOSIT" or "DEPOSIT_PAID" or "PAID" => Stage.WaitingGoods,
                "WAREHOUSE_RECEIVED" or "CHECKED_IN" => Stage.AtOriginWarehouse,
                "IN_TRANSIT" or "SHIPPED" => Stage.InTransit,
                "AT_DESTINATION_WAREHOUSE" => Stage.AtVietnamWarehouse,
                "OUT_FOR_DELIVERY" or "DELIVERING" => Stage.OutForDelivery,
                /* COMPLETED / CANCELLED / REJECTED: đã xong, không còn "đang chạy". */
                _ => Stage.None,
            };

        private static void AddToStage(CustomerDashboardProgressDto progress, Stage stage)
        {
            switch (stage)
            {
                case Stage.WaitingStaff: progress.WaitingStaff++; break;
                case Stage.WaitingGoods: progress.WaitingGoods++; break;
                case Stage.AtOriginWarehouse: progress.AtOriginWarehouse++; break;
                case Stage.InTransit: progress.InTransit++; break;
                case Stage.AtVietnamWarehouse: progress.AtVietnamWarehouse++; break;
                case Stage.OutForDelivery: progress.OutForDelivery++; break;
            }
        }

        /// <summary>
        /// Mỗi phần hàng chỉ đếm MỘT lần, ở đúng chặng.
        /// <para>
        /// Đặt NCC xong, hàng của đơn mua NCC đó thành đơn kho PUR-xxx-n và đi tiếp theo đơn kho;
        /// còn yêu cầu mua hộ thì vẫn nằm ở PURCHASING tới khi xong hẳn. Bản cũ đếm CẢ yêu cầu (vào
        /// "chờ hàng") LẪN đơn kho của nó → một món hàng thành hai dòng, và yêu cầu ở "chờ hàng" mãi
        /// kể cả khi đơn kho đã về tới VN.
        /// </para>
        /// Nay: yêu cầu đã có đơn kho thì chặng lấy theo đơn kho. Yêu cầu chỉ còn được đếm riêng khi
        /// vẫn còn đơn mua NCC chưa đặt (chưa có đơn kho) VÀ không đơn kho nào của nó đang ở chặng
        /// tương đương ("chờ hàng") — tức phần hàng còn đang đi mua.
        /// </summary>
        private async Task<CustomerDashboardProgressDto> BuildProgressAsync(
            List<Order> orders, List<PurchaseRequest> purchaseRequests)
        {
            var progress = new CustomerDashboardProgressDto();

            var requestsWithWarehouseOrder = new HashSet<Guid>();
            var requestsWaitingGoodsByOrder = new HashSet<Guid>();

            foreach (var order in orders)
            {
                var stage = StageOfOrder(order.Status);
                AddToStage(progress, stage);

                if (order.PurchaseRequestId is Guid owner && !Is(order.Status, "CANCELLED"))
                {
                    requestsWithWarehouseOrder.Add(owner);
                    if (stage == Stage.WaitingGoods) requestsWaitingGoodsByOrder.Add(owner);
                }
            }

            var buyingRequests = purchaseRequests
                .Where(p => Is(p.Status, "PAID") || Is(p.Status, "PURCHASING") || Is(p.Status, "PURCHASED")
                            || Is(p.Status, "SELLER_SHIPPED"))
                .ToList();

            if (buyingRequests.Count > 0)
            {
                /* Đơn mua NCC còn sống mà chưa thành đơn kho = phần hàng vẫn đang đi mua. */
                var placedPurchaseOrderIds = orders
                    .Where(o => o.PurchaseOrderId.HasValue)
                    .Select(o => o.PurchaseOrderId!.Value)
                    .ToHashSet();
                var buyingIds = buyingRequests.Select(p => p.Id).ToHashSet();
                var requestsStillBuying = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                    .Where(po => buyingIds.Contains(po.PurchaseRequestId)
                                 && PurchaseOrderStatuses.IsActive(po)
                                 && !placedPurchaseOrderIds.Contains(po.Id))
                    .Select(po => po.PurchaseRequestId)
                    .ToHashSet();

                progress.WaitingGoods += buyingRequests.Count(p =>
                    !requestsWithWarehouseOrder.Contains(p.Id)
                    || (requestsStillBuying.Contains(p.Id) && !requestsWaitingGoodsByOrder.Contains(p.Id)));
            }

            progress.WaitingStaff += purchaseRequests.Count(p =>
                Is(p.Status, "PENDING_REVIEW") || Is(p.Status, "IN_REVIEW") || Is(p.Status, "NEED_MORE_INFO"));

            progress.Total = progress.WaitingStaff + progress.WaitingGoods + progress.AtOriginWarehouse
                + progress.InTransit + progress.AtVietnamWarehouse + progress.OutForDelivery;

            return progress;
        }

        /* ── Dùng chung ───────────────────────────────────────────────────────────── */

        private static class ActionKeys
        {
            public const string QuotationConfirm = "QUOTATION_CONFIRM";
            public const string PaymentDue = "PAYMENT_DUE";
            public const string PurchasePriceDecision = "PURCHASE_PRICE_DECISION";
            public const string DeliveryConfirm = "DELIVERY_CONFIRM";
            public const string RefundIncoming = "REFUND_INCOMING";
        }

        private static class ItemKinds
        {
            public const string Consignment = "CONSIGNMENT";
            public const string Purchase = "PURCHASE";
        }

        private static List<CustomerDashboardActionDto> EmptyActions() => new()
        {
            new CustomerDashboardActionDto { Key = ActionKeys.QuotationConfirm },
            new CustomerDashboardActionDto { Key = ActionKeys.PaymentDue },
            new CustomerDashboardActionDto { Key = ActionKeys.PurchasePriceDecision },
            new CustomerDashboardActionDto { Key = ActionKeys.DeliveryConfirm },
            new CustomerDashboardActionDto { Key = ActionKeys.RefundIncoming },
        };

        /// <summary>Gom danh sách dòng thành một thẻ việc: đếm, cộng tiền, lấy hạn sớm nhất.</summary>
        private static CustomerDashboardActionDto Build(string key, List<CustomerDashboardItemDto> items)
        {
            /* Việc gấp lên trước: có hạn thì theo hạn, không hạn thì theo mới nhất. */
            var ordered = items
                .OrderBy(i => i.DueAt ?? DateTime.MaxValue)
                .ThenByDescending(i => i.UpdatedAt ?? DateTime.MinValue)
                .ToList();

            return new CustomerDashboardActionDto
            {
                Key = key,
                Count = ordered.Count,
                Amount = ordered.Sum(i => i.Amount),
                ConsignmentCount = ordered.Count(i => i.Kind == ItemKinds.Consignment),
                PurchaseCount = ordered.Count(i => i.Kind == ItemKinds.Purchase),
                EarliestDueAt = ordered.Where(i => i.DueAt.HasValue).Select(i => i.DueAt!.Value).DefaultIfEmpty().Min() is var due
                    && due != default ? due : null,
                Items = ordered.Take(PreviewSize).ToList(),
            };
        }

        private static bool Is(string? actual, string expected) =>
            string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        /// <summary>Đơn kho sinh từ yêu cầu mua hộ (PUR-xxx-n), không phải đơn ký gửi của khách.</summary>
        private static bool IsPurchaseWarehouseOrder(Order order) =>
            order.PurchaseRequestId.HasValue || Is(order.OrderType, "PURCHASE");

        /// <summary>
        /// Điền luồng / id / mã cho một dòng đến từ bảng đơn. Đơn kho PUR thuộc phía MUA HỘ: kind
        /// PURCHASE, <c>Id</c> = yêu cầu mua hộ sở hữu (màn hình mở đúng trang mua hộ), orderId của đơn
        /// kho để ở <c>WarehouseOrderId</c>. Đơn ký gửi giữ nguyên như cũ.
        /// </summary>
        private static CustomerDashboardItemDto ForOrder(Order? order, Guid orderId, CustomerDashboardItemDto item)
        {
            var isPurchase = order != null && IsPurchaseWarehouseOrder(order);
            item.Kind = isPurchase ? ItemKinds.Purchase : ItemKinds.Consignment;
            item.Id = isPurchase ? order!.PurchaseRequestId ?? orderId : orderId;
            item.WarehouseOrderId = isPurchase ? orderId : null;
            item.Code = order?.ConsignmentCode ?? string.Empty;
            return item;
        }

        /// <summary>Khoản chưa vào tài khoản. Không dùng danh sách "đã trả" vì mã trạng thái đời cũ nhiều biến thể.</summary>
        private static bool IsUnpaid(string? status)
        {
            var value = (status ?? string.Empty).Trim().ToUpperInvariant();

            return value is "PENDING" or "PENDING_RECONCILIATION" or "PROCESSING" or "AWAITING_PAYMENT";
        }

        /// <summary>
        /// Tổng báo giá ký gửi = cước + phí dịch vụ + thuế. TaxAndDuty ĐÃ là VAT + thuế NK, nên chỉ
        /// lấy một trong hai cách ghi — trước đây cộng cả ba cột nên thuế bị tính gấp đôi.
        /// </summary>
        private static decimal TotalOf(Quotation? quotation)
        {
            if (quotation == null) return 0m;

            return (quotation.EstimatedFreightCharge ?? 0m)
                + (quotation.ServiceFee ?? 0m)
                + TaxOf(quotation);
        }

        private static decimal TaxOf(Quotation quotation) =>
            quotation.TaxAndDuty is > 0m
                ? quotation.TaxAndDuty.Value
                : (quotation.Vat ?? 0m) + (quotation.ImportTax ?? 0m);

        private static string DescribeInstalment(string? installmentType) =>
            (installmentType ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "DEPOSIT" => "Tiền cọc",
                "FINAL" or "FINAL_PAYMENT" => "Đợt thanh toán cuối",
                "FULL" or "FULL_PAYMENT" => "Thanh toán một lần",
                _ => "Khoản chờ thanh toán",
            };

        private static string DescribePurchasePayment(PurchasePayment payment) =>
            PurchasePaymentTypes.Of(payment) switch
            {
                PurchasePaymentTypes.Prepayment => "Phần trả trước đơn mua hộ",
                PurchasePaymentTypes.PriceDifference => "Phần chênh giá mua",
                PurchasePaymentTypes.LegacyDeposit => "Tiền cọc đơn mua hộ",
                PurchasePaymentTypes.LegacyFinal => "Đợt cuối đơn mua hộ",
                _ => "Khoản chờ thanh toán",
            };
    }
}
