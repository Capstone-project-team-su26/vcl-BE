using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>Nhãn "Mua hộ" / "Ký gửi" của một đơn kho — dùng cho các màn xuất kho, lô vận chuyển.</summary>
    public sealed class OrderPurchaseLabel
    {
        public static readonly OrderPurchaseLabel Consignment = new();

        public bool IsPurchase { get; init; }
        public string OrderType => IsPurchase ? "PURCHASE" : "CONSIGNMENT";

        /// <summary>Mã yêu cầu mua hộ (PUR-…). Không kèm thông tin NCC.</summary>
        public string? PurchaseCode { get; init; }
    }

    /// <summary>
    /// Nạp nhãn mua hộ cho một lô đơn kho: tối đa 2 truy vấn (đơn mua NCC khi đơn kho chỉ gắn đơn
    /// mua, rồi yêu cầu mua hộ) cho cả lô — không truy vấn theo từng kiện.
    /// </summary>
    public static class OrderPurchaseLabels
    {
        public static bool IsPurchaseOrder(Order? order) =>
            order != null
            && (string.Equals(order.OrderType?.Trim(), "PURCHASE", StringComparison.OrdinalIgnoreCase)
                || order.PurchaseRequestId.HasValue
                || order.PurchaseOrderId.HasValue);

        public static async Task<Dictionary<Guid, OrderPurchaseLabel>> LoadAsync(IUnitOfWork unitOfWork, IEnumerable<Order?> orders)
        {
            var purchaseOrders = orders
                .Where(IsPurchaseOrder)
                .Select(o => o!)
                .GroupBy(o => o.Id)
                .Select(g => g.First())
                .ToList();
            var result = new Dictionary<Guid, OrderPurchaseLabel>();
            if (purchaseOrders.Count == 0) return result;

            var poIds = purchaseOrders
                .Where(o => !o.PurchaseRequestId.HasValue && o.PurchaseOrderId.HasValue)
                .Select(o => o.PurchaseOrderId!.Value)
                .Distinct()
                .ToList();
            var requestIdByPo = poIds.Count == 0
                ? new Dictionary<Guid, Guid?>()
                : (await unitOfWork.Repository<PurchaseOrder>().FindAsync(p => poIds.Contains(p.Id)))
                    .ToDictionary(p => p.Id, p => (Guid?)p.PurchaseRequestId);

            Guid? RequestIdOf(Order o) =>
                o.PurchaseRequestId
                ?? (o.PurchaseOrderId.HasValue && requestIdByPo.TryGetValue(o.PurchaseOrderId.Value, out var rid) ? rid : null);

            var requestIds = purchaseOrders.Select(RequestIdOf).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var codes = requestIds.Count == 0
                ? new Dictionary<Guid, string?>()
                : (await unitOfWork.Repository<PurchaseRequest>().FindAsync(r => requestIds.Contains(r.Id)))
                    .ToDictionary(r => r.Id, r => r.PurchaseCode);

            foreach (var o in purchaseOrders)
            {
                var rid = RequestIdOf(o);
                var code = rid.HasValue && codes.TryGetValue(rid.Value, out var c) ? c : null;
                result[o.Id] = new OrderPurchaseLabel
                {
                    IsPurchase = true,
                    PurchaseCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim()
                };
            }

            return result;
        }

        public static OrderPurchaseLabel Of(Dictionary<Guid, OrderPurchaseLabel> labels, Guid? orderId) =>
            orderId.HasValue && labels.TryGetValue(orderId.Value, out var label) ? label : OrderPurchaseLabel.Consignment;
    }
}
