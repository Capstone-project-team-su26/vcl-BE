using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>Mã lý do kiện chưa xuất được — FE hiện nguyên văn cho kho biết vướng gì.</summary>
    public static class ExportBlockReasons
    {
        public const string NotStored = "NOT_STORED";
        public const string NotInStorageZone = "NOT_IN_STORAGE_ZONE";
        public const string WrongWarehouse = "WRONG_WAREHOUSE";
        public const string OrderClosed = "ORDER_CLOSED";
        public const string DepositNotConfirmed = "DEPOSIT_NOT_CONFIRMED";
        public const string ReceivingNotSettled = "RECEIVING_NOT_SETTLED";
        public const string ExportHold = "EXPORT_HOLD";
        public const string RestrictedNoPermit = "RESTRICTED_NO_PERMIT";
        public const string AlreadyInExport = "ALREADY_IN_EXPORT";
        public const string InventoryNotAvailable = "INVENTORY_NOT_AVAILABLE";

        public static string Describe(string code) => code switch
        {
            NotStored => "Kiện chưa ở trạng thái đã lưu kho.",
            NotInStorageZone => "Kiện không nằm ở ô kệ thuộc khu lưu kho đang dùng.",
            WrongWarehouse => "Kiện không nằm ở kho nguồn của tuyến.",
            OrderClosed => "Đơn đã đóng (huỷ / từ chối / hoàn tất).",
            DepositNotConfirmed => "Đơn chưa có khoản cọc được xác nhận.",
            ReceivingNotSettled => "Phiếu nhập kho của đơn chưa được quản lý kho duyệt xong.",
            ExportHold => "Đơn đang bật giữ hàng tại kho nguồn.",
            RestrictedNoPermit => "Đơn có hàng hạn chế nhưng chưa đính kèm giấy phép.",
            AlreadyInExport => "Kiện đã nằm trong một phiếu xuất khác.",
            InventoryNotAvailable => "Kiện không có dòng tồn kho sẵn sàng (đang giữ hoặc sai sổ).",
            _ => code
        };
    }

    /// <summary>Kết quả xét một kiện: kiện, chỗ đang nằm, đơn, và các lý do chưa xuất được.</summary>
    public sealed class ExportParcelCandidate
    {
        public Parcel Parcel { get; init; }
        public Order Order { get; init; }
        public OrderItem OrderItem { get; init; }
        public Bin Bin { get; init; }
        public Shelf Shelf { get; init; }
        public WarehouseZone Zone { get; init; }
        public Warehouse Warehouse { get; init; }

        /// <summary>Dòng tồn kho sẽ bị khoá khi gửi duyệt phiếu.</summary>
        public Inventory Inventory { get; init; }

        public List<string> Reasons { get; } = new();
        public bool IsEligible => Reasons.Count == 0;
    }

    /// <summary>
    /// Xét kiện nào xuất được, theo đúng bảng lý do ở docs/migrate-source-cu/06-luong-xuat-kho-quoc-te.md.
    /// <para>
    /// Nạp một lần rồi xét nhiều kiện: màn "kiện đủ điều kiện" và các bước lập / gửi duyệt / duyệt
    /// phiếu đều phải xét lại cùng bộ điều kiện, nên chúng đi qua đúng một nơi để không lệch nhau.
    /// </para>
    /// </summary>
    public sealed class ExportEligibilityContext
    {
        private static readonly HashSet<string> ClosedOrderStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "CANCELLED", "REJECTED", "COMPLETED", "CLOSED" };

        /// <summary>Phiếu xuất còn hiệu lực — kiện trong đó không cho phiếu khác lấy.</summary>
        public static readonly HashSet<string> ActiveWroStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "DRAFT", "PENDING_APPROVAL", "APPROVED", "PICKING", "READY", "IN_SHIPMENT" };

        private List<Parcel> _parcels;
        private Dictionary<Guid, Bin> _bins;
        private Dictionary<Guid, Shelf> _shelves;
        private Dictionary<Guid, WarehouseZone> _zones;
        private Dictionary<Guid, Warehouse> _warehouses;
        private Dictionary<Guid, Order> _orders;
        private Dictionary<Guid, OrderItem> _orderItems;
        private List<Inventory> _inventories;
        private HashSet<Guid> _depositPaidOrderIds;
        private HashSet<Guid> _unsettledReceivingOrderIds;
        private HashSet<Guid> _restrictedOrderIdsWithoutPermit;
        private Dictionary<Guid, Guid> _parcelToActiveWro;

        public static async Task<ExportEligibilityContext> LoadAsync(IUnitOfWork uow)
        {
            var ctx = new ExportEligibilityContext();

            ctx._parcels = (await uow.Repository<Parcel>().GetAllAsync()).ToList();
            ctx._bins = (await uow.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id);
            ctx._shelves = (await uow.Repository<Shelf>().GetAllAsync()).ToDictionary(s => s.Id);
            ctx._zones = (await uow.Repository<WarehouseZone>().GetAllAsync()).ToDictionary(z => z.Id);
            ctx._warehouses = (await uow.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id);
            ctx._orders = (await uow.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);
            ctx._orderItems = (await uow.Repository<OrderItem>().GetAllAsync()).ToDictionary(i => i.Id);
            ctx._inventories = (await uow.Repository<Inventory>().GetAllAsync()).ToList();

            ctx._depositPaidOrderIds = await LoadDepositPaidOrdersAsync(uow);
            ctx._unsettledReceivingOrderIds = (await uow.Repository<WarehouseReceivingNote>().GetAllAsync())
                .Where(n => !string.Equals(n.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.ConsignmentOrderId)
                .ToHashSet();

            ctx._restrictedOrderIdsWithoutPermit = await LoadRestrictedOrdersWithoutPermitAsync(uow, ctx._orderItems.Values);

            var activeWros = (await uow.Repository<WroRequest>().GetAllAsync())
                .Where(w => ActiveWroStatuses.Contains(w.Status?.Trim() ?? string.Empty))
                .Select(w => w.Id)
                .ToHashSet();

            ctx._parcelToActiveWro = (await uow.Repository<WroParcel>().GetAllAsync())
                .Where(wp => activeWros.Contains(wp.WroRequestId)
                             && !string.Equals(wp.Status, WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase))
                .GroupBy(wp => wp.ParcelId)
                .ToDictionary(g => g.Key, g => g.First().WroRequestId);

            return ctx;
        }

        private static async Task<HashSet<Guid>> LoadDepositPaidOrdersAsync(IUnitOfWork uow)
        {
            var quotations = (await uow.Repository<Quotation>().GetAllAsync()).ToList();
            var invoices = (await uow.Repository<Invoice>().GetAllAsync()).ToList();
            var payments = (await uow.Repository<Payment>().GetAllAsync()).ToList();

            var paidInvoiceIds = payments
                .Where(p => p.InvoiceId.HasValue
                            && string.Equals(p.InstallmentType, "DEPOSIT", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.InvoiceId!.Value)
                .ToHashSet();

            var paidQuotationIds = invoices
                .Where(i => paidInvoiceIds.Contains(i.Id) && i.QuotationId.HasValue)
                .Select(i => i.QuotationId!.Value)
                .ToHashSet();

            return quotations
                .Where(q => paidQuotationIds.Contains(q.Id) && q.OrderId.HasValue)
                .Select(q => q.OrderId!.Value)
                .ToHashSet();
        }

        /// <summary>
        /// Đơn có hàng thuộc danh mục cấm/hạn chế mà chưa đính kèm giấy phép. Đối chiếu theo tên
        /// hàng và loại hàng, giống bước kiểm lúc khách tạo đơn.
        /// </summary>
        private static async Task<HashSet<Guid>> LoadRestrictedOrdersWithoutPermitAsync(
            IUnitOfWork uow, IEnumerable<OrderItem> orderItems)
        {
            var restricted = (await uow.Repository<RestrictedItem>().GetAllAsync())
                .Where(r => r.IsActive && !string.IsNullOrWhiteSpace(r.ItemName))
                .ToList();
            if (restricted.Count == 0) return new HashSet<Guid>();

            var flagged = new HashSet<Guid>();
            foreach (var item in orderItems)
            {
                if (!item.OrderId.HasValue) continue;
                if (flagged.Contains(item.OrderId.Value)) continue;

                foreach (var r in restricted)
                {
                    var matchesName = !string.IsNullOrWhiteSpace(item.ProductName)
                        && item.ProductName.Contains(r.ItemName, StringComparison.OrdinalIgnoreCase);
                    var matchesType = !string.IsNullOrWhiteSpace(item.ProductType)
                        && item.ProductType.Contains(r.ItemName, StringComparison.OrdinalIgnoreCase);
                    if (matchesName || matchesType)
                    {
                        flagged.Add(item.OrderId.Value);
                        break;
                    }
                }
            }

            if (flagged.Count == 0) return flagged;

            var permittedOrderIds = (await uow.Repository<Attachment>().GetAllAsync())
                .Where(a => string.Equals(a.EntityType, AttachmentEntityTypes.Order, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.DocumentType, AttachmentDocumentTypes.Permit, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.EntityId)
                .ToHashSet();

            flagged.ExceptWith(permittedOrderIds);
            return flagged;
        }

        /// <summary>Kiện đang nằm trong phiếu xuất nào (trừ phiếu đang xét).</summary>
        public Guid? ActiveWroOf(Guid parcelId, Guid? ignoreWroRequestId = null)
        {
            if (!_parcelToActiveWro.TryGetValue(parcelId, out var wroId)) return null;
            if (ignoreWroRequestId.HasValue && wroId == ignoreWroRequestId.Value) return null;
            return wroId;
        }

        public Parcel FindParcel(Guid parcelId) => _parcels.FirstOrDefault(p => p.Id == parcelId);

        public Parcel FindParcelByCode(string packageCode) => string.IsNullOrWhiteSpace(packageCode)
            ? null
            : _parcels.FirstOrDefault(p => string.Equals(p.PackageCode?.Trim(), packageCode.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Dòng tồn sẵn sàng khoá của một kiện.</summary>
        public Inventory AvailableInventoryOf(Guid parcelId) => _inventories
            .FirstOrDefault(i => i.ParcelId == parcelId
                                 && string.Equals(i.Status, InventoryStatuses.Available, StringComparison.OrdinalIgnoreCase));

        /// <summary>Xét toàn bộ kiện đang ở một kho nguồn.</summary>
        public List<ExportParcelCandidate> EvaluateWarehouse(Guid originWarehouseId, Guid? ignoreWroRequestId = null)
        {
            var result = new List<ExportParcelCandidate>();
            foreach (var parcel in _parcels)
            {
                if (!parcel.BinId.HasValue) continue;
                var warehouseId = WarehouseOf(parcel.BinId.Value);
                if (warehouseId != originWarehouseId) continue;

                result.Add(Evaluate(parcel, originWarehouseId, ignoreWroRequestId));
            }

            return result
                .OrderByDescending(c => c.IsEligible)
                .ThenBy(c => c.Parcel.PackageCode)
                .ToList();
        }

        public ExportParcelCandidate Evaluate(Parcel parcel, Guid originWarehouseId, Guid? ignoreWroRequestId = null)
        {
            Bin bin = parcel.BinId.HasValue && _bins.TryGetValue(parcel.BinId.Value, out var b) ? b : null;
            Shelf shelf = bin?.ShelfId != null && _shelves.TryGetValue(bin.ShelfId.Value, out var s) ? s : null;
            WarehouseZone zone = shelf?.ZoneId != null && _zones.TryGetValue(shelf.ZoneId.Value, out var z) ? z : null;
            Warehouse warehouse = zone?.WarehouseId != null && _warehouses.TryGetValue(zone.WarehouseId.Value, out var w) ? w : null;

            Order order = parcel.ConsignmentId.HasValue && _orders.TryGetValue(parcel.ConsignmentId.Value, out var o) ? o : null;
            OrderItem orderItem = parcel.OrderItemId.HasValue && _orderItems.TryGetValue(parcel.OrderItemId.Value, out var oi) ? oi : null;
            var inventory = AvailableInventoryOf(parcel.Id);

            var candidate = new ExportParcelCandidate
            {
                Parcel = parcel,
                Order = order,
                OrderItem = orderItem,
                Bin = bin,
                Shelf = shelf,
                Zone = zone,
                Warehouse = warehouse,
                Inventory = inventory
            };

            if (!string.Equals(parcel.PackageStatus?.Trim(), ParcelStatuses.Stored, StringComparison.OrdinalIgnoreCase))
                candidate.Reasons.Add(ExportBlockReasons.NotStored);

            if (zone == null || !BinPlacementPolicy.IsStorageZone(zone) || !BinPlacementPolicy.IsZoneActive(zone))
                candidate.Reasons.Add(ExportBlockReasons.NotInStorageZone);

            if (warehouse == null || warehouse.Id != originWarehouseId)
                candidate.Reasons.Add(ExportBlockReasons.WrongWarehouse);

            if (order == null || ClosedOrderStatuses.Contains(order.Status?.Trim() ?? string.Empty))
                candidate.Reasons.Add(ExportBlockReasons.OrderClosed);

            if (order != null)
            {
                // Mua hộ đã thu tiền hàng ở luồng mua, không có cọc ký gửi để đối chiếu.
                var isPurchase = string.Equals(order.OrderType?.Trim(), "PURCHASE", StringComparison.OrdinalIgnoreCase);
                if (!isPurchase && !_depositPaidOrderIds.Contains(order.Id))
                    candidate.Reasons.Add(ExportBlockReasons.DepositNotConfirmed);

                if (_unsettledReceivingOrderIds.Contains(order.Id))
                    candidate.Reasons.Add(ExportBlockReasons.ReceivingNotSettled);

                if (order.ExportHold)
                    candidate.Reasons.Add(ExportBlockReasons.ExportHold);

                if (_restrictedOrderIdsWithoutPermit.Contains(order.Id))
                    candidate.Reasons.Add(ExportBlockReasons.RestrictedNoPermit);
            }

            if (ActiveWroOf(parcel.Id, ignoreWroRequestId).HasValue)
                candidate.Reasons.Add(ExportBlockReasons.AlreadyInExport);

            if (inventory == null)
                candidate.Reasons.Add(ExportBlockReasons.InventoryNotAvailable);

            return candidate;
        }

        /// <summary>Kho chứa một ô kệ.</summary>
        public Guid? WarehouseOf(Guid binId)
        {
            if (!_bins.TryGetValue(binId, out var bin) || bin.ShelfId == null) return null;
            if (!_shelves.TryGetValue(bin.ShelfId.Value, out var shelf)) return null;
            if (shelf.ZoneId == null || !_zones.TryGetValue(shelf.ZoneId.Value, out var zone)) return null;
            return zone.WarehouseId;
        }

        public Warehouse GetWarehouse(Guid warehouseId) =>
            _warehouses.TryGetValue(warehouseId, out var w) ? w : null;

        public Order GetOrder(Guid orderId) => _orders.TryGetValue(orderId, out var o) ? o : null;

        public Bin GetBin(Guid binId) => _bins.TryGetValue(binId, out var b) ? b : null;
    }
}
