using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Hàng đợi "Đơn hàng cần xử lý" của Sale, đứng sau bước khách tất toán.
    /// </summary>
    /// <remarks>
    /// Mỗi dòng là một VIỆC chứ không phải một đơn: đơn vừa có kiện giao thẳng vừa có kiện gửi
    /// lại kho sẽ ra hai dòng, hai nút, rụng độc lập. Nhờ vậy không cần giữ thêm trạng thái
    /// "đơn đã xong" ở đâu cả — hết việc thì hết dòng.
    /// </remarks>
    public partial class OrderService
    {
        public async Task<OrderActionQueueResponse> GetActionQueueAsync(string userRole)
        {
            var normalizedRole = userRole?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedRole) || !StaffRolesForQueue.Contains(normalizedRole))
            {
                throw new UnauthorizedAccessException("Vai trò hiện tại không được xem hàng đợi xử lý đơn.");
            }

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToList();
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);

            // Việc đầu chặng: khách trả cọc xong, Sale lập phiếu cho kho gốc nhận hàng. Dòng này
            // không phụ thuộc kiện hay tất toán nên tính riêng, trước cả nhánh chặng cuối bên dưới.
            var receivingRows = await BuildReceivingNoteRowsAsync(orders, customers);

            var settledOrderIds = await _settlementQueryService.GetSettledOrderIdsAsync(orders);
            if (settledOrderIds.Count == 0)
            {
                return new OrderActionQueueResponse
                {
                    Items = receivingRows.OrderByDescending(i => i.ArrivedAt ?? DateTime.MinValue).ToList(),
                };
            }

            var destinationBinIds = await GetDestinationBinIdsAsync();

            var parcelsByOrder = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId.HasValue && settledOrderIds.Contains(p.ConsignmentId.Value))
                .GroupBy(p => p.ConsignmentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var delivery = await LoadDeliveryCoverageAsync();
            var inbound = await LoadInboundCoverageAsync();

            var inspections = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync())
                .GroupBy(i => i.ParcelId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.InspectedAt).First());

            var items = new List<OrderActionQueueItem>();

            foreach (var order in orders.Where(o => settledOrderIds.Contains(o.Id)))
            {
                if (!parcelsByOrder.TryGetValue(order.Id, out var orderParcels)) continue;

                var arrived = orderParcels.Where(p => IsAtDestination(p, destinationBinIds)).ToList();
                if (arrived.Count == 0) continue;

                var customer = order.CustomerId.HasValue && customers.TryGetValue(order.CustomerId.Value, out var c)
                    ? c
                    : null;

                var direct = arrived.Where(p => ResolveHandling(p) == DirectDeliveryHandling).ToList();
                var store = arrived.Where(p => ResolveHandling(p) == StoreAtVnHandling).ToList();

                var directRow = BuildDirectDeliveryRow(order, customer, arrived, direct, delivery, inspections);
                if (directRow != null) items.Add(directRow);

                var storeRow = BuildStoreAtVnRow(order, customer, arrived, store, inbound, inspections);
                if (storeRow != null) items.Add(storeRow);
            }

            items.AddRange(receivingRows);

            return new OrderActionQueueResponse
            {
                Items = items
                    .OrderBy(i => i.CanAct ? 0 : 1)
                    .ThenByDescending(i => i.ArrivedAt ?? DateTime.MinValue)
                    .ToList(),
            };
        }

        /// <summary>
        /// Đơn ký gửi khách đã trả cọc mà chưa có phiếu tiếp nhận kho còn hiệu lực.
        /// </summary>
        /// <remarks>
        /// Không đòi tất toán và không đòi có kiện: ở mốc này hàng còn chưa tới kho gốc, kiện chỉ
        /// sinh ra sau khi kho bấm xác nhận nhận hàng theo phiếu này. Ràng buộc trạng thái phải
        /// khớp <c>WarehouseReceivingNoteService.ReceivingNoteOpenableStatuses</c>, lệch là hàng đợi
        /// bày ra một nút mà bấm vào thì API chặn.
        /// </remarks>
        private async Task<List<OrderActionQueueItem>> BuildReceivingNoteRowsAsync(
            IReadOnlyCollection<Order> orders,
            IReadOnlyDictionary<Guid, Customer> customers)
        {
            var candidates = orders
                .Where(o => !string.Equals(o.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
                .Where(o => WarehouseReceivingNoteService.ReceivingNoteOpenableStatuses.Contains(
                    (o.Status ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0) return new List<OrderActionQueueItem>();

            // Phiếu còn hiệu lực rồi thì việc đã xong, không bày lại.
            var coveredOrderIds = (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                .Where(n => WarehouseReceivingNoteService.ActiveNoteStatuses.Contains(
                    (n.Status ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
                .Select(n => n.ConsignmentOrderId)
                .ToHashSet();

            var quotationsByOrder = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId.HasValue)
                .GroupBy(q => q.OrderId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(q => string.Equals(q.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(q => q.CreatedAt).First());

            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .ToDictionary(w => w.Id, w => w);

            var rows = new List<OrderActionQueueItem>();

            foreach (var order in candidates)
            {
                if (coveredOrderIds.Contains(order.Id)) continue;

                var customer = order.CustomerId.HasValue && customers.TryGetValue(order.CustomerId.Value, out var c)
                    ? c
                    : null;

                // Kho nhận hàng lấy từ báo giá khách đã chấp nhận.
                Warehouse? warehouse = null;
                if (quotationsByOrder.TryGetValue(order.Id, out var quotation)
                    && quotation.WarehouseId.HasValue)
                {
                    warehouses.TryGetValue(quotation.WarehouseId.Value, out warehouse);
                }

                rows.Add(new OrderActionQueueItem
                {
                    OrderId = order.Id,
                    OrderCode = order.ConsignmentCode,
                    OrderType = "CONSIGNMENT",
                    Status = order.Status,
                    CustomerId = customer?.Id,
                    CustomerName = customer?.FullName,
                    CustomerPhone = customer?.Phone,
                    ReceiverName = order.ReceiverName,
                    ReceiverPhone = order.ReceiverPhone,
                    ReceiverAddress = order.ReceiverAddress,
                    Route = MapRouteToDisplayName(order.Route),
                    // Dùng để xếp thứ tự: mốc khách trả tiền, chưa có thì lấy lúc tạo đơn.
                    ArrivedAt = order.PaymentConfirmedAt ?? order.CreatedAt,

                    RowKey = $"{order.Id}:RECEIVING_NOTE",
                    HandlingGroup = "RECEIVING_NOTE",
                    HandlingGroupText = "Tiếp nhận hàng tại kho gốc",
                    ActionState = "AWAITING_RECEIVING_NOTE",
                    ActionStateText = "Khách đã thanh toán, chờ Sale lập phiếu nhập kho",

                    // Sale lập phiếu bằng POST /api/warehouse-receiving-notes; phiếu chờ quản lý kho duyệt.
                    NextAction = "CREATE_RECEIVING_NOTE",
                    CanAct = true,
                    ReceivingWarehouseId = warehouse?.Id,
                    ReceivingWarehouseName = warehouse?.WarehouseName,
                });
            }

            return rows;
        }

        /// <summary>
        /// Nhóm của kiện. Kiện chưa phân loại coi như giao thẳng — trùng mặc định lúc kho mở lô,
        /// và quan trọng hơn: không để kiện nào rơi ra ngoài cả hai nhóm rồi biến mất lặng lẽ.
        /// </summary>
        private static string ResolveHandling(Parcel parcel) =>
            string.Equals(parcel.DestinationHandling, StoreAtVnHandling, StringComparison.OrdinalIgnoreCase)
                ? StoreAtVnHandling
                : DirectDeliveryHandling;

        private OrderActionQueueItem? BuildDirectDeliveryRow(
            Order order,
            Customer? customer,
            IReadOnlyCollection<Parcel> arrived,
            IReadOnlyCollection<Parcel> group,
            DeliveryCoverage delivery,
            IReadOnlyDictionary<Guid, ParcelInspection> inspections)
        {
            // Kiện đã đi giao hoặc đã giao xong thì không còn là việc của Sale nữa.
            var pending = group
                .Where(p => !delivery.CoveredParcelIds.Contains(p.Id) && IsWaitingRelease(p))
                .ToList();

            if (pending.Count == 0) return null;

            var row = BuildBaseRow(order, customer, arrived, pending, inspections);
            row.HandlingGroup = DirectDeliveryHandling;
            row.HandlingGroupText = "Giao ngay khi về VN";
            row.RowKey = $"{order.Id}:{DirectDeliveryHandling}";
            row.ActionState = "AWAITING_DELIVERY_REQUEST";
            row.ActionStateText = "Chờ lập yêu cầu giao hàng";
            row.NextAction = "CREATE_DELIVERY_REQUEST";
            row.CanAct = true;
            return row;
        }

        private OrderActionQueueItem? BuildStoreAtVnRow(
            Order order,
            Customer? customer,
            IReadOnlyCollection<Parcel> arrived,
            IReadOnlyCollection<Parcel> group,
            InboundCoverage inbound,
            IReadOnlyDictionary<Guid, ParcelInspection> inspections)
        {
            // Mốc xong việc lấy theo KIỆN đã lên kệ, không lấy theo phiếu đã duyệt. Phiếu duyệt
            // rồi mà bước xếp kệ hỏng giữa chừng là hàng vẫn ngoài sổ; bám vào phiếu thì dòng
            // biến mất cùng với đống hàng đó và không ai còn nhìn thấy nữa.
            var pending = group
                .Where(p => string.Equals(p.PackageStatus?.Trim(), ReceivedAtDestinationStatus,
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pending.Count == 0) return null;

            var row = BuildBaseRow(order, customer, arrived, pending, inspections);
            row.HandlingGroup = StoreAtVnHandling;
            row.HandlingGroupText = "Gửi lại kho VN";
            row.RowKey = $"{order.Id}:{StoreAtVnHandling}";

            var isPurchase = string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase);
            var rejected = pending
                .Select(p => inbound.LatestRejectedByParcel.TryGetValue(p.Id, out var r) ? r : null)
                .FirstOrDefault(r => r != null);

            if (isPurchase)
            {
                // Mua hộ không đi qua bước Sale báo kho; kho lập phiếu thẳng sau khi khách trả tiền.
                row.ActionState = "NOTIFIED_AWAITING_INBOUND";
                row.ActionStateText = "Chờ kho lập phiếu nhập kho";
                row.NextAction = "NONE";
                row.CanAct = false;
                row.BlockedReason = "Đơn mua hộ vào kho thẳng, không cần Sale thông báo.";
                return row;
            }

            if (rejected != null)
            {
                row.ActionState = "INBOUND_REJECTED";
                row.ActionStateText = "Phiếu nhập kho bị từ chối";
                row.NextAction = "RENOTIFY_WAREHOUSE";
                row.CanAct = true;
                row.InboundCode = rejected.InboundCode;
                row.InboundStatus = rejected.Status;
                row.InboundRejectionReason = rejected.RejectionReason;
                return row;
            }

            var waitingApproval = pending
                .Select(p => inbound.PendingByParcel.TryGetValue(p.Id, out var r) ? r : null)
                .FirstOrDefault(r => r != null);

            if (waitingApproval != null)
            {
                row.ActionState = "INBOUND_PENDING_APPROVAL";
                row.ActionStateText = "Phiếu nhập kho chờ OM duyệt";
                row.NextAction = "NONE";
                row.CanAct = false;
                row.InboundCode = waitingApproval.InboundCode;
                row.InboundStatus = waitingApproval.Status;
                return row;
            }

            if (order.WarehouseNotifiedAt.HasValue)
            {
                row.ActionState = "NOTIFIED_AWAITING_INBOUND";
                row.ActionStateText = "Đã báo kho, chờ kho lập phiếu";
                row.NextAction = "NONE";
                row.CanAct = false;
                row.BlockedReason = "Kho đã nhận việc, chờ kho lập phiếu nhập kho.";
                return row;
            }

            row.ActionState = "AWAITING_WAREHOUSE_NOTICE";
            row.ActionStateText = "Chờ thông báo cho kho";
            row.NextAction = "NOTIFY_WAREHOUSE";
            row.CanAct = true;
            return row;
        }

        private OrderActionQueueItem BuildBaseRow(
            Order order,
            Customer? customer,
            IReadOnlyCollection<Parcel> arrived,
            IReadOnlyCollection<Parcel> group,
            IReadOnlyDictionary<Guid, ParcelInspection> inspections)
        {
            return new OrderActionQueueItem
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
                DiscrepancyParcelCount = group.Count(p =>
                    inspections.TryGetValue(p.Id, out var ins) && ins.HasDiscrepancy),
                ArrivedAt = arrived.Max(p => p.CheckInTime ?? p.PutAwayTime),
                WarehouseNotifiedAt = order.WarehouseNotifiedAt,
                GroupParcelCount = group.Count,
                GroupTotalWeight = group.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m),
                ParcelIds = group.Select(p => p.Id).ToList(),
                ReadyToDeliverParcelCount = group.Count,
            };
        }

        /// <summary>Kiện còn nằm chờ ở kho VN, chưa bị cuốn vào bước giao hay bước hoàn.</summary>
        private static bool IsWaitingRelease(Parcel parcel) =>
            parcel.PackageStatus?.Trim().ToUpperInvariant()
                is ReceivedAtDestinationStatus or StoredStatus;

        private sealed record InboundRef(string? InboundCode, string? Status, string? RejectionReason);

        private sealed class DeliveryCoverage
        {
            public HashSet<Guid> CoveredParcelIds { get; init; } = new();
        }

        private sealed class InboundCoverage
        {
            /// <summary>Kiện đang nằm trong phiếu còn hiệu lực — không lập phiếu mới cho chúng nữa.</summary>
            public HashSet<Guid> ActiveParcelIds { get; init; } = new();

            public Dictionary<Guid, InboundRef> PendingByParcel { get; init; } = new();
            public Dictionary<Guid, InboundRef> LatestRejectedByParcel { get; init; } = new();
        }

        private async Task<DeliveryCoverage> LoadDeliveryCoverageAsync()
        {
            var activeIds = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync())
                .Where(d => !string.Equals(d.Status, "DELIVERY_REJECTED", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Id)
                .ToHashSet();

            return new DeliveryCoverage
            {
                CoveredParcelIds = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                    .Where(i => activeIds.Contains(i.DeliveryRequestId))
                    .Select(i => i.ParcelId)
                    .ToHashSet(),
            };
        }

        private async Task<InboundCoverage> LoadInboundCoverageAsync()
        {
            var requests = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync())
                .ToDictionary(r => r.Id);

            var items = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync()).ToList();

            var coverage = new InboundCoverage();

            foreach (var item in items)
            {
                if (!requests.TryGetValue(item.InboundRequestId, out var request)) continue;

                var status = request.Status?.Trim().ToUpperInvariant();
                var reference = new InboundRef(request.InboundCode, request.Status, request.RejectionReason);

                if (status == "INBOUND_REJECTED")
                {
                    coverage.LatestRejectedByParcel[item.ParcelId] = reference;
                    continue;
                }

                coverage.ActiveParcelIds.Add(item.ParcelId);

                if (status == "INBOUND_PENDING")
                {
                    coverage.PendingByParcel[item.ParcelId] = reference;
                }
            }

            // Kiện đã có phiếu còn hiệu lực thì lần từ chối cũ không còn là việc phải xử lý nữa.
            foreach (var parcelId in coverage.ActiveParcelIds)
            {
                coverage.LatestRejectedByParcel.Remove(parcelId);
            }

            return coverage;
        }

        private async Task<HashSet<Guid>> GetDestinationBinIdsAsync()
        {
            var destinationWarehouseIds = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .Where(w => string.Equals(w.WarehouseType, "DESTINATION", StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Id)
                .ToHashSet();

            return (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.WarehouseId.HasValue && destinationWarehouseIds.Contains(b.WarehouseId.Value))
                .Select(b => b.Id)
                .ToHashSet();
        }
    }
}
