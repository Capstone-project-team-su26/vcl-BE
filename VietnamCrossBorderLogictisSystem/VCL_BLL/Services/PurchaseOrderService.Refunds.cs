using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Tiền đi NGƯỢC của luồng mua hộ — quy tắc đã chốt:
    /// <list type="number">
    /// <item>Không mua được / mua ít hơn khách đặt / NCC giao thiếu: hoàn ĐỦ tiền hàng + phí mua hộ + VAT
    ///       phần phí tương ứng (phí chia theo tỷ lệ tiền hàng đã báo).</item>
    /// <item>Khách huỷ sau khi đã đặt NCC: hoàn tiền hàng KHÁCH THỰC TRẢ (gồm phần chênh giá đã trả, trừ
    ///       phần chênh đã được hoàn) − phí huỷ; giữ phí mua hộ + VAT phí.</item>
    /// <item>Thuế NK: phần hàng NCC giao thiếu không thu (dòng TAX_ADJUSTMENT âm khi tất toán; tất toán
    ///       xong rồi thì trả luôn trong khoản hoàn).</item>
    /// </list>
    /// Mỗi khoản hoàn ghi chi tiết theo sản phẩm ở PURCHASE_REFUND_LINES; tổng hoàn không vượt tổng đã thu.
    /// </summary>
    public partial class PurchaseOrderService
    {
        private static readonly HashSet<string> RefundCloseRoles = new(StringComparer.OrdinalIgnoreCase)
        { "ADMIN", "ADMINISTRATOR", "SALE", "SALES" };

        // ── Đọc sổ hoàn tiền ─────────────────────────────────────────────────────────────

        private static bool IsStatus(string? actual, string expected) =>
            string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        private static bool IsReason(PurchaseRefundLine line, string code) =>
            string.Equals(line.ReasonCode?.Trim(), code, StringComparison.OrdinalIgnoreCase);

        private static string Money(decimal value) => $"{value:#,##0}đ";

        /// <summary>Dòng chi tiết của mọi khoản hoàn CÒN HIỆU LỰC (chờ hoàn / đã hoàn) của yêu cầu.</summary>
        private async Task<List<PurchaseRefundLine>> LiveRefundLinesAsync(Guid requestId)
        {
            var liveIds = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseRequestId == requestId && PurchasePaymentTypes.IsRefund(p)
                            && !IsStatus(p.Status, PurchaseRefundStatuses.Cancelled))
                .Select(p => p.Id)
                .ToHashSet();
            return (await _unitOfWork.Repository<PurchaseRefundLine>().GetAllAsync())
                .Where(l => liveIds.Contains(l.PurchasePaymentId))
                .ToList();
        }

        /// <summary>Số lượng đã đóng "không mua được" (đã hoàn tiền) theo sản phẩm của yêu cầu.</summary>
        private async Task<Dictionary<Guid, int>> ClosedUnfulfilledQtyAsync(Guid requestId) =>
            (await LiveRefundLinesAsync(requestId))
                .Where(l => l.PurchaseRequestItemId.HasValue && IsReason(l, PurchaseRefundReasons.Unfulfilled))
                .GroupBy(l => l.PurchaseRequestItemId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        /// <summary>
        /// Chênh giá của đơn mua đã tất toán chưa: khách đã trả phần chênh (giá thực cao hơn giá báo),
        /// hoặc phần thừa đã / đang được hoàn (giá thực thấp hơn). Đã tất toán thì mỗi sản phẩm khách
        /// trả đúng giá mua thật; chưa thì khách trả theo giá báo.
        /// </summary>
        private static bool IsPriceDiffSettled(Guid purchaseOrderId, IEnumerable<PurchasePayment> payments) =>
            payments.Any(p => p.PurchaseOrderId == purchaseOrderId && (
                (PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.PriceDifference && PurchasePaymentTypes.IsPaid(p))
                || (PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.RefundPriceDifference
                    && (IsStatus(p.Status, PurchaseRefundStatuses.Pending) || IsStatus(p.Status, PurchaseRefundStatuses.Refunded)))));

        private static decimal QuotedUnitOf(PurchaseOrderItem item) => item.QuotedUnitPrice ?? item.UnitPrice;

        /// <summary>Phí mua hộ ứng với một phần tiền hàng (chia theo tỷ lệ tiền hàng đã báo).</summary>
        private static decimal FeeShare(PurchaseQuotation quotation, decimal quotedGoods) =>
            quotation.ProductsSubtotal > 0m ? Math.Round(quotation.PurchaseFee * quotedGoods / quotation.ProductsSubtotal, 0) : 0m;

        private static decimal VatOnFee(PurchaseQuotation quotation, decimal fee) =>
            Math.Round(fee * (quotation.VatRate ?? 0m) / 100m, 0);

        private string FeeFormula(PurchaseQuotation quotation, decimal goods, decimal fee, decimal vat) =>
            $"phí mua hộ {Money(fee)} ({Money(quotation.PurchaseFee)} × {Money(goods)} / {Money(quotation.ProductsSubtotal)}) "
            + $"+ VAT {quotation.VatRate ?? 0m:G29}% phí {Money(vat)}";

        // ── Chênh giá / huỷ đơn ─────────────────────────────────────────────────────────

        /// <summary>Giá mua thực thấp hơn giá báo: hoàn phần thừa, một dòng cho mỗi sản phẩm có chênh.</summary>
        private async Task<List<PurchaseRefundLine>> BuildPriceDiffRefundLinesAsync(PurchaseOrder po)
        {
            var items = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => i.PurchaseOrderId == po.Id)
                .ToList();
            var lines = new List<PurchaseRefundLine>();
            foreach (var item in items)
            {
                var quotedUnit = QuotedUnitOf(item);
                var diff = quotedUnit * item.Quantity - item.LineTotal;
                if (diff == 0m) continue;
                lines.Add(new PurchaseRefundLine
                {
                    PurchaseOrderId = po.Id,
                    PurchaseRequestItemId = item.PurchaseRequestItemId,
                    ProductName = item.ProductName,
                    ReasonCode = PurchaseRefundReasons.PriceDiff,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    PriceDifferenceAmount = diff,
                    Amount = diff,
                    Formula = $"({Money(quotedUnit)} giá báo − {Money(item.UnitPrice)} giá mua) × {item.Quantity} = {Money(diff)}"
                });
            }
            return lines;
        }

        private async Task CancelPendingRefundsAsync(Guid purchaseOrderId, string refundType)
        {
            var repo = _unitOfWork.Repository<PurchasePayment>();
            foreach (var p in (await repo.GetAllAsync())
                         .Where(p => p.PurchaseOrderId == purchaseOrderId
                                     && string.Equals(p.PaymentType, refundType, StringComparison.OrdinalIgnoreCase)
                                     && IsStatus(p.Status, PurchaseRefundStatuses.Pending)))
            {
                p.Status = PurchaseRefundStatuses.Cancelled;
                repo.Update(p);
            }
        }

        /// <summary>
        /// Huỷ đơn ĐÃ đặt NCC. Gọi SAU khi các khoản đang chờ của đơn đã bị huỷ, nên "chênh giá đã
        /// tất toán" chỉ còn tính khoản chênh khách đã trả / phần thừa đã thực hoàn.
        /// </summary>
        private async Task<List<PurchaseRefundLine>> BuildCancellationRefundLinesAsync(
            PurchaseOrder po, bool supplierFault, string reason, Guid userId)
        {
            var quotation = po.PurchaseQuotationId.HasValue
                ? await _unitOfWork.Repository<PurchaseQuotation>().GetByIdAsync(po.PurchaseQuotationId.Value)
                : null;
            var payments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseRequestId == po.PurchaseRequestId)
                .ToList();
            var settled = IsPriceDiffSettled(po.Id, payments);
            var shortQty = (await LiveRefundLinesAsync(po.PurchaseRequestId))
                .Where(l => l.PurchaseOrderId == po.Id && IsReason(l, PurchaseRefundReasons.SupplierShort) && l.PurchaseRequestItemId.HasValue)
                .GroupBy(l => l.PurchaseRequestItemId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            var items = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => i.PurchaseOrderId == po.Id)
                .OrderBy(i => i.ProductName)
                .ToList();
            var rate = supplierFault ? 0m : await GetCancelFeeRateAsync();

            var rows = new List<(PurchaseOrderItem Item, int Qty, decimal Goods, decimal Diff, decimal Fee, decimal Vat)>();
            foreach (var item in items)
            {
                var qty = item.Quantity - shortQty.GetValueOrDefault(item.PurchaseRequestItemId);
                if (qty <= 0) continue;
                var quotedUnit = QuotedUnitOf(item);
                var goods = quotedUnit * qty;
                var diff = settled ? (item.UnitPrice - quotedUnit) * qty : 0m;
                var fee = supplierFault && quotation != null ? FeeShare(quotation, goods) : 0m;
                var vat = supplierFault && quotation != null ? VatOnFee(quotation, fee) : 0m;
                rows.Add((item, qty, goods, diff, fee, vat));
            }

            // Phí huỷ tính trên TỔNG tiền hàng khách thực trả rồi chia về từng dòng (dòng cuối nhận phần lẻ).
            var paidTotal = rows.Sum(r => r.Goods + r.Diff);
            var cancelFeeTotal = Math.Round(paidTotal * rate / 100m, 0);
            var remaining = cancelFeeTotal;
            var lines = new List<PurchaseRefundLine>();
            for (var k = 0; k < rows.Count; k++)
            {
                var r = rows[k];
                var paid = r.Goods + r.Diff;
                var share = k == rows.Count - 1 || paidTotal == 0m
                    ? remaining
                    : Math.Round(cancelFeeTotal * paid / paidTotal, 0);
                remaining -= share;
                var unitPaid = r.Qty == 0 ? 0m : paid / r.Qty;
                var amount = paid + r.Fee + r.Vat - share;

                var formula = $"tiền hàng khách trả {Money(paid)} ({r.Qty} × {Money(unitPaid)}"
                    + (r.Diff != 0m ? $", gồm chênh giá {Money(r.Diff)}" : string.Empty) + ")";
                formula += supplierFault
                    ? $" + {FeeFormula(quotation!, r.Goods, r.Fee, r.Vat)} = {Money(amount)}"
                    : $" − phí huỷ {rate:G29}% {Money(share)} = {Money(amount)} (giữ phí mua hộ + VAT phí)";

                lines.Add(new PurchaseRefundLine
                {
                    PurchaseOrderId = po.Id,
                    PurchaseRequestItemId = r.Item.PurchaseRequestItemId,
                    ProductName = r.Item.ProductName,
                    ReasonCode = supplierFault ? PurchaseRefundReasons.CancelSupplier : PurchaseRefundReasons.CancelCustomer,
                    Quantity = r.Qty,
                    UnitPrice = unitPaid,
                    GoodsAmount = r.Goods,
                    PriceDifferenceAmount = r.Diff,
                    ServiceFeeAmount = r.Fee,
                    VatAmount = r.Vat,
                    CancelFeeAmount = share,
                    Amount = amount,
                    Formula = formula,
                    Note = reason,
                    CreatedBy = userId
                });
            }
            return lines;
        }

        /// <summary>Huỷ đơn CHƯA đặt NCC mà khách đã trả phần chênh giá: trả lại đủ phần chênh đó.</summary>
        private async Task<List<PurchaseRefundLine>> BuildPriceDiffReturnLinesAsync(PurchaseOrder po, string reason, Guid userId)
        {
            var diffPaid = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseOrderId == po.Id && PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.PriceDifference
                            && PurchasePaymentTypes.IsPaid(p))
                .Sum(p => p.Amount);
            if (diffPaid <= 0m) return new List<PurchaseRefundLine>();

            var returned = (await LiveRefundLinesAsync(po.PurchaseRequestId))
                .Where(l => l.PurchaseOrderId == po.Id && IsReason(l, PurchaseRefundReasons.PriceDiffReturn))
                .Sum(l => l.Amount);
            var amount = diffPaid - returned;
            if (amount <= 0m) return new List<PurchaseRefundLine>();

            return new List<PurchaseRefundLine>
            {
                new()
                {
                    PurchaseOrderId = po.Id,
                    ProductName = po.PurchaseOrderCode,
                    ReasonCode = PurchaseRefundReasons.PriceDiffReturn,
                    PriceDifferenceAmount = amount,
                    Amount = amount,
                    Formula = $"Phần chênh giá khách đã trả {Money(diffPaid)} cho đơn chưa đặt NCC — trả lại {Money(amount)}",
                    Note = reason,
                    CreatedBy = userId
                }
            };
        }

        /// <summary>Huỷ báo giá + hoá đơn chặng VN của đơn kho mua hộ và các khoản thu đang chờ trên đó.</summary>
        private async Task VoidWarehouseBillingAsync(Order order)
        {
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotations = (await quotationRepo.GetAllAsync()).Where(q => q.OrderId == order.Id).ToList();
            foreach (var q in quotations)
            {
                q.Status = "CANCELLED";
                quotationRepo.Update(q);
            }

            var quotationIds = quotations.Select(q => q.Id).ToHashSet();
            var invoiceRepo = _unitOfWork.Repository<Invoice>();
            var invoices = (await invoiceRepo.GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .ToList();
            foreach (var invoice in invoices)
            {
                invoice.InvoiceStatus = "CANCELLED";
                invoiceRepo.Update(invoice);
            }

            var invoiceIds = invoices.Select(i => i.Id).ToHashSet();
            var paymentRepo = _unitOfWork.Repository<Payment>();
            foreach (var payment in (await paymentRepo.GetAllAsync())
                         .Where(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value)
                                     && IsStatus(p.PaymentStatus, "PENDING")))
            {
                payment.PaymentStatus = "CANCELLED";
                paymentRepo.Update(payment);
            }
        }

        private async Task<(Invoice? Invoice, List<Payment> Payments)> FindWarehouseInvoiceAsync(Order order)
        {
            var quotationIds = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == order.Id)
                .Select(q => q.Id)
                .ToHashSet();
            var invoice = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .OrderByDescending(i => i.IssuedAt)
                .FirstOrDefault();
            var payments = invoice == null
                ? new List<Payment>()
                : (await _unitOfWork.Repository<Payment>().GetAllAsync()).Where(p => p.InvoiceId == invoice.Id).ToList();
            return (invoice, payments);
        }

        private async Task<string?> FindWarehouseInvoiceStatusAsync(Guid purchaseOrderId)
        {
            var order = await FindWarehouseOrderAsync(purchaseOrderId);
            if (order == null) return null;
            return (await FindWarehouseInvoiceAsync(order)).Invoice?.InvoiceStatus;
        }

        // ── Đóng phần không mua được / NCC giao thiếu ────────────────────────────────────

        public async Task<CloseUnfulfilledResponseDto> CloseUnfulfilledAsync(
            Guid requestId, Guid userId, string userRole, CloseUnfulfilledDto dto)
        {
            if (!RefundCloseRoles.Contains(userRole?.Trim() ?? string.Empty))
                throw new UnauthorizedAccessException("Chỉ Sale / Admin được đóng phần không mua được và lập khoản hoàn.");
            dto ??= new CloseUnfulfilledDto();
            var reason = dto.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Phải ghi lý do (ví dụ: NCC hết hàng, chỉ mua được 3/5).");

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            var quotation = await GetAcceptedQuotationAsync(requestId)
                ?? throw new ArgumentException("Yêu cầu chưa có báo giá được khách chấp nhận.");
            if (!quotation.PrepayAmount.HasValue)
                throw new ArgumentException("Báo giá lập theo cách tính cũ (thu cọc theo tỷ lệ) — phần không mua được xử lý tay với kế toán.");

            var payments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseRequestId == requestId)
                .ToList();
            if (!payments.Any(p => PurchasePaymentTypes.IsFirstInstalment(p) && PurchasePaymentTypes.IsPaid(p)))
                throw new ArgumentException("Khách chưa thanh toán phần trả trước — chưa có khoản nào để hoàn.");

            var requestItems = (await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync())
                .Where(i => i.PurchaseRequestId == requestId)
                .ToDictionary(i => i.Id);
            var quoted = (await _unitOfWork.Repository<PurchaseQuotationItem>().GetAllAsync())
                .Where(qi => qi.PurchaseQuotationId == quotation.Id)
                .GroupBy(qi => qi.PurchaseRequestItemId)
                .ToDictionary(g => g.Key, g => (Unit: g.First().UnitPrice, Qty: g.Sum(x => x.Quantity)));

            var orders = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(o => o.PurchaseRequestId == requestId)
                .ToList();
            var orderIds = orders.Select(o => o.Id).ToHashSet();
            var activeIds = orders.Where(PurchaseOrderStatuses.IsActive).Select(o => o.Id).ToHashSet();
            var poItems = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => orderIds.Contains(i.PurchaseOrderId))
                .ToList();
            var activeQty = poItems.Where(i => activeIds.Contains(i.PurchaseOrderId))
                .GroupBy(i => i.PurchaseRequestItemId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

            var lines = await LiveRefundLinesAsync(requestId);
            int SumQty(Guid itemId, params string[] reasons) =>
                lines.Where(l => l.PurchaseRequestItemId == itemId && reasons.Any(r => IsReason(l, r))).Sum(l => l.Quantity);

            // Khoản hoàn huỷ đơn đời cũ (chưa có chi tiết): lấy số lượng theo dòng của đơn đã huỷ.
            var liveRefunds = payments.Where(p => PurchasePaymentTypes.IsRefund(p) && !IsStatus(p.Status, PurchaseRefundStatuses.Cancelled)).ToList();
            var refundsWithLines = lines.Select(l => l.PurchasePaymentId).ToHashSet();
            var legacyCancelQty = liveRefunds
                .Where(p => PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.RefundCancellation && !refundsWithLines.Contains(p.Id) && p.PurchaseOrderId.HasValue)
                .SelectMany(p => poItems.Where(i => i.PurchaseOrderId == p.PurchaseOrderId!.Value))
                .GroupBy(i => i.PurchaseRequestItemId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

            // Phần thừa đã HOÀN cho đơn bị huỷ khi chưa đặt NCC: hàng đó quay về "chưa mua", nên lúc hoàn
            // tiền hàng theo giá báo phải trừ đi phần đã trả trước đó (không hoàn hai lần).
            var neverPlacedCancelled = orders
                .Where(o => PurchaseOrderStatuses.Is(o.Status, PurchaseOrderStatuses.Cancelled) && !o.OrderedAt.HasValue)
                .Select(o => o.Id)
                .ToHashSet();
            var strayRefunds = liveRefunds
                .Where(p => PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.RefundPriceDifference
                            && IsStatus(p.Status, PurchaseRefundStatuses.Refunded)
                            && p.PurchaseOrderId.HasValue && neverPlacedCancelled.Contains(p.PurchaseOrderId.Value))
                .ToList();
            var strayByItem = new Dictionary<Guid, decimal>();
            foreach (var stray in strayRefunds)
            {
                var own = lines.Where(l => l.PurchasePaymentId == stray.Id && l.PurchaseRequestItemId.HasValue).ToList();
                if (own.Count > 0)
                {
                    foreach (var l in own)
                        strayByItem[l.PurchaseRequestItemId!.Value] = strayByItem.GetValueOrDefault(l.PurchaseRequestItemId!.Value) + l.PriceDifferenceAmount;
                }
                else
                {
                    foreach (var i in poItems.Where(i => i.PurchaseOrderId == stray.PurchaseOrderId!.Value))
                        strayByItem[i.PurchaseRequestItemId] = strayByItem.GetValueOrDefault(i.PurchaseRequestItemId)
                            + (QuotedUnitOf(i) * i.Quantity - i.LineTotal);
                }
            }

            var filter = (dto.PurchaseRequestItemIds ?? new List<Guid>()).ToHashSet();
            foreach (var id in filter)
                if (!requestItems.ContainsKey(id))
                    throw new ArgumentException($"Sản phẩm {id} không thuộc yêu cầu mua hộ này.");

            var newLines = new List<PurchaseRefundLine>();

            // ① Phần chưa vào đơn mua nào (NCC hết hàng / mua ít hơn khách đặt).
            foreach (var item in requestItems.Values.OrderBy(i => i.ProductName))
            {
                if (filter.Count > 0 && !filter.Contains(item.Id)) continue;
                if (!quoted.TryGetValue(item.Id, out var q)) continue;

                var open = q.Qty - activeQty.GetValueOrDefault(item.Id)
                    - SumQty(item.Id, PurchaseRefundReasons.Unfulfilled, PurchaseRefundReasons.CancelCustomer, PurchaseRefundReasons.CancelSupplier)
                    - legacyCancelQty.GetValueOrDefault(item.Id);
                if (open <= 0) continue;

                var goods = q.Unit * open;
                var alreadyDeducted = -lines.Where(l => l.PurchaseRequestItemId == item.Id && IsReason(l, PurchaseRefundReasons.Unfulfilled))
                    .Sum(l => l.PriceDifferenceAmount);
                var stray = Math.Min(Math.Max(strayByItem.GetValueOrDefault(item.Id) - alreadyDeducted, 0m), goods);
                var fee = FeeShare(quotation, goods);
                var vat = VatOnFee(quotation, fee);
                var amount = goods - stray + fee + vat;

                newLines.Add(new PurchaseRefundLine
                {
                    PurchaseRequestItemId = item.Id,
                    ProductName = item.ProductName,
                    ReasonCode = PurchaseRefundReasons.Unfulfilled,
                    Quantity = open,
                    UnitPrice = q.Unit,
                    GoodsAmount = goods,
                    PriceDifferenceAmount = -stray,
                    ServiceFeeAmount = fee,
                    VatAmount = vat,
                    Amount = amount,
                    Formula = $"tiền hàng {open} × {Money(q.Unit)} = {Money(goods)} + {FeeFormula(quotation, goods, fee, vat)}"
                        + (stray > 0m ? $" − chênh giá đã hoàn trước đó {Money(stray)}" : string.Empty)
                        + $" = {Money(amount)}",
                    Note = reason,
                    CreatedBy = userId
                });
            }

            // ② NCC giao thiếu trong đơn đã đặt.
            foreach (var shortage in dto.SupplierShortages ?? new List<SupplierShortageInputDto>())
            {
                var po = orders.FirstOrDefault(o => o.Id == shortage.PurchaseOrderId)
                    ?? throw new ArgumentException($"Đơn mua {shortage.PurchaseOrderId} không thuộc yêu cầu này.");
                if (!PurchaseOrderStatuses.Placed.Contains(po.Status ?? string.Empty))
                    throw new ArgumentException(
                        $"Đơn mua {po.PurchaseOrderCode} chưa đặt NCC hoặc đã huỷ ({PurchaseOrderStatuses.Text(po.Status)}) — phần chưa mua thì đóng theo 'không mua được'.");
                var poItem = poItems.FirstOrDefault(i => i.PurchaseOrderId == po.Id && i.PurchaseRequestItemId == shortage.PurchaseRequestItemId)
                    ?? throw new ArgumentException($"Sản phẩm {shortage.PurchaseRequestItemId} không có trong đơn mua {po.PurchaseOrderCode}.");
                var already = lines.Concat(newLines)
                    .Where(l => l.PurchaseOrderId == po.Id && l.PurchaseRequestItemId == poItem.PurchaseRequestItemId
                                && IsReason(l, PurchaseRefundReasons.SupplierShort))
                    .Sum(l => l.Quantity);
                var maxShort = poItem.Quantity - already;
                if (shortage.Quantity <= 0 || shortage.Quantity > maxShort)
                    throw new ArgumentException($"Số lượng NCC giao thiếu của '{poItem.ProductName}' phải từ 1 đến {Math.Max(maxShort, 0)}.");

                var settled = IsPriceDiffSettled(po.Id, payments);
                var quotedUnit = QuotedUnitOf(poItem);
                var unitPaid = settled ? poItem.UnitPrice : quotedUnit;
                var goods = quotedUnit * shortage.Quantity;
                var diff = (unitPaid - quotedUnit) * shortage.Quantity;
                var fee = FeeShare(quotation, goods);
                var vat = VatOnFee(quotation, fee);

                // Thuế NK của phần thiếu: chưa tất toán thì trừ trên hoá đơn đơn kho (TAX_ADJUSTMENT);
                // tất toán xong rồi (khách đã trả thuế) thì trả lại ngay trong khoản hoàn này.
                decimal importTax = 0m;
                var taxInRefund = false;
                var warehouseOrder = await FindWarehouseOrderAsync(po.Id);
                if (warehouseOrder != null)
                {
                    var orderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                        .Where(i => i.OrderId == warehouseOrder.Id)
                        .ToList();
                    var taxByItem = await PurchaseImportTax.ByOrderItemAsync(_unitOfWork, warehouseOrder, orderItems);
                    var orderItem = PurchaseImportTax.MatchOrderItem(orderItems, poItem);
                    if (orderItem != null && taxByItem.TryGetValue(orderItem.Id, out var itemTax) && poItem.Quantity > 0)
                        importTax = Math.Round(itemTax * shortage.Quantity / poItem.Quantity, 0);
                    var (_, invoicePayments) = await FindWarehouseInvoiceAsync(warehouseOrder);
                    taxInRefund = invoicePayments.Any(p => IsStatus(p.InstallmentType, "FINAL_PAYMENT") && IsStatus(p.PaymentStatus, "PAID"));
                }

                var amount = goods + diff + fee + vat + (taxInRefund ? importTax : 0m);
                var formula = $"tiền hàng {shortage.Quantity} × {Money(unitPaid)} = {Money(goods + diff)}"
                    + (diff != 0m ? $" (gồm chênh giá {Money(diff)})" : string.Empty)
                    + $" + {FeeFormula(quotation, goods, fee, vat)}";
                if (importTax > 0m)
                    formula += taxInRefund
                        ? $" + thuế NK đã thu {Money(importTax)}"
                        : $"; thuế NK {Money(importTax)} không thu (trừ khi tất toán đơn {warehouseOrder!.ConsignmentCode})";
                formula += $" = {Money(amount)}";

                newLines.Add(new PurchaseRefundLine
                {
                    PurchaseOrderId = po.Id,
                    PurchaseRequestItemId = poItem.PurchaseRequestItemId,
                    ProductName = poItem.ProductName,
                    ReasonCode = PurchaseRefundReasons.SupplierShort,
                    Quantity = shortage.Quantity,
                    UnitPrice = unitPaid,
                    GoodsAmount = goods,
                    PriceDifferenceAmount = diff,
                    ServiceFeeAmount = fee,
                    VatAmount = vat,
                    ImportTaxAdjustment = importTax,
                    ImportTaxInRefund = taxInRefund,
                    Amount = amount,
                    Formula = formula,
                    Note = reason,
                    CreatedBy = userId
                });
            }

            if (newLines.Count == 0)
                throw new ArgumentException("Không còn phần nào chưa mua / NCC giao thiếu để hoàn.");

            var refund = await _purchasePaymentService.UpsertRefundAsync(
                purchaseRequest, null, PurchasePaymentTypes.RefundUnfulfilled, newLines, userId, mergeWithPending: false);

            await AddRequestHistoryAsync(purchaseRequest, purchaseRequest.Status ?? string.Empty,
                $"Đóng phần không mua được / NCC giao thiếu: {reason}. Hoàn {Money(refund.Amount)}.", userId);
            await _unitOfWork.SaveChangesAsync();

            // Không còn đơn mua nào và mọi sản phẩm đã đóng / hoàn → yêu cầu kết thúc (huỷ). Còn đơn kho
            // thì để luồng đóng đơn kho quyết định COMPLETED như bình thường.
            var afterLines = await LiveRefundLinesAsync(requestId);
            var hasActiveOrder = orders.Any(PurchaseOrderStatuses.IsActive);
            var everythingClosed = quoted.All(kv =>
                kv.Value.Qty - activeQty.GetValueOrDefault(kv.Key)
                - afterLines.Where(l => l.PurchaseRequestItemId == kv.Key
                                        && (IsReason(l, PurchaseRefundReasons.Unfulfilled) || IsReason(l, PurchaseRefundReasons.CancelCustomer)
                                            || IsReason(l, PurchaseRefundReasons.CancelSupplier))).Sum(l => l.Quantity)
                - legacyCancelQty.GetValueOrDefault(kv.Key) <= 0);
            if (!hasActiveOrder && everythingClosed
                && !IsStatus(purchaseRequest.Status, "CANCELLED") && !IsStatus(purchaseRequest.Status, "COMPLETED"))
            {
                await AddRequestHistoryAsync(purchaseRequest, "CANCELLED", $"Không mua được sản phẩm nào — đã hoàn tiền: {reason}", userId);
                purchaseRequest.Status = "CANCELLED";
                requestRepo.Update(purchaseRequest);
                await _unitOfWork.SaveChangesAsync();
            }
            else if (hasActiveOrder)
            {
                var closedOrder = (await _unitOfWork.Repository<Order>().GetAllAsync())
                    .FirstOrDefault(o => o.PurchaseRequestId == requestId && o.PurchaseOrderId.HasValue
                                         && IsStatus(o.Status, "COMPLETED"));
                if (closedOrder != null)
                {
                    await PurchaseRequestProgress.SyncAfterOrderClosedAsync(_unitOfWork, closedOrder);
                    await _unitOfWork.SaveChangesAsync();
                }
            }

            await NotifyCustomerAsync(purchaseRequest, "Hoàn tiền phần không mua được",
                $"Yêu cầu mua hộ {purchaseRequest.PurchaseCode}: {reason}. Chúng tôi sẽ hoàn {Money(refund.Amount)} "
                + "(tiền hàng + phí mua hộ + VAT phần phí tương ứng).");

            return new CloseUnfulfilledResponseDto
            {
                Refund = (await _purchasePaymentService.BuildRefundDtosAsync(new[] { refund })).First(),
                Summary = await _purchasePaymentService.BuildRefundSummaryAsync(purchaseRequest),
                RequestStatus = purchaseRequest.Status ?? string.Empty
            };
        }

        public Task<PurchaseRefundSummaryDto> GetRefundsAsync(Guid requestId, Guid userId, string userRole) =>
            _purchasePaymentService.GetRefundSummaryAsync(requestId, userId, userRole);

        /// <summary>Kế toán đóng một khoản hoàn cụ thể của yêu cầu (kể cả khoản không gắn đơn mua nào).</summary>
        public async Task<PurchaseRefundSummaryDto> CompleteRequestRefundAsync(
            Guid requestId, Guid refundId, Guid userId, string userRole, CompletePurchaseRefundDto dto)
        {
            if (!StaffRoleHelper.CanHandlePayments(userRole))
                throw new UnauthorizedAccessException("Chỉ Admin / Sale được xác nhận hoàn tiền.");
            var transactionCode = dto?.TransactionCode?.Trim();
            if (string.IsNullOrWhiteSpace(transactionCode))
                throw new ArgumentException("Phải nhập mã giao dịch chuyển khoản đã hoàn cho khách.");

            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            var refund = await _purchasePaymentService.MarkRefundDoneAsync(null, requestId, refundId, transactionCode, dto!.Amount);

            await NotifyCustomerAsync(purchaseRequest, "Đã hoàn tiền",
                $"Chúng tôi đã hoàn {Money(refund.Amount)} cho yêu cầu mua hộ {purchaseRequest.PurchaseCode}. Mã giao dịch: {transactionCode}.");

            return await _purchasePaymentService.BuildRefundSummaryAsync(purchaseRequest);
        }
    }
}
