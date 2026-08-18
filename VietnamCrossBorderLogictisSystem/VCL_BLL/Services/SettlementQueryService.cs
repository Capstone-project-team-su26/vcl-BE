using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <inheritdoc cref="ISettlementQueryService"/>
    public class SettlementQueryService : ISettlementQueryService
    {
        private readonly IUnitOfWork _unitOfWork;

        private const string Deposit = "DEPOSIT";
        private const string FinalPayment = "FINAL_PAYMENT";

        /// <summary>
        /// Giá trị đời đầu của ĐỢT CỌC bên mua hộ, không phải "đã trả đủ".
        /// </summary>
        /// <remarks>
        /// Cột <c>PURCHASE_PAYMENTS.payment_type</c> lấy nó làm mặc định, và
        /// <c>PurchasePaymentService</c> vẫn dùng nó để nhận biết khách đã trả đợt đầu. Cho nên
        /// coi <c>FULL_PAYMENT</c> là tất toán sẽ mở cửa kho cho đơn mới trả mỗi tiền cọc.
        /// Chỉ chấp nhận nó khi đơn không hề có đợt cọc lẫn đợt cuối nào khác — tức đơn đời cũ
        /// thu một lần.
        /// </remarks>
        private const string LegacyFullPayment = "FULL_PAYMENT";

        public SettlementQueryService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<bool> IsSettledAsync(Order order)
        {
            if (order == null) return false;

            var settled = await GetSettledOrderIdsAsync(new[] { order });
            return settled.Contains(order.Id);
        }

        public async Task<HashSet<Guid>> GetSettledOrderIdsAsync(IReadOnlyCollection<Order> orders)
        {
            var settled = new HashSet<Guid>();
            if (orders == null || orders.Count == 0) return settled;

            var orderIds = orders.Select(o => o.Id).ToHashSet();

            await CollectConsignmentSettlementsAsync(orderIds, settled);
            await CollectPurchaseSettlementsAsync(orders, settled);

            return settled;
        }

        /// <summary>Ký gửi: QUOTATIONS → INVOICES → PAYMENTS, chỉ nhận đợt cuối đã trả.</summary>
        private async Task CollectConsignmentSettlementsAsync(
            IReadOnlySet<Guid> orderIds, HashSet<Guid> settled)
        {
            var quotationToOrder = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId.HasValue && orderIds.Contains(q.OrderId!.Value))
                .GroupBy(q => q.Id)
                .ToDictionary(g => g.Key, g => g.First().OrderId!.Value);

            if (quotationToOrder.Count == 0) return;

            var invoiceToOrder = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationToOrder.ContainsKey(i.QuotationId!.Value))
                .ToDictionary(i => i.Id, i => quotationToOrder[i.QuotationId!.Value]);

            foreach (var payment in await _unitOfWork.Repository<Payment>().GetAllAsync())
            {
                if (!payment.InvoiceId.HasValue) continue;
                if (!IsPaid(payment.PaymentStatus)) continue;
                if (!Matches(payment.InstallmentType, FinalPayment)) continue;

                if (invoiceToOrder.TryGetValue(payment.InvoiceId.Value, out var orderId))
                {
                    settled.Add(orderId);
                }
            }
        }

        /// <summary>Mua hộ: PURCHASE_PAYMENTS nối qua <see cref="Order.PurchaseRequestId"/>.</summary>
        private async Task CollectPurchaseSettlementsAsync(
            IReadOnlyCollection<Order> orders, HashSet<Guid> settled)
        {
            var orderByRequest = orders
                .Where(o => o.PurchaseRequestId.HasValue)
                .GroupBy(o => o.PurchaseRequestId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Id);

            if (orderByRequest.Count == 0) return;

            var paidByRequest = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => orderByRequest.ContainsKey(p.PurchaseRequestId) && IsPaid(p.Status))
                .GroupBy(p => p.PurchaseRequestId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (requestId, payments) in paidByRequest)
            {
                var hasFinal = payments.Any(p => Matches(p.PaymentType, FinalPayment));

                // Đơn đời cũ thu một lần: chỉ có đúng dòng FULL_PAYMENT, không có cọc, không có
                // đợt cuối. Đơn đời mới luôn sinh DEPOSIT trước nên không lọt vào nhánh này.
                var isLegacyOneShot =
                    !hasFinal
                    && payments.Any(p => Matches(p.PaymentType, LegacyFullPayment))
                    && !payments.Any(p => Matches(p.PaymentType, Deposit));

                if (hasFinal || isLegacyOneShot)
                {
                    settled.Add(orderByRequest[requestId]);
                }
            }
        }

        private static bool IsPaid(string? status) =>
            status?.Trim().ToUpperInvariant() is "PAID" or "SUCCESS" or "COMPLETED";

        /// <summary>
        /// So loại đợt thanh toán. Giá trị rỗng coi là KHÔNG khớp — an toàn về tiền, vì cột
        /// <c>payment_type</c> có mặc định <c>FULL_PAYMENT</c> nên dòng bỏ trống rất dễ bị hiểu
        /// nhầm thành đã trả đủ.
        /// </summary>
        private static bool Matches(string? value, string expected) =>
            string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
