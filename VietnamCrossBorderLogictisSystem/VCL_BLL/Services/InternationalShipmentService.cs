using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class InternationalShipmentService : IInternationalShipmentService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Gom kiện thành lô vận chuyển quốc tế.
        // Phong xác nhận 14/08: người gom lô là NHÂN VIÊN KHO QUỐC TẾ, không phải OM. Đúng nghiệp
        // vụ — người đang đứng cạnh container mới biết còn chỗ cho mấy kiện, OM ngồi văn phòng
        // không xếp hàng lên xe được. OM/Staff/Admin vẫn giữ quyền để hỗ trợ khi kho vướng.
        // Sale KHÔNG có trong danh sách này: sale chỉ theo dõi hành trình lô đã có.
        private static readonly string[] ConsolidationRoles =
            { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Cập nhật hành trình lô (trạng thái + xác nhận đến kho đích): thêm cả Sale.
        // Anh Hoàng chốt 11/08 — bên thứ 3 vận chuyển có trang quản lý riêng, sale vào đó xem
        // lô đi tới đâu / thông quan ra sao rồi gõ tay vào hệ thống mình; "vấn đề cho khách xem
        // thì để sale xử lý". Sale không tạo được lô mới, chỉ cập nhật hành trình lô đã có.
        private static readonly string[] TrackingUpdateRoles =
            { "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Nhận kiện tại kho đích: thêm cả Warehouse Staff
        private static readonly string[] ReceivingRoles = { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Hai hướng kiện có thể đi sau khi kho VN mở lô kiểm đếm.
        private const string DirectDelivery = "DIRECT_DELIVERY";
        private const string StoreAtVn = "STORE_AT_VN";

        /// <summary>Nhãn tiếng Việt của hướng xử lý, để FE hiện thẳng khỏi tự map.</summary>
        private static string? DescribeHandling(string? handling) => handling?.Trim().ToUpperInvariant() switch
        {
            DirectDelivery => "Khách muốn giao ngay khi về VN",
            StoreAtVn => "Khách muốn gửi lại kho VN",
            _ => null,
        };

        /// <summary>
        /// Chuẩn hoá hướng xử lý về đúng một trong hai giá trị hợp lệ. Trả null khi bỏ trống để
        /// bên gọi tự quyết mặc định. Gõ sai thì báo lỗi ngay chứ không lặng lẽ nhận bừa — sai
        /// hướng là kiện đi nhầm nhánh, phát hiện ra thì hàng đã nằm sai chỗ.
        /// </summary>
        private static string? NormalizeHandling(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var normalized = value.Trim().ToUpperInvariant();
            if (normalized != DirectDelivery && normalized != StoreAtVn)
            {
                throw new ArgumentException(
                    $"{fieldName} không hợp lệ: '{value}'. Chỉ nhận {DirectDelivery} (giao thẳng cho khách) " +
                    $"hoặc {StoreAtVn} (khách gửi lại kho VN).");
            }

            return normalized;
        }

        /// <summary>Nhãn tiếng Việt của tình trạng kiện lúc nhận.</summary>
        private static string? DescribeCondition(string? condition) =>
            condition?.Trim().ToUpperInvariant() switch
            {
                "INTACT" => "Nguyên vẹn",
                "MISSING" => "Thiếu hàng",
                "DAMAGED" => "Hư hỏng",
                "WET" => "Ẩm ướt",
                "SEAL_BROKEN" => "Rách niêm phong",
                _ => null,
            };

        /// <summary>Tình trạng khác nguyên vẹn thì tự nó đã là chênh lệch, khỏi cần so số.</summary>
        private static bool IsProblemCondition(string? condition)
        {
            var normalized = condition?.Trim().ToUpperInvariant();
            return !string.IsNullOrEmpty(normalized) && normalized != "INTACT";
        }

        /// <summary>
        /// Kho có thật sự đối chiếu kiện này không. Bấm xác nhận mà không nhập gì thì bỏ qua,
        /// đừng đẻ biên bản rỗng làm loãng danh sách chênh lệch của OM.
        /// </summary>
        private static bool HasInspectionInput(DestinationReceivingItemDto item) =>
            item.ActualQuantity.HasValue
            || item.ActualWeight.HasValue
            || !string.IsNullOrWhiteSpace(item.Condition)
            || !string.IsNullOrWhiteSpace(item.InspectionNote);

        /// <summary>
        /// Ghi biên bản đối chiếu một kiện.
        /// <para>
        /// Khai báo lấy từ chính kiện: số lượng luôn là 1 (mỗi Parcel là một kiện vật lý), cân
        /// nặng lấy trọng lượng thực đã cân ở kho nguồn. Lệch một trong hai, hoặc tình trạng
        /// khác nguyên vẹn, thì đánh dấu để OM lọc ra xử lý với hãng vận chuyển.
        /// </para>
        /// </summary>
        private async Task SaveParcelInspectionAsync(
            Parcel parcel, Guid shipmentId, DestinationReceivingItemDto input, Guid userId, DateTime now)
        {
            const int declaredQuantity = 1;
            var declaredWeight = parcel.ActualWeight ?? parcel.ChargeableWeight;

            var quantityMismatch = input.ActualQuantity.HasValue
                && input.ActualQuantity.Value != declaredQuantity;

            // Cân điện tử luôn lệch vài chục gram, chấp nhận sai số 0.05kg để khỏi báo động giả.
            var weightMismatch = input.ActualWeight.HasValue
                && declaredWeight.HasValue
                && Math.Abs(input.ActualWeight.Value - declaredWeight.Value) > 0.05m;

            var condition = string.IsNullOrWhiteSpace(input.Condition)
                ? null
                : input.Condition.Trim().ToUpperInvariant();

            // Ghi đè bản ghi cũ của cùng kiện. Kho thường đã lưu đối chiếu từng kiện trước rồi
            // mới bấm xác nhận cả lô — chèn thêm ở đây là danh sách của OM có hai dòng cho một
            // kiện, không biết tin dòng nào.
            var repo = _unitOfWork.Repository<ParcelInspection>();
            var existing = (await repo.GetAllAsync())
                .Where(i => i.ParcelId == parcel.Id)
                .OrderByDescending(i => i.InspectedAt)
                .FirstOrDefault();

            var entity = existing ?? new ParcelInspection
            {
                Id = Guid.NewGuid(),
                ParcelId = parcel.Id,
            };

            entity.ShipmentId = shipmentId;
            entity.OrderId = parcel.ConsignmentId;
            entity.DeclaredQuantity = declaredQuantity;
            entity.ActualQuantity = input.ActualQuantity;
            entity.DeclaredWeight = declaredWeight;
            entity.ActualWeight = input.ActualWeight;
            entity.Condition = condition;
            entity.Note = string.IsNullOrWhiteSpace(input.InspectionNote) ? null : input.InspectionNote.Trim();
            entity.HasDiscrepancy = quantityMismatch || weightMismatch || IsProblemCondition(condition);
            entity.InspectedBy = userId;
            entity.InspectedAt = now;

            if (existing == null)
            {
                await repo.AddAsync(entity);
            }
            else
            {
                repo.Update(entity);
            }
        }

        // Chỉ vai điều hành và quản trị mới xem được: biên bản có tên, số điện thoại khách.
        private static readonly string[] InspectionViewRoles =
            { "OPERATIONSMANAGER", "OPERATIONS", "ADMIN", "STAFF", "WAREHOUSESTAFF" };

        // Kho la nguoi cam kien tren tay nen duoc luu ket qua doi chieu; OM/Admin luu thay khi ho tro.
        private static readonly string[] InspectionSaveRoles =
            { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        public async Task<ParcelInspectionDto> SaveParcelInspectionAsync(
            Guid parcelId, SaveParcelInspectionRequest request, Guid userId, string userRole)
        {
            var normalizedRole = userRole?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedRole) || !InspectionSaveRoles.Contains(normalizedRole))
            {
                throw new UnauthorizedAccessException("Vai trò hiện tại không được lưu kết quả đối chiếu kiện.");
            }

            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(parcelId)
                ?? throw new NotFoundException($"Không tìm thấy kiện hàng với ID {parcelId}");

            // Lô mà kiện đang đi theo — để OM lọc chênh lệch theo từng chuyến.
            var shipmentParcel = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .FirstOrDefault(sp => sp.ParcelId == parcelId);

            var condition = string.IsNullOrWhiteSpace(request?.Condition)
                ? null
                : request.Condition.Trim().ToUpperInvariant();

            const int declaredQuantity = 1;
            var declaredWeight = parcel.ActualWeight ?? parcel.ChargeableWeight;

            var quantityMismatch = request?.ActualQuantity != null
                && request.ActualQuantity.Value != declaredQuantity;

            // Cân điện tử luôn lệch vài chục gram; dưới ngưỡng này coi như khớp.
            var weightMismatch = request?.ActualWeight != null
                && declaredWeight.HasValue
                && Math.Abs(request.ActualWeight.Value - declaredWeight.Value) > 0.05m;

            var repo = _unitOfWork.Repository<ParcelInspection>();
            var now = DateTime.UtcNow;

            // Lưu đè bản ghi cũ của cùng kiện. Sửa số nhập nhầm là chuyện thường; để lại hai
            // dòng chỉ làm danh sách chênh lệch của OM khó đọc.
            var existing = (await repo.GetAllAsync())
                .Where(i => i.ParcelId == parcelId)
                .OrderByDescending(i => i.InspectedAt)
                .FirstOrDefault();

            var entity = existing ?? new ParcelInspection
            {
                Id = Guid.NewGuid(),
                ParcelId = parcelId,
            };

            entity.ShipmentId = shipmentParcel?.ShipmentId ?? entity.ShipmentId;
            entity.OrderId = parcel.ConsignmentId;
            entity.DeclaredQuantity = declaredQuantity;
            entity.ActualQuantity = request?.ActualQuantity;
            entity.DeclaredWeight = declaredWeight;
            entity.ActualWeight = request?.ActualWeight;
            entity.Condition = condition;
            entity.Note = string.IsNullOrWhiteSpace(request?.Note) ? null : request.Note.Trim();
            entity.HasDiscrepancy = quantityMismatch || weightMismatch || IsProblemCondition(condition);
            entity.InspectedBy = userId;
            entity.InspectedAt = now;

            if (existing == null)
            {
                await repo.AddAsync(entity);
            }
            else
            {
                repo.Update(entity);
            }

            await _unitOfWork.SaveChangesAsync();

            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            var order = parcel.ConsignmentId.HasValue
                ? await _unitOfWork.Repository<Order>().GetByIdAsync(parcel.ConsignmentId.Value)
                : null;

            var quantityDiff = entity.ActualQuantity.HasValue
                ? entity.ActualQuantity.Value - declaredQuantity
                : (int?)null;
            var weightDiff = entity.ActualWeight.HasValue && declaredWeight.HasValue
                ? entity.ActualWeight.Value - declaredWeight.Value
                : (decimal?)null;

            return new ParcelInspectionDto
            {
                InspectionId = entity.Id,
                ParcelId = parcelId,
                PackageCode = parcel.PackageCode ?? string.Empty,
                ShipmentId = entity.ShipmentId,
                OrderId = entity.OrderId,
                OrderCode = order?.ConsignmentCode,
                DeclaredQuantity = declaredQuantity,
                ActualQuantity = entity.ActualQuantity,
                DeclaredWeight = declaredWeight,
                ActualWeight = entity.ActualWeight,
                QuantityDifference = quantityDiff,
                WeightDifference = weightDiff,
                Condition = entity.Condition,
                ConditionText = DescribeCondition(entity.Condition),
                Note = entity.Note,
                HasDiscrepancy = entity.HasDiscrepancy,
                DiscrepancySummary = BuildDiscrepancySummary(quantityDiff, weightDiff, entity.Condition),
                InspectedByName = user?.FullName,
                InspectedAt = entity.InspectedAt,
            };
        }

        public async Task<ParcelInspectionListDto> GetParcelInspectionsAsync(
            bool onlyDiscrepancy, Guid? shipmentId, string userRole)
        {
            var normalizedRole = userRole?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedRole) || !InspectionViewRoles.Contains(normalizedRole))
            {
                throw new UnauthorizedAccessException("Vai trò hiện tại không được xem biên bản kiểm đếm.");
            }

            var all = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync()).ToList();

            var scoped = shipmentId.HasValue
                ? all.Where(i => i.ShipmentId == shipmentId.Value).ToList()
                : all;

            var filtered = (onlyDiscrepancy ? scoped.Where(i => i.HasDiscrepancy) : scoped)
                .OrderByDescending(i => i.InspectedAt)
                .ToList();

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id);
            var shipments = (await _unitOfWork.Repository<Shipment>().GetAllAsync()).ToDictionary(sh => sh.Id);
            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);
            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id);

            var items = new List<ParcelInspectionDto>();
            foreach (var ins in filtered)
            {
                parcels.TryGetValue(ins.ParcelId, out var parcel);

                Shipment? shipment = null;
                if (ins.ShipmentId.HasValue) shipments.TryGetValue(ins.ShipmentId.Value, out shipment);

                Order? order = null;
                if (ins.OrderId.HasValue) orders.TryGetValue(ins.OrderId.Value, out order);

                Customer? customer = null;
                if (order?.CustomerId is Guid cid) customers.TryGetValue(cid, out customer);

                string? warehouseName = null;
                if (shipment?.DestinationWarehouseId is Guid wid && warehouses.TryGetValue(wid, out var wh))
                {
                    warehouseName = wh.WarehouseName;
                }

                var quantityDiff = ins.ActualQuantity.HasValue && ins.DeclaredQuantity.HasValue
                    ? ins.ActualQuantity.Value - ins.DeclaredQuantity.Value
                    : (int?)null;
                var weightDiff = ins.ActualWeight.HasValue && ins.DeclaredWeight.HasValue
                    ? ins.ActualWeight.Value - ins.DeclaredWeight.Value
                    : (decimal?)null;

                items.Add(new ParcelInspectionDto
                {
                    InspectionId = ins.Id,
                    ParcelId = ins.ParcelId,
                    PackageCode = parcel?.PackageCode ?? string.Empty,
                    ShipmentId = ins.ShipmentId,
                    ShipmentCode = shipment?.ShipmentCode,
                    OrderId = ins.OrderId,
                    OrderCode = order?.ConsignmentCode,
                    CustomerName = customer?.FullName,
                    CustomerPhone = customer?.Phone,
                    DeclaredQuantity = ins.DeclaredQuantity,
                    ActualQuantity = ins.ActualQuantity,
                    DeclaredWeight = ins.DeclaredWeight,
                    ActualWeight = ins.ActualWeight,
                    QuantityDifference = quantityDiff,
                    WeightDifference = weightDiff,
                    Condition = ins.Condition,
                    ConditionText = DescribeCondition(ins.Condition),
                    Note = ins.Note,
                    HasDiscrepancy = ins.HasDiscrepancy,
                    DiscrepancySummary = BuildDiscrepancySummary(quantityDiff, weightDiff, ins.Condition),
                    InspectedByName = ins.InspectedBy.HasValue && users.TryGetValue(ins.InspectedBy.Value, out var u)
                        ? u.FullName
                        : null,
                    InspectedAt = ins.InspectedAt,
                    WarehouseName = warehouseName,
                });
            }

            var weekAgo = DateTime.UtcNow.AddDays(-7);
            return new ParcelInspectionListDto
            {
                // Số liệu tính trên TOÀN BỘ biên bản chứ không theo bộ lọc đang xem, để OM lọc
                // hẹp lại mà mấy ô đếm không tụt về 0 rồi tưởng hết việc.
                Summary = new ParcelInspectionSummaryDto
                {
                    Total = all.Count,
                    WithDiscrepancy = all.Count(i => i.HasDiscrepancy),
                    RecentDiscrepancy = all.Count(i =>
                        i.HasDiscrepancy && i.InspectedAt.HasValue && i.InspectedAt.Value >= weekAgo),
                    DamagedParcels = all.Count(i => IsProblemCondition(i.Condition)),
                },
                Items = items,
            };
        }

        /// <summary>Gộp chênh lệch thành một câu tiếng Việt để OM đọc lướt danh sách.</summary>
        private static string BuildDiscrepancySummary(int? quantityDiff, decimal? weightDiff, string? condition)
        {
            var parts = new List<string>();

            if (quantityDiff.HasValue && quantityDiff.Value != 0)
            {
                parts.Add(quantityDiff.Value < 0
                    ? $"Thiếu {Math.Abs(quantityDiff.Value)} kiện"
                    : $"Thừa {quantityDiff.Value} kiện");
            }

            if (weightDiff.HasValue && Math.Abs(weightDiff.Value) > 0.05m)
            {
                parts.Add(weightDiff.Value < 0
                    ? $"nhẹ hơn {Math.Abs(weightDiff.Value):0.###} kg"
                    : $"nặng hơn {weightDiff.Value:0.###} kg");
            }

            var conditionText = DescribeCondition(condition);
            if (IsProblemCondition(condition) && conditionText != null)
            {
                parts.Add(conditionText.ToLowerInvariant());
            }

            return parts.Count == 0 ? "Khớp khai báo" : string.Join(" · ", parts);
        }

        public InternationalShipmentService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        /// <summary>
        /// Bảng tra cứu dùng chung khi dựng DTO lô hàng.
        /// </summary>
        /// <remarks>
        /// Repository chỉ có <c>GetAllAsync()</c>, không nhận điều kiện lọc — nên mỗi lần đụng vào
        /// một bảng là kéo trọn bảng đó về bộ nhớ. Bản cũ gọi <c>GetAllAsync()</c> cho 11 bảng ngay
        /// bên trong hàm dựng DTO, mà hàm đó lại chạy trong vòng lặp từng lô: một trang 50 lô là
        /// khoảng 550 lần quét toàn bảng, đo được 22 giây cho 50KB dữ liệu trả về. Nạp một lần rồi
        /// truyền vào đưa nó về đúng 11 lần cho cả trang, và không còn phình theo số lô nữa.
        /// </remarks>
        private sealed class ShipmentLookups
        {
            public Dictionary<Guid, List<ShipmentWroRequest>> WroLinksByShipment { get; init; } = new();
            public Dictionary<Guid, WroRequest> WroRequestsById { get; init; } = new();
            public Dictionary<Guid, List<WroItem>> WroItemsByWroRequest { get; init; } = new();
            public Dictionary<Guid, Warehouse> WarehousesById { get; init; } = new();
            public Dictionary<Guid, Carrier> CarriersById { get; init; } = new();
            public Dictionary<Guid, ShippingRoute> RoutesById { get; init; } = new();
            public Dictionary<Guid, Inventory> InventoriesById { get; init; } = new();
            public Dictionary<Guid, Parcel> ParcelsById { get; init; } = new();
            public Dictionary<Guid, Order> OrdersById { get; init; } = new();
            public Dictionary<Guid, Customer> CustomersById { get; init; } = new();
        }

        private async Task<ShipmentLookups> LoadShipmentLookupsAsync()
        {
            return new ShipmentLookups
            {
                WroLinksByShipment = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                    .GroupBy(sw => sw.ShipmentId)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                WroRequestsById = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                    .ToDictionary(w => w.Id, w => w),
                WroItemsByWroRequest = (await _unitOfWork.Repository<WroItem>().GetAllAsync())
                    .GroupBy(i => i.WroRequestId)
                    .ToDictionary(g => g.Key, g => g.ToList()),
                WarehousesById = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                    .ToDictionary(w => w.Id, w => w),
                CarriersById = (await _unitOfWork.Repository<Carrier>().GetAllAsync())
                    .ToDictionary(c => c.Id, c => c),
                RoutesById = (await _unitOfWork.Repository<ShippingRoute>().GetAllAsync())
                    .ToDictionary(r => r.Id, r => r),
                InventoriesById = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                    .ToDictionary(i => i.Id, i => i),
                ParcelsById = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                    .ToDictionary(p => p.Id, p => p),
                OrdersById = (await _unitOfWork.Repository<Order>().GetAllAsync())
                    .ToDictionary(o => o.Id, o => o),
                CustomersById = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .ToDictionary(c => c.Id, c => c),
            };
        }

        /// <summary>
        /// Dựng DTO đầy đủ cho một lô. Dùng chung cho cả danh sách lẫn chi tiết để hai
        /// đầu không lệch nhau — FE render cùng một component cho cả hai màn.
        /// </summary>
        private InternationalShipmentResponseDto BuildShipmentDto(Shipment shipment, ShipmentLookups db)
        {
            var wroLinks = db.WroLinksByShipment.TryGetValue(shipment.Id, out var links)
                ? links
                : new List<ShipmentWroRequest>();
            var wroIds = wroLinks.Select(sw => sw.WroRequestId).ToHashSet();

            var wroRequests = wroIds
                .Select(id => db.WroRequestsById.TryGetValue(id, out var w) ? w : null)
                .Where(w => w != null)
                .Select(w => w!)
                .ToList();

            Warehouse? origin = shipment.OriginWarehouseId.HasValue
                && db.WarehousesById.TryGetValue(shipment.OriginWarehouseId.Value, out var foundOrigin)
                ? foundOrigin
                : null;
            Warehouse? destination = shipment.DestinationWarehouseId.HasValue
                && db.WarehousesById.TryGetValue(shipment.DestinationWarehouseId.Value, out var foundDest)
                ? foundDest
                : null;

            Carrier? carrier = shipment.CarrierId.HasValue
                && db.CarriersById.TryGetValue(shipment.CarrierId.Value, out var foundCarrier)
                ? foundCarrier
                : null;

            // Tuyến không lưu trên Shipment; mọi WRO trong lô đã được validate là cùng tuyến
            // nên lấy của phiếu đầu tiên là đại diện cho cả lô.
            var routeId = wroRequests.Select(w => w.ShippingRouteId).FirstOrDefault(r => r.HasValue);
            ShippingRoute? route = routeId.HasValue
                && db.RoutesById.TryGetValue(routeId.Value, out var foundRoute)
                ? foundRoute
                : null;

            // Kiện trong lô: qua WroItem → Inventory → Parcel
            var wroItems = wroIds
                .SelectMany(id => db.WroItemsByWroRequest.TryGetValue(id, out var list) ? list : new List<WroItem>())
                .ToList();
            var inventories = db.InventoriesById;
            var allParcels = db.ParcelsById;

            // Đơn và khách của từng kiện: màn xác nhận lô về kho đích phải cho sale xem trước
            // ai sẽ bị ảnh hưởng, vì một lần bấm là mọi đơn trong lô cùng đổi trạng thái.
            var ordersById = db.OrdersById;
            var customersById = db.CustomersById;

            var parcels = new List<InternationalShipmentParcelDto>();
            var orderIds = new HashSet<Guid>();

            foreach (var item in wroItems)
            {
                if (!inventories.TryGetValue(item.InventoryId, out var inv)) continue;

                orderIds.Add(inv.OrderId);

                if (allParcels.TryGetValue(inv.ParcelId, out var parcel))
                {
                    ordersById.TryGetValue(inv.OrderId, out var order);

                    // Khách lấy theo đơn trước, vì đó mới là chủ đơn thật sự. Tồn kho cũng có
                    // cột khách nên dùng làm đường lui khi đơn chưa gắn khách.
                    var customerId = order?.CustomerId ?? inv.CustomerId;
                    customersById.TryGetValue(customerId, out var customer);

                    parcels.Add(new InternationalShipmentParcelDto
                    {
                        ParcelId = parcel.Id,
                        PackageCode = parcel.PackageCode ?? string.Empty,
                        Weight = parcel.ActualWeight ?? parcel.ChargeableWeight,
                        OrderId = order?.Id ?? inv.OrderId,
                        OrderCode = order?.ConsignmentCode,
                        CustomerId = customerId == Guid.Empty ? null : customerId,
                        CustomerName = customer?.FullName,
                        PackageStatus = parcel.PackageStatus,
                        DestinationHandling = parcel.DestinationHandling,
                        CustomerIntent = order?.DefaultDestinationHandling,
                        CustomerIntentText = DescribeHandling(order?.DefaultDestinationHandling)
                    });
                }
            }

            // Hồ sơ PDF: phiếu nhập kho của các đơn liên quan. Phiếu xuất kho từng WRO
            // sẽ được nối vào đây khi ReleasePdfUrl có mặt.
            var pdfUrls = orderIds
                .Select(id => ordersById.TryGetValue(id, out var o) ? o : null)
                .Where(o => o != null && !string.IsNullOrWhiteSpace(o.ReceiptPdfUrl))
                .Select(o => o!.ReceiptPdfUrl!)
                .Distinct()
                .ToList();

            // Tờ khai của chính lô là nguồn đúng — mỗi lần bàn giao một tờ. Lô cũ chưa có thì
            // vẫn gom từ các phiếu xuất kho để dữ liệu trước đây không biến mất khỏi màn hình.
            var customsDocs = !string.IsNullOrWhiteSpace(shipment.CustomsDocumentUrls)
                ? shipment.CustomsDocumentUrls.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
                : wroRequests
                    .Where(w => !string.IsNullOrWhiteSpace(w.CustomsDocumentUrls))
                    .SelectMany(w => w.CustomsDocumentUrls.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    .Distinct()
                    .ToList();

            return new InternationalShipmentResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                OriginWarehouseId = shipment.OriginWarehouseId,
                OriginWarehouseName = origin?.WarehouseName ?? string.Empty,
                DestinationWarehouseId = shipment.DestinationWarehouseId,
                DestinationWarehouseName = destination?.WarehouseName ?? string.Empty,
                CarrierId = shipment.CarrierId,
                CarrierName = carrier?.CarrierName,
                ShippingMethod = shipment.ShippingMethod,
                ShippingRouteId = routeId,
                ShippingRouteName = route?.RouteName ?? route?.RouteCode,
                CarrierTrackingCode = shipment.CarrierTrackingCode,
                TotalPackages = shipment.TotalPackages ?? parcels.Count,
                TotalWeight = shipment.TotalWeight ?? parcels.Sum(p => p.Weight ?? 0m),
                Status = shipment.Status ?? string.Empty,
                StatusTab = MapStatusToTab(shipment.Status),
                ShippedAt = shipment.ShippedAt,
                DeliveredAt = shipment.DeliveredAt,
                PdfUrls = pdfUrls,
                CustomsDocUrls = customsDocs,
                WroRequests = wroRequests
                    .Select(w => new InternationalShipmentWroDto { WroRequestId = w.Id, WroCode = w.WroCode ?? string.Empty })
                    .ToList(),
                Parcels = parcels
            };
        }

        public async Task<PaginatedResponse<InternationalShipmentResponseDto>> GetListAsync(
            int pageNumber, int pageSize, string? statusTab, string? status, Guid? originWarehouseId, string? search)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .Where(s => string.Equals(s.ShipmentStage, "INTERNATIONAL", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(s => string.Equals(s.Status, status.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(statusTab))
            {
                var tab = statusTab.Trim();
                query = query.Where(s => string.Equals(MapStatusToTab(s.Status), tab, StringComparison.OrdinalIgnoreCase));
            }

            if (originWarehouseId.HasValue)
            {
                query = query.Where(s => s.OriginWarehouseId == originWarehouseId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(s => (s.ShipmentCode ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query.OrderByDescending(s => s.ShippedAt ?? DateTime.MinValue).ToList();
            var totalCount = ordered.Count;

            var page = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

            // Nạp bảng tra cứu MỘT lần cho cả trang, không nạp lại theo từng lô.
            var lookups = await LoadShipmentLookupsAsync();

            var items = page.Select(shipment => BuildShipmentDto(shipment, lookups)).ToList();

            return new PaginatedResponse<InternationalShipmentResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<InternationalShipmentResponseDto> GetByIdAsync(Guid shipmentId)
        {
            var shipment = await _unitOfWork.Repository<Shipment>().GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            return BuildShipmentDto(shipment, await LoadShipmentLookupsAsync());
        }

        public async Task<InternationalShipmentResponseDto> CreateAsync(Guid userId, string userRole, CreateInternationalShipmentDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu lô vận chuyển không được để trống.");

            // 1. Phân quyền
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && ConsolidationRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException(
                    "Chỉ nhân viên Kho Quốc tế (hoặc Staff, Operations) được gom kiện thành lô vận chuyển quốc tế.");
            }

            // 2. Xác định danh sách KIỆN vào lô.
            //
            // Lô là một container thật, có sức chứa. Kiện của cùng một phiếu xuất kho hoàn toàn
            // có thể tách sang hai chuyến khác nhau, nên đơn vị gom lô đúng là KIỆN chứ không
            // phải phiếu. Truyền parcelIds là chọn đích danh; truyền wroRequestIds thì hiểu là
            // lấy trọn kiện của mấy phiếu đó (giữ cho màn hình cũ không gãy).
            var wroRepo = _unitOfWork.Repository<WroRequest>();
            var shipmentWroRepo = _unitOfWork.Repository<ShipmentWroRequest>();
            var shipmentParcelRepoForCheck = _unitOfWork.Repository<ShipmentParcel>();
            var allShipmentParcels = await shipmentParcelRepoForCheck.GetAllAsync();

            var allWroItems = await _unitOfWork.Repository<WroItem>().GetAllAsync();
            var allInventoriesForPick = await _unitOfWork.Repository<Inventory>().GetAllAsync();
            var allParcelsForPick = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id);

            // Kiện ↔ phiếu xuất kho: Parcel ← Inventory ← WroItem → WroRequest
            var inventoryByParcel = allInventoriesForPick
                .Where(i => i.ParcelId != Guid.Empty)
                .GroupBy(i => i.ParcelId)
                .ToDictionary(g => g.Key, g => g.Select(i => i.Id).ToHashSet());

            // Một dòng tồn kho có thể nằm trong NHIỀU phiếu xuất kho — phiếu cũ bị từ chối rồi
            // lập lại phiếu mới chẳng hạn. Nên gom thành danh sách, lát nữa chọn phiếu nào đang
            // ở trạng thái đã xuất kho xong.
            var wroIdsByInventory = allWroItems
                .GroupBy(i => i.InventoryId)
                .ToDictionary(g => g.Key, g => g.Select(i => i.WroRequestId).Distinct().ToList());

            var pickedParcelIds = new List<Guid>();

            if (request.ParcelIds != null && request.ParcelIds.Count > 0)
            {
                pickedParcelIds = request.ParcelIds.Distinct().ToList();
            }
            else if (request.WroRequestIds != null && request.WroRequestIds.Count > 0)
            {
                var wantedWroIds = request.WroRequestIds.Distinct().ToHashSet();
                pickedParcelIds = allWroItems
                    .Where(i => wantedWroIds.Contains(i.WroRequestId))
                    .Select(i => allInventoriesForPick.FirstOrDefault(inv => inv.Id == i.InventoryId))
                    .Where(inv => inv != null && inv.ParcelId != Guid.Empty)
                    .Select(inv => inv!.ParcelId)
                    .Distinct()
                    .ToList();

                if (pickedParcelIds.Count == 0)
                {
                    throw new ArgumentException("Các phiếu xuất kho đã chọn không có kiện hàng nào để gom lô.");
                }
            }
            else
            {
                throw new ArgumentException("Phải chọn ít nhất một kiện hàng (parcelIds) hoặc một yêu cầu xuất kho (wroRequestIds).");
            }

            // 2a. Từng kiện: có thật, chưa thuộc lô nào, và truy được về phiếu xuất kho.
            var wroIdsOfPickedParcels = new HashSet<Guid>();
            foreach (var parcelId in pickedParcelIds)
            {
                if (!allParcelsForPick.TryGetValue(parcelId, out var parcel))
                {
                    throw new NotFoundException($"Không tìm thấy kiện hàng với ID {parcelId}");
                }

                // Chốt nay đặt trên KIỆN chứ không phải phiếu. Đặt trên phiếu thì phiếu nào đã
                // tách một phần sang lô trước là phần còn lại kẹt luôn, không đi chuyến sau được.
                if (allShipmentParcels.Any(sp => sp.ParcelId == parcelId))
                {
                    throw new ArgumentException(
                        $"Kiện {parcel.PackageCode ?? parcelId.ToString()} đã thuộc một lô vận chuyển khác.");
                }

                if (!inventoryByParcel.TryGetValue(parcelId, out var inventoryIds))
                {
                    throw new ArgumentException(
                        $"Kiện {parcel.PackageCode ?? parcelId.ToString()} không có bản ghi tồn kho nên không truy được về phiếu xuất kho.");
                }

                var candidateWroIds = inventoryIds
                    .Where(invId => wroIdsByInventory.ContainsKey(invId))
                    .SelectMany(invId => wroIdsByInventory[invId])
                    .Distinct()
                    .ToList();

                if (candidateWroIds.Count == 0)
                {
                    throw new ArgumentException(
                        $"Kiện {parcel.PackageCode ?? parcelId.ToString()} chưa nằm trong phiếu xuất kho nào, chưa gom lô được.");
                }

                // Kiện có thể dính nhiều phiếu (phiếu cũ bị từ chối, lập lại phiếu mới). Lấy
                // phiếu đang ở trạng thái đã xuất kho xong; không có cái nào thì báo lỗi kèm
                // trạng thái thật để người dùng biết đường xử lý.
                Guid chosenWroId = Guid.Empty;
                var seenStatuses = new List<string>();
                foreach (var candidate in candidateWroIds)
                {
                    var candidateWro = await wroRepo.GetByIdAsync(candidate);
                    if (candidateWro == null) continue;

                    var st = candidateWro.Status?.Trim().ToUpperInvariant() ?? string.Empty;
                    seenStatuses.Add($"{candidateWro.WroCode}: {candidateWro.Status}");

                    if (st is "RELEASED" or "HANDED_OVER" or "IN_TRANSIT")
                    {
                        chosenWroId = candidate;
                        break;
                    }
                }

                if (chosenWroId == Guid.Empty)
                {
                    throw new ArgumentException(
                        $"Kiện {parcel.PackageCode ?? parcelId.ToString()} chưa có phiếu xuất kho nào đã xuất xong " +
                        $"({string.Join(" · ", seenStatuses)}).");
                }

                wroIdsOfPickedParcels.Add(chosenWroId);
            }

            // 2b. Các phiếu đứng sau những kiện đã chọn phải đã xuất kho xong.
            var wroRequestIds = wroIdsOfPickedParcels.ToList();
            var wroRequests = new List<WroRequest>();
            foreach (var wroId in wroRequestIds)
            {
                var wro = await wroRepo.GetByIdAsync(wroId);
                if (wro == null)
                {
                    throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {wroId}");
                }

                // Nhận mọi phiếu đã rời kho nguồn. Ba trạng thái vì ba đường đi khác nhau đều
                // hợp lệ, thiếu một cái là hàng đứng vĩnh viễn ở kho nguồn:
                //   RELEASED    - hoàn tất xuất kho (đường cũ)
                //   HANDED_OVER - đã bàn giao cho nhà xe
                //   IN_TRANSIT  - hoàn tất xuất kho nay đặt thẳng IN_TRANSIT, và gán tuyến vận
                //                 chuyển cũng đặt IN_TRANSIT. Nên OM gán tuyến SAU khi bàn giao
                //                 là phiếu rơi khỏi danh sách này, không gom lô được nữa — lỗi
                //                 phụ thuộc thứ tự thao tác, người dùng không thể tự đoán ra.
                var eligible = new[] { "RELEASED", "HANDED_OVER", "IN_TRANSIT" };
                if (!eligible.Contains(wro.Status?.Trim().ToUpperInvariant()))
                {
                    throw new ArgumentException(
                        $"Yêu cầu xuất kho {wro.WroCode ?? wroId.ToString()} chưa đủ điều kiện " +
                        $"(phải đã xuất kho xong — RELEASED, HANDED_OVER hoặc IN_TRANSIT; hiện tại: {wro.Status}).");
                }

                wroRequests.Add(wro);
            }

            // 2b. Mọi WRO trong lô phải cùng kho xuất và cùng tuyến, nếu không thì lô này
            // không thể đi chung một chuyến. Kho xuất suy từ vị trí thật của hàng
            // (WroItem → Inventory → Bin → Warehouse) chứ không tin số client khai.
            var routeIds = wroRequests
                .Select(w => w.ShippingRouteId)
                .Distinct()
                .ToList();

            if (routeIds.Count > 1)
            {
                throw new ArgumentException(
                    "Tất cả phiếu WRO trong Lô phải cùng Kho xuất và cùng Tuyến vận chuyển! " +
                    $"Đang có {routeIds.Count} tuyến khác nhau trong danh sách đã chọn.");
            }

            // Kho xuất suy từ vị trí thật của CHÍNH những kiện đã chọn (Parcel → Inventory →
            // Bin → Warehouse), không tính cả phiếu nữa — vì phiếu có thể còn kiện nằm ở lô khác.
            var binsForCheck = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .ToDictionary(b => b.Id, b => b);

            var inventoriesById = allInventoriesForPick.ToDictionary(i => i.Id, i => i);

            var sourceWarehouseIds = new HashSet<Guid>();
            foreach (var parcelId in pickedParcelIds)
            {
                if (!inventoryByParcel.TryGetValue(parcelId, out var inventoryIds)) continue;

                foreach (var invId in inventoryIds)
                {
                    if (inventoriesById.TryGetValue(invId, out var inv) &&
                        binsForCheck.TryGetValue(inv.BinId, out var bin) &&
                        bin.WarehouseId.HasValue)
                    {
                        sourceWarehouseIds.Add(bin.WarehouseId.Value);
                    }
                }
            }

            if (sourceWarehouseIds.Count > 1)
            {
                throw new ArgumentException(
                    "Tất cả kiện hàng trong Lô phải cùng Kho xuất và cùng Tuyến vận chuyển! " +
                    $"Đang có {sourceWarehouseIds.Count} kho xuất khác nhau trong danh sách đã chọn.");
            }

            if (sourceWarehouseIds.Count == 1 && !sourceWarehouseIds.Contains(request.OriginWarehouseId))
            {
                throw new ArgumentException(
                    "Kho đi khai báo không khớp với kho đang chứa những kiện đã chọn.");
            }

            // 3. Validate kho đi/kho đến
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var originWarehouse = await warehouseRepo.GetByIdAsync(request.OriginWarehouseId);
            if (originWarehouse == null)
                throw new NotFoundException($"Không tìm thấy kho đi với ID {request.OriginWarehouseId}");
            var destinationWarehouse = await warehouseRepo.GetByIdAsync(request.DestinationWarehouseId);
            if (destinationWarehouse == null)
                throw new NotFoundException($"Không tìm thấy kho đến với ID {request.DestinationWarehouseId}");

            // 3b. Kho đến phải khớp tuyến vận chuyển của những kiện đã chọn.
            //
            // Tuyến đã khai sẵn đi từ kho nào tới kho nào, nhưng người gom lô vẫn nhập tay kho
            // đến — chọn nhầm thì lô báo về sai kho, và địa chỉ shipper tới lấy hàng ở chặng cuối
            // cũng lấy theo kho đích của lô, tức xe chạy nhầm tỉnh. Đối chiếu ngay lúc gom, khi
            // sửa còn rẻ.
            //
            // Tuyến chưa khai kho đích thì bỏ qua: không có gì để đối chiếu, chặn ở đây chỉ làm
            // kẹt những tuyến khai thiếu.
            var lotRouteId = routeIds.FirstOrDefault(r => r.HasValue);
            if (lotRouteId.HasValue)
            {
                var route = await _unitOfWork.Repository<ShippingRoute>().GetByIdAsync(lotRouteId.Value);

                if (route?.DestinationWarehouseId is Guid routeDestinationId
                    && routeDestinationId != request.DestinationWarehouseId)
                {
                    var routeDestination = await warehouseRepo.GetByIdAsync(routeDestinationId);
                    throw new ArgumentException(
                        $"Kho đến không khớp tuyến vận chuyển. Tuyến \"{route.RouteName}\" đi về "
                        + $"{routeDestination?.WarehouseName ?? routeDestinationId.ToString()}, "
                        + $"nhưng lô đang khai về {destinationWarehouse.WarehouseName}. "
                        + "Chọn lại kho đến, hoặc sửa tuyến nếu hàng thật sự đổi điểm nhận.");
                }
            }

            Carrier? carrier = null;
            if (request.CarrierId.HasValue)
            {
                carrier = await _unitOfWork.Repository<Carrier>().GetByIdAsync(request.CarrierId.Value);
                if (carrier == null)
                    throw new NotFoundException($"Không tìm thấy đơn vị vận chuyển với ID {request.CarrierId.Value}");
            }

            // 4. Gom danh sách Parcel từ các WRO (WroItem -> Inventory -> Parcel)
            // 4. Kiện vào lô là ĐÚNG những kiện đã chọn, không suy lại từ phiếu nữa. Suy lại từ
            // phiếu là kéo luôn cả kiện mà người dùng cố ý bỏ ra vì container không còn chỗ.
            var parcels = pickedParcelIds
                .Where(id => allParcelsForPick.ContainsKey(id))
                .Select(id => allParcelsForPick[id])
                .ToList();

            var totalPackages = parcels.Count;
            var totalWeight = parcels.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m);

            // 5. Tạo lô vận chuyển (Shipment) trạng thái MANIFESTED
            //
            // Gom các phiếu xuất kho thành một lô CHÍNH LÀ lập bản kê khai hàng hoá của lô
            // (manifest), nên lô sinh ra ở MANIFESTED chứ không phải CREATED. Để CREATED thì
            // bảng AllowedTransitions chỉ cho đi tiếp sang đúng MANIFESTED — mà màn hình của
            // sale chỉ có 4 lựa chọn CUSTOMS_EXPORT_PENDING / IN_TRANSIT / CUSTOMS_IMPORT_PENDING
            // / ISSUE, không có nút nào bấm ra MANIFESTED. Lô đứng im ngay bước đầu, không bao
            // giờ tới được IN_TRANSIT để xác nhận hàng về kho VN.
            var shipmentCode = $"INTL-{DateTime.UtcNow:yyyyMMddHHmmss}-{new Random().Next(1000, 9999)}";
            var shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                ShipmentCode = shipmentCode,
                ShipmentType = "INTERNATIONAL",
                ShipmentStage = "INTERNATIONAL",
                Status = "MANIFESTED",
                OriginWarehouseId = request.OriginWarehouseId,
                DestinationWarehouseId = request.DestinationWarehouseId,
                CarrierId = request.CarrierId,
                ShippingMethod = request.ShippingMethod,
                TotalPackages = totalPackages,
                TotalWeight = totalWeight,
                // Tờ khai thông quan thuộc về lần bàn giao, không thuộc từng phiếu xuất kho.
                CustomsDocumentUrls = request.CustomsDocumentUrls is { Count: > 0 }
                    ? string.Join(';', request.CustomsDocumentUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
                    : null
            };
            await _unitOfWork.Repository<Shipment>().AddAsync(shipment);

            // 6. Liên kết WRO và Parcel vào lô
            foreach (var wro in wroRequests)
            {
                await shipmentWroRepo.AddAsync(new ShipmentWroRequest
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    WroRequestId = wro.Id
                });
            }

            var shipmentParcelRepo = _unitOfWork.Repository<ShipmentParcel>();
            foreach (var parcel in parcels)
            {
                await shipmentParcelRepo.AddAsync(new ShipmentParcel
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    ParcelId = parcel.Id
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 7. Trả về kết quả, dùng chung đúng bộ dựng của GET chi tiết.
            //
            // Trước đây chỗ này tự dựng DTO riêng nên response lúc tạo lô nghèo hơn hẳn lúc xem
            // chi tiết: thiếu statusTab, carrierTrackingCode, tuyến vận chuyển, mốc thời gian,
            // các link chứng từ, và từng kiện cũng thiếu đơn/khách. Mà FE đã đọc sẵn những
            // trường đó, nên tạo lô xong màn hình hiện rỗng cho tới khi gọi thêm GET. Dùng chung
            // một bộ dựng thì hai đường không bao giờ lệch nhau nữa.
            return BuildShipmentDto(shipment, await LoadShipmentLookupsAsync());
        }

        /// <summary>
        /// Vòng đời lô vận chuyển quốc tế, bám theo nghiệp vụ thực tế:
        ///
        ///   CREATED → MANIFESTED → CUSTOMS_EXPORT_PENDING → IN_TRANSIT
        ///           → CUSTOMS_IMPORT_PENDING → ARRIVED_DESTINATION
        ///
        /// Hai chặng thông quan tách riêng vì mỗi chặng có thể bị treo độc lập:
        /// xuất tại nước nguồn và nhập tại Việt Nam.
        ///
        /// Giữ lại đường tắt MANIFESTED → IN_TRANSIT và IN_TRANSIT → ARRIVED_DESTINATION
        /// để client cũ chưa cập nhật vẫn chạy được.
        /// </summary>
        private static readonly Dictionary<string, string[]> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MANIFESTED"] = new[] { "CREATED" },
            ["CUSTOMS_EXPORT_PENDING"] = new[] { "MANIFESTED", "CUSTOMS_REJECTED", "HOLD" },
            ["IN_TRANSIT"] = new[] { "CUSTOMS_EXPORT_PENDING", "MANIFESTED", "HOLD" },
            ["CUSTOMS_IMPORT_PENDING"] = new[] { "IN_TRANSIT", "CUSTOMS_REJECTED", "HOLD" },
            ["ARRIVED_DESTINATION"] = new[] { "CUSTOMS_IMPORT_PENDING", "IN_TRANSIT" },

            // Trạng thái sự cố: vào được từ mọi chặng chưa kết thúc, và gỡ ra được.
            ["CUSTOMS_REJECTED"] = new[] { "CUSTOMS_EXPORT_PENDING", "CUSTOMS_IMPORT_PENDING" },
            ["HOLD"] = new[] { "CREATED", "MANIFESTED", "CUSTOMS_EXPORT_PENDING", "IN_TRANSIT", "CUSTOMS_IMPORT_PENDING", "CUSTOMS_REJECTED" },
            ["ISSUE"] = new[] { "CREATED", "MANIFESTED", "CUSTOMS_EXPORT_PENDING", "IN_TRANSIT", "CUSTOMS_IMPORT_PENDING", "CUSTOMS_REJECTED", "HOLD" }
        };

        /// <summary>
        /// Gom trạng thái về đúng 5 tab của màn Vận chuyển bên mobile, để FE lọc theo
        /// một giá trị thay vì phải tự nhớ trạng thái nào thuộc tab nào.
        /// </summary>
        public static string MapStatusToTab(string? status)
        {
            var s = status?.Trim().ToUpperInvariant() ?? string.Empty;

            return s switch
            {
                "CREATED" or "MANIFESTED" or "LOT_CREATED" or "PREPARING" => "PREPARING",
                "CUSTOMS_EXPORT_PENDING" or "CUSTOMS_IMPORT_PENDING" => "CUSTOMS",
                "IN_TRANSIT" => "IN_TRANSIT",
                "ARRIVED_DESTINATION" or "ARRIVED_IN_VN" => "ARRIVED",
                "ISSUE" or "CUSTOMS_REJECTED" or "HOLD" => "ISSUE",
                _ => "PREPARING"
            };
        }

        public async Task<UpdateInternationalShipmentStatusResponseDto> UpdateStatusAsync(
            Guid shipmentId, Guid userId, string userRole, UpdateInternationalShipmentStatusDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            // 1. Phân quyền
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && TrackingUpdateRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException("Chỉ Sale, Staff hoặc Operations được cập nhật trạng thái lô vận chuyển.");
            }

            // 2. Tìm lô vận chuyển (404 nếu không có)
            var shipmentRepo = _unitOfWork.Repository<Shipment>();
            var shipment = await shipmentRepo.GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            // 3. Validate trạng thái mới + đúng thứ tự
            var targetStatus = request.Status.Trim().ToUpperInvariant();
            if (!AllowedTransitions.ContainsKey(targetStatus))
            {
                throw new ArgumentException(
                    $"Trạng thái không hợp lệ. Hợp lệ: {string.Join(", ", AllowedTransitions.Keys)}.");
            }

            // Lô về tới kho đích phải đi qua PUT /{id}/arrive, vì bước đó mới cập nhật trạng
            // thái CỦA TỪNG KIỆN. Đặt thẳng ở đây thì lô báo "đã về" mà kiện vẫn còn ở
            // RELEASED — kho VN mở màn kiểm đếm không thấy hàng đâu, mà nhìn lô thì tưởng xong.
            if (string.Equals(targetStatus, "ARRIVED_DESTINATION", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Không đặt trực tiếp trạng thái ARRIVED_DESTINATION được. "
                    + "Dùng chức năng xác nhận lô đến kho đích để hệ thống cập nhật cả lô lẫn các kiện.");
            }

            var currentStatus = shipment.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var allowedFrom = AllowedTransitions[targetStatus];
            if (!allowedFrom.Contains(currentStatus, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Không thể chuyển sang {targetStatus} khi lô đang ở trạng thái {shipment.Status}. " +
                    $"Chỉ chuyển được từ: {string.Join(", ", allowedFrom)}.");
            }

            // 4. Cập nhật trạng thái + mốc thời gian tương ứng
            var now = DateTime.UtcNow;
            shipment.Status = targetStatus;
            if (targetStatus == "IN_TRANSIT") shipment.ShippedAt = now;
            if (targetStatus == "ARRIVED_DESTINATION") shipment.DeliveredAt = now;

            // Mã vận đơn bên đối tác: sale nhập tay, thường chỉ có từ lúc lô bắt đầu đi. Bỏ
            // trống thì giữ nguyên mã cũ — lần cập nhật trạng thái sau không được xoá mất mã
            // mà sale đã nhập, vì đó là thứ duy nhất tra được lô trên trang của đối tác.
            if (!string.IsNullOrWhiteSpace(request.CarrierTrackingCode))
            {
                shipment.CarrierTrackingCode = request.CarrierTrackingCode.Trim();
            }

            shipmentRepo.Update(shipment);

            // 5. Lưu lịch sử cập nhật (Tracking)
            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = targetStatus,
                TrackingStage = "INTERNATIONAL",
                Note = request.Note,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            // 6. Lấy tên người cập nhật
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);

            return new UpdateInternationalShipmentStatusResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                Status = shipment.Status,
                StatusTab = MapStatusToTab(shipment.Status),
                Note = request.Note,
                CarrierTrackingCode = shipment.CarrierTrackingCode,
                UpdatedAt = now,
                UpdatedByName = user?.FullName ?? string.Empty
            };
        }

        public async Task<ConfirmShipmentArrivalResponseDto> ConfirmArrivalAsync(
            Guid shipmentId, Guid userId, string userRole, ConfirmShipmentArrivalDto request)
        {
            // 1. Phân quyền
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && TrackingUpdateRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException("Chỉ Sale, Staff hoặc Operations được xác nhận lô đến kho đích.");
            }

            // 2. Tìm lô (404)
            var shipmentRepo = _unitOfWork.Repository<Shipment>();
            var shipment = await shipmentRepo.GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            // 3. Chỉ xác nhận khi đang IN_TRANSIT (đồng thời chặn xác nhận nhiều lần vì đã ARRIVED sẽ khác IN_TRANSIT)
            var currentStatus = shipment.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (currentStatus == "ARRIVED_DESTINATION")
            {
                throw new ArgumentException("Lô vận chuyển này đã được xác nhận đến kho đích trước đó.");
            }
            // Khớp đúng bảng AllowedTransitions ở trên: ARRIVED_DESTINATION vào được từ IN_TRANSIT
            // hoặc CUSTOMS_IMPORT_PENDING. Trước đây chỉ nhận IN_TRANSIT, nên lô nào đi đúng vòng
            // đời có chặng thông quan nhập là không xác nhận tới kho được — kẹt vĩnh viễn.
            var arrivableFrom = new[] { "IN_TRANSIT", "CUSTOMS_IMPORT_PENDING" };
            if (!arrivableFrom.Contains(currentStatus))
            {
                throw new ArgumentException(
                    $"Lô chưa đủ điều kiện xác nhận đến kho đích (phải đang IN_TRANSIT hoặc " +
                    $"CUSTOMS_IMPORT_PENDING, hiện tại: {shipment.Status}).");
            }

            var now = DateTime.UtcNow;

            // 4. Cập nhật lô -> ARRIVED_DESTINATION
            shipment.Status = "ARRIVED_DESTINATION";
            shipment.DeliveredAt = now;
            shipmentRepo.Update(shipment);

            // 5. Cập nhật các Parcel trong lô -> ARRIVED_DESTINATION (sẵn sàng cho nhập kho tại kho đích)
            var shipmentParcelRepo = _unitOfWork.Repository<ShipmentParcel>();
            var allShipmentParcels = await shipmentParcelRepo.GetAllAsync();
            var parcelIds = allShipmentParcels
                .Where(sp => sp.ShipmentId == shipment.Id)
                .Select(sp => sp.ParcelId)
                .ToHashSet();

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var parcels = allParcels.Where(p => parcelIds.Contains(p.Id)).ToList();
            // Kiện đã rời kho nguồn nên phải nhả chỗ trong ô kệ. Thiếu bước này thì
            // Bin.CurrentVolume chỉ tăng mà không bao giờ giảm, lâu dần mọi ô đều trông như đã đầy.
            var binRepo = _unitOfWork.Repository<Bin>();
            var allBins = await binRepo.GetAllAsync();
            var binsById = allBins.ToDictionary(b => b.Id);

            foreach (var parcel in parcels)
            {
                if (parcel.BinId.HasValue && binsById.TryGetValue(parcel.BinId.Value, out var sourceBin))
                {
                    var released = (parcel.Length ?? 0m) * (parcel.Width ?? 0m) * (parcel.Height ?? 0m);
                    var remaining = (sourceBin.CurrentVolume ?? 0m) - released;
                    sourceBin.CurrentVolume = remaining < 0m ? 0m : remaining;
                    binRepo.Update(sourceBin);
                }

                parcel.BinId = null;
                parcel.PackageStatus = "ARRIVED_DESTINATION";
                parcelRepo.Update(parcel);
            }

            // 6. Lưu lịch sử (Tracking)
            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = "ARRIVED_DESTINATION",
                TrackingStage = "INTERNATIONAL",
                Note = request?.Note,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            // 7. Thông tin kho đích + người xác nhận
            var destinationWarehouse = shipment.DestinationWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(shipment.DestinationWarehouseId.Value)
                : null;
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);

            return new ConfirmShipmentArrivalResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                DestinationWarehouseId = shipment.DestinationWarehouseId,
                DestinationWarehouseName = destinationWarehouse?.WarehouseName ?? string.Empty,
                Status = shipment.Status,
                ArrivedAt = now,
                ConfirmedByName = user?.FullName ?? string.Empty,
                Note = request?.Note,
                TotalPackages = parcels.Count,
                Parcels = parcels.Select(p => new InternationalShipmentParcelDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    Weight = p.ActualWeight ?? p.ChargeableWeight
                }).ToList()
            };
        }

        public async Task<DestinationReceivingResponseDto> ConfirmDestinationReceivingAsync(
            Guid shipmentId, Guid userId, string userRole, DestinationReceivingDto request)
        {
            // 1. Phân quyền (Warehouse Staff / Staff / Operations)
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && ReceivingRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException("Chỉ Warehouse Staff, Staff hoặc Operations được xác nhận nhận kiện tại kho đích.");
            }

            // 2. Tìm lô (404)
            var shipmentRepo = _unitOfWork.Repository<Shipment>();
            var shipment = await shipmentRepo.GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            // 3. Chỉ nhận kiện khi lô đã ARRIVED_DESTINATION
            if (!string.Equals(shipment.Status, "ARRIVED_DESTINATION", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Lô chưa đến kho đích (phải ở trạng thái ARRIVED_DESTINATION, hiện tại: {shipment.Status}).");
            }

            // 4. Lấy các Parcel thuộc lô
            var shipmentParcelRepo = _unitOfWork.Repository<ShipmentParcel>();
            var allShipmentParcels = await shipmentParcelRepo.GetAllAsync();
            var shipmentParcelIds = allShipmentParcels
                .Where(sp => sp.ShipmentId == shipment.Id)
                .Select(sp => sp.ParcelId)
                .ToHashSet();

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var shipmentParcels = allParcels.Where(p => shipmentParcelIds.Contains(p.Id)).ToList();

            // 5. Hướng xử lý mặc định cho lần nhận này. Không chọn gì thì hiểu là giao thẳng —
            // gửi lại kho là ý muốn riêng của khách, phải nói ra mới có.
            var defaultHandling = NormalizeHandling(request?.Handling, nameof(request.Handling));

            // 6. Xác định danh sách Parcel cần nhận, kèm hướng xử lý của từng kiện.
            // Ba cách gọi đều phải chạy được: items (mới, phân loại từng kiện), parcelIds (cũ,
            // mobile đang dùng), hoặc không truyền gì (nhận cả lô).
            // Giá trị null trong dictionary nghĩa là "kiện có trong danh sách nhưng kho không
            // chốt hướng riêng" — khác hẳn với "kiện không được gửi lên". Phân biệt được hai
            // trường hợp đó thì kiện không chốt riêng mới rơi đúng xuống ý khách ở bước sau.
            var handlingByParcel = new Dictionary<Guid, string?>();
            List<Guid>? requestedIds = null;

            // Kết quả đối chiếu của từng kiện, giữ lại để ghi biên bản sau khi xác định kiện hợp lệ.
            var inspectionByParcel = new Dictionary<Guid, DestinationReceivingItemDto>();

            if (request?.Items != null && request.Items.Count > 0)
            {
                requestedIds = new List<Guid>();
                foreach (var item in request.Items)
                {
                    if (handlingByParcel.ContainsKey(item.ParcelId)) continue;
                    requestedIds.Add(item.ParcelId);
                    handlingByParcel[item.ParcelId] =
                        NormalizeHandling(item.Handling, nameof(item.Handling));
                    inspectionByParcel[item.ParcelId] = item;
                }
            }
            else if (request?.ParcelIds != null && request.ParcelIds.Count > 0)
            {
                requestedIds = request.ParcelIds.Distinct().ToList();
            }

            List<Parcel> targets;
            if (requestedIds != null)
            {
                targets = new List<Parcel>();
                foreach (var pid in requestedIds)
                {
                    var parcel = shipmentParcels.FirstOrDefault(p => p.Id == pid);
                    if (parcel == null)
                    {
                        throw new ArgumentException($"Parcel {pid} không thuộc lô vận chuyển này.");
                    }
                    if (string.Equals(parcel.PackageStatus, "RECEIVED_AT_DESTINATION", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException($"Parcel {parcel.PackageCode ?? pid.ToString()} đã được xác nhận nhận tại kho đích trước đó.");
                    }
                    targets.Add(parcel);
                }
            }
            else
            {
                // Nhận tất cả kiện chưa nhận
                targets = shipmentParcels
                    .Where(p => !string.Equals(p.PackageStatus, "RECEIVED_AT_DESTINATION", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (targets.Count == 0)
                {
                    throw new ArgumentException("Tất cả kiện trong lô đã được xác nhận nhận tại kho đích.");
                }
            }

            // Ý muốn khách khai từ lúc tạo đơn. Kho không chọn gì thì rơi về đây, thay vì mặc
            // định giao thẳng — khách đã nói muốn gửi lại kho mà hệ thống vẫn đẩy đi giao là sai.
            var orderIds = targets.Where(p => p.ConsignmentId.HasValue)
                .Select(p => p.ConsignmentId!.Value)
                .ToHashSet();
            var customerIntentByOrder = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id) && !string.IsNullOrWhiteSpace(o.DefaultDestinationHandling))
                .ToDictionary(o => o.Id, o => o.DefaultDestinationHandling!.Trim().ToUpperInvariant());

            // 7. Cập nhật trạng thái Parcel + thời gian nhận + hướng xử lý
            var now = DateTime.UtcNow;
            foreach (var parcel in targets)
            {
                parcel.PackageStatus = "RECEIVED_AT_DESTINATION";
                parcel.CheckInTime = now;

                // Thứ tự ưu tiên: kho chọn riêng cho kiện → kho chọn chung cho lần nhận này →
                // ý khách khai lúc đặt đơn → cuối cùng mới là giao thẳng.
                string? customerIntent = null;
                if (parcel.ConsignmentId.HasValue)
                {
                    customerIntentByOrder.TryGetValue(parcel.ConsignmentId.Value, out customerIntent);
                }

                handlingByParcel.TryGetValue(parcel.Id, out var perParcel);
                parcel.DestinationHandling =
                    perParcel ?? defaultHandling ?? customerIntent ?? DirectDelivery;
                parcelRepo.Update(parcel);

                // Biên bản đối chiếu của kiện. Chỉ ghi khi kho thật sự nhập gì đó — kho bấm
                // xác nhận mà không đếm lại thì đừng đẻ ra bản ghi rỗng làm loãng danh sách
                // chênh lệch của OM.
                if (inspectionByParcel.TryGetValue(parcel.Id, out var inspectionInput)
                    && HasInspectionInput(inspectionInput))
                {
                    await SaveParcelInspectionAsync(parcel, shipment.Id, inspectionInput, userId, now);
                }
            }

            // 7. Lưu lịch sử nhận kiện tại kho đích
            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = "RECEIVED_AT_DESTINATION",
                TrackingStage = "DESTINATION_RECEIVING",
                Note = request?.Note,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            var destinationWarehouse = shipment.DestinationWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(shipment.DestinationWarehouseId.Value)
                : null;
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);

            // Biên bản vừa ghi trong lần này, trả kèm để app kho hiện ngay kết quả đối chiếu
            // mà không phải gọi lại API.
            var inspectionsJustSaved = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync())
                .Where(i => i.InspectedAt == now)
                .ToDictionary(i => i.ParcelId, i => i);

            return new DestinationReceivingResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                DestinationWarehouseId = shipment.DestinationWarehouseId,
                DestinationWarehouseName = destinationWarehouse?.WarehouseName ?? string.Empty,
                ReceivedAt = now,
                ReceivedByName = user?.FullName ?? string.Empty,
                Note = request?.Note,
                ReceivedCount = targets.Count,
                Parcels = targets.Select(p =>
                {
                    string? intent = null;
                    if (p.ConsignmentId.HasValue)
                    {
                        customerIntentByOrder.TryGetValue(p.ConsignmentId.Value, out intent);
                    }

                    return new DestinationReceivingParcelDto
                    {
                        ParcelId = p.Id,
                        PackageCode = p.PackageCode ?? string.Empty,
                        PackageStatus = p.PackageStatus ?? string.Empty,
                        DestinationHandling = p.DestinationHandling,
                        CustomerIntent = intent,
                        CustomerIntentText = DescribeHandling(intent),
                        Inspection = inspectionsJustSaved.TryGetValue(p.Id, out var ins)
                            ? new ParcelInspectionResultDto
                            {
                                DeclaredQuantity = ins.DeclaredQuantity,
                                ActualQuantity = ins.ActualQuantity,
                                DeclaredWeight = ins.DeclaredWeight,
                                ActualWeight = ins.ActualWeight,
                                Condition = ins.Condition,
                                ConditionText = DescribeCondition(ins.Condition),
                                Note = ins.Note,
                                HasDiscrepancy = ins.HasDiscrepancy,
                                QuantityDifference = ins.ActualQuantity.HasValue && ins.DeclaredQuantity.HasValue
                                    ? ins.ActualQuantity.Value - ins.DeclaredQuantity.Value
                                    : null,
                                WeightDifference = ins.ActualWeight.HasValue && ins.DeclaredWeight.HasValue
                                    ? ins.ActualWeight.Value - ins.DeclaredWeight.Value
                                    : null,
                                InspectedByName = user?.FullName,
                                InspectedAt = ins.InspectedAt,
                            }
                            : null,
                    };
                }).ToList()
            };
        }
    }
}
