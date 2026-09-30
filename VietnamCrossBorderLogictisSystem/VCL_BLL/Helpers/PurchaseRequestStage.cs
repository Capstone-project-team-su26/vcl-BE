using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>Chặng thật (đầu–cuối) của một yêu cầu mua hộ, kèm nhãn tiếng Việt.</summary>
    public sealed record PurchaseStageResult(string Stage, string Text, bool IsFullyCompleted);

    /// <summary>
    /// CHẶNG THẬT của yêu cầu mua hộ — suy từ yêu cầu + đơn mua NCC + đơn kho (OrderType=PURCHASE)
    /// + khoản hoàn, không chỉ từ <c>PURCHASE_REQUESTS.status</c>.
    /// <para>
    /// Lý do: status COMPLETED được ghi bởi hai đường khác nghĩa — <see cref="PurchaseRequestProgress"/>
    /// (mọi đơn kho đã đóng → hoàn tất thật) và <see cref="PurchasePaymentEffects"/> khi khoản
    /// FINAL_PAYMENT đời cũ được trả (chỉ là tất toán, hàng có thể vẫn ở kho nước ngoài). Nhãn cũ
    /// "Hoàn tất nghiệp vụ" vì thế hiện cả trên đơn chưa giao.
    /// </para>
    /// "Đơn đã hoàn tất" chỉ khi: yêu cầu đã được hệ thống chốt COMPLETED, mọi phần hàng (đơn mua
    /// chưa huỷ + đơn kho của nó, hoặc đơn kho đời cũ) đều COMPLETED, và không còn khoản hoàn chờ chuyển.
    /// Bảng chặng khớp với web Sale (vcl-admin-ui purchaseRequestStage.js).
    /// </summary>
    public static class PurchaseRequestStage
    {
        public const string AwaitingQuote = "AWAITING_QUOTE";
        public const string NeedMoreInfo = "NEED_MORE_INFO";
        public const string AwaitingPayment = "AWAITING_PAYMENT";
        public const string Prepaid = "PREPAID";
        public const string OrderingSupplier = "ORDERING_SUPPLIER";
        public const string InTransit = "IN_TRANSIT";
        public const string ArrivedVn = "ARRIVED_VN";
        public const string Delivered = "DELIVERED";
        public const string AwaitingRefund = "AWAITING_REFUND";
        public const string Completed = "COMPLETED";
        public const string Cancelled = "CANCELLED";
        public const string Rejected = "REJECTED";

        private static readonly Dictionary<string, string> Texts = new(StringComparer.OrdinalIgnoreCase)
        {
            [AwaitingQuote] = "Chờ báo giá",
            [NeedMoreInfo] = "Chờ khách bổ sung thông tin",
            [AwaitingPayment] = "Đã báo giá – chờ khách thanh toán",
            [Prepaid] = "Đã thanh toán trước",
            [OrderingSupplier] = "Đang đặt hàng NCC",
            [InTransit] = "Đang vận chuyển",
            [ArrivedVn] = "Đã về kho VN",
            [Delivered] = "Đã giao – chờ đóng đơn",
            [AwaitingRefund] = "Chờ hoàn tiền cho khách",
            [Completed] = "Đơn đã hoàn tất",
            [Cancelled] = "Đã huỷ",
            [Rejected] = "Đã từ chối",
        };

        /// <summary>Mã chặng hợp lệ cho tham số lọc <c>stage</c>.</summary>
        public static IReadOnlyCollection<string> All => Texts.Keys;

        public static bool IsKnown(string? stage) => !string.IsNullOrWhiteSpace(stage) && Texts.ContainsKey(stage.Trim());

        public static string Text(string stage) => Texts.TryGetValue(stage, out var text) ? text : stage;

        private static readonly Dictionary<string, int> Rank = new(StringComparer.OrdinalIgnoreCase)
        {
            [AwaitingQuote] = 1, [NeedMoreInfo] = 1, [AwaitingPayment] = 2, [Prepaid] = 3,
            [OrderingSupplier] = 4, [InTransit] = 5, [ArrivedVn] = 6, [Delivered] = 7,
            [AwaitingRefund] = 8, [Completed] = 9,
        };

        /// <summary>Trạng thái yêu cầu khi chưa có phần hàng nào để đối chiếu.</summary>
        private static readonly Dictionary<string, string> RequestStatusStage = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NEW"] = AwaitingQuote, ["PENDING_REVIEW"] = AwaitingQuote, ["IN_REVIEW"] = AwaitingQuote,
            ["NEED_MORE_INFO"] = NeedMoreInfo,
            ["QUOTED"] = AwaitingPayment, ["WAITING_PAYMENT"] = AwaitingPayment,
            ["PAID"] = Prepaid, ["DEPOSIT_PAID"] = Prepaid,
            ["PURCHASING"] = OrderingSupplier, ["PURCHASED"] = OrderingSupplier,
            // Đời cũ: các mốc sau đều là hàng CHƯA về Việt Nam (đợt cuối thu ở kho nguồn).
            ["SELLER_SHIPPED"] = InTransit, ["ARRIVED_ORIGIN_WAREHOUSE"] = InTransit, ["WAITING_STORED"] = InTransit,
            ["STORED"] = InTransit, ["WAITING_FINAL_PAYMENT"] = InTransit,
        };

        private static readonly Dictionary<string, string> TerminalRequestStage = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CANCELLED"] = Cancelled, ["REJECTED"] = Rejected, ["QUOTATION_REJECTED"] = Rejected,
        };

        /// <summary>Đơn kho đã nhận hàng trở đi; trạng thái trước nhận hàng → theo đơn mua NCC.</summary>
        private static readonly Dictionary<string, string> WarehouseOrderStage = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CHECKED_IN"] = InTransit, ["WAREHOUSE_RECEIVED"] = InTransit, ["IN_TRANSIT"] = InTransit,
            ["AT_DESTINATION_WAREHOUSE"] = ArrivedVn, ["WAITING_FINAL_PAYMENT"] = ArrivedVn,
            ["DELIVERED"] = Delivered, ["CUSTOMER_CONFIRMED"] = Delivered,
            ["COMPLETED"] = Completed,
        };

        private static readonly HashSet<string> ClosedWarehouseOrder = new(StringComparer.OrdinalIgnoreCase)
        { "CANCELLED", "CANCELLED_FORFEITED" };

        private static string Norm(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

        private static PurchaseStageResult Result(string stage) => new(stage, Text(stage), stage == Completed);

        private static string Slowest(IEnumerable<string> stages) =>
            stages.OrderBy(s => Rank.TryGetValue(s, out var r) ? r : 0).First();

        /// <summary>
        /// Suy chặng cho một yêu cầu.
        /// </summary>
        /// <param name="request">Yêu cầu mua hộ.</param>
        /// <param name="purchaseOrders">Mọi đơn mua NCC của yêu cầu (kể cả đã huỷ).</param>
        /// <param name="warehouseOrders">Mọi đơn kho (ORDERS) gắn với yêu cầu.</param>
        /// <param name="hasPendingRefund">Còn khoản hoàn PENDING chờ chuyển trả khách.</param>
        /// <param name="openLineCount">Số dòng còn đưa được vào đơn mua mới (PurchaseRequestListItemDto.OpenLineCount).</param>
        public static PurchaseStageResult Compute(
            PurchaseRequest request,
            IReadOnlyCollection<PurchaseOrder> purchaseOrders,
            IReadOnlyCollection<Order> warehouseOrders,
            bool hasPendingRefund,
            int openLineCount)
        {
            var status = Norm(request.Status);
            if (TerminalRequestStage.TryGetValue(status, out var terminal))
                return Result(terminal);

            var activeOrders = purchaseOrders.Where(PurchaseOrderStatuses.IsActive).ToList();
            var parts = new List<string>();

            if (activeOrders.Count > 0)
            {
                foreach (var po in activeOrders)
                {
                    var warehouseStatus = Norm(warehouseOrders.FirstOrDefault(o => o.PurchaseOrderId == po.Id)?.Status);
                    if (ClosedWarehouseOrder.Contains(warehouseStatus)) continue;

                    parts.Add(WarehouseOrderStage.TryGetValue(warehouseStatus, out var fromWarehouse)
                        ? fromWarehouse
                        : PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.SupplierShipped) ? InTransit : OrderingSupplier);
                }

                // Còn dòng chưa đưa vào đơn mua nào → phần đó vẫn ở bước đặt NCC.
                if (openLineCount > 0) parts.Add(OrderingSupplier);
            }
            else
            {
                // Đời cũ: không có đơn mua NCC, đơn kho gắn thẳng vào yêu cầu.
                foreach (var order in warehouseOrders.Where(o => o.PurchaseOrderId == null))
                {
                    var warehouseStatus = Norm(order.Status);
                    if (ClosedWarehouseOrder.Contains(warehouseStatus)) continue;

                    var floor = RequestStatusStage.TryGetValue(status, out var f) ? f : Prepaid;
                    if (WarehouseOrderStage.TryGetValue(warehouseStatus, out var fromWarehouse))
                        parts.Add(status == "COMPLETED" || Rank[fromWarehouse] >= Rank[floor] ? fromWarehouse : floor);
                    else
                        // COMPLETED đời cũ = đã tất toán ở kho nguồn, hàng chưa về VN.
                        parts.Add(status == "COMPLETED" ? InTransit : floor);
                }
            }

            if (parts.Count == 0)
            {
                if (status == "COMPLETED")
                {
                    // Mọi đơn mua đã huỷ / đóng: xong nếu không còn khoản hoàn treo.
                    if (purchaseOrders.Count > 0)
                        return Result(hasPendingRefund ? AwaitingRefund : Completed);
                    // Không có gì để đối chiếu thì không được tự nhận hoàn tất.
                    return Result(InTransit);
                }

                if (!RequestStatusStage.TryGetValue(status, out var byStatus))
                    return Result(AwaitingQuote);

                // Đã có đơn mua (kể cả đã huỷ hết) mà yêu cầu vẫn PAID → vẫn là bước đặt NCC.
                return Result(byStatus == Prepaid && purchaseOrders.Count > 0 ? OrderingSupplier : byStatus);
            }

            var stage = Slowest(parts);
            if (stage == Completed && hasPendingRefund) stage = AwaitingRefund;
            // Hệ thống chưa chốt yêu cầu (còn phần chưa đóng) → chưa gọi là hoàn tất.
            if (stage == Completed && status != "COMPLETED") stage = Delivered;

            return Result(stage);
        }

        /// <summary>
        /// Chặng cho nhiều yêu cầu một lượt: đọc đơn mua / đơn kho / khoản hoàn một lần rồi gom theo yêu cầu.
        /// </summary>
        public static async Task<Dictionary<Guid, PurchaseStageResult>> ComputeAsync(
            IUnitOfWork unitOfWork,
            IReadOnlyCollection<PurchaseRequest> requests,
            IReadOnlyDictionary<Guid, int>? openLineCounts = null)
        {
            var result = new Dictionary<Guid, PurchaseStageResult>();
            if (requests.Count == 0) return result;

            var ids = requests.Select(r => r.Id).ToHashSet();

            var posByRequest = (await unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(po => ids.Contains(po.PurchaseRequestId))
                .GroupBy(po => po.PurchaseRequestId)
                .ToDictionary(g => g.Key, g => (IReadOnlyCollection<PurchaseOrder>)g.ToList());

            var ordersByRequest = (await unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => o.PurchaseRequestId.HasValue && ids.Contains(o.PurchaseRequestId.Value))
                .GroupBy(o => o.PurchaseRequestId!.Value)
                .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Order>)g.ToList());

            var pendingRefundRequests = (await unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => ids.Contains(p.PurchaseRequestId) && PurchasePaymentTypes.IsRefund(p)
                            && string.Equals(p.Status?.Trim(), PurchaseRefundStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.PurchaseRequestId)
                .ToHashSet();

            foreach (var request in requests)
            {
                result[request.Id] = Compute(
                    request,
                    posByRequest.TryGetValue(request.Id, out var pos) ? pos : Array.Empty<PurchaseOrder>(),
                    ordersByRequest.TryGetValue(request.Id, out var orders) ? orders : Array.Empty<Order>(),
                    pendingRefundRequests.Contains(request.Id),
                    openLineCounts != null && openLineCounts.TryGetValue(request.Id, out var open) ? open : 0);
            }

            return result;
        }
    }
}
