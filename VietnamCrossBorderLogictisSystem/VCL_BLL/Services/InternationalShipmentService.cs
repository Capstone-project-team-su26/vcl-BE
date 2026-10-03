using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
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
                "INTACT" or "GOOD" => "Nguyên vẹn",
                "MISSING" => "Thiếu hàng",
                "DAMAGED" => "Hư hỏng",
                "WET" => "Ẩm ướt",
                "SEAL_BROKEN" => "Rách niêm phong",
                _ => null,
            };

        /// <summary>
        /// Tình trạng khác nguyên vẹn thì tự nó đã là chênh lệch, khỏi cần so số. App kho mới gửi
        /// <c>GOOD</c> cho kiện nguyên vẹn (bộ cũ là <c>INTACT</c>) — cả hai đều KHÔNG phải chênh lệch;
        /// trước đây GOOD bị tính là lệch nên mọi kiện tốt đều hiện trong danh sách chênh lệch của OM.
        /// </summary>
        private static bool IsProblemCondition(string? condition)
        {
            var normalized = condition?.Trim().ToUpperInvariant();
            return !string.IsNullOrEmpty(normalized) && normalized != "INTACT" && normalized != "GOOD";
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
            entity.Length = input.Length;
            entity.Width = input.Width;
            entity.Height = input.Height;
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
            if (!StaffRoleHelper.IsRoleAllowed(InspectionSaveRoles, normalizedRole))
            {
                throw new UnauthorizedAccessException("Vai trò hiện tại không được lưu kết quả đối chiếu kiện.");
            }

            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(parcelId)
                ?? throw new NotFoundException($"Không tìm thấy kiện hàng với ID {parcelId}");

            // Lô mà kiện đang đi theo — để OM lọc chênh lệch theo từng chuyến.
            var shipmentParcel = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .FirstOrDefault(sp => sp.ParcelId == parcelId);

            // Nhân viên kho đã được gán kho chỉ cân lại kiện ở đúng kho VN mình phụ trách.
            await WarehouseAccessPolicy.EnsureAssignedWarehouseAsync(
                _unitOfWork, userId, userRole, await VnArrivalPhotos.DestinationWarehouseOfParcelAsync(_unitOfWork, parcelId));

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

            // Biên bản ghi nhận LỆCH (hỏng, thiếu, lệch cân) phải có ảnh kiện làm bằng chứng — số
            // này là căn cứ đòi hãng / đền khách, không ảnh thì không ai kiểm chứng được.
            var photos = VnArrivalPhotos.Of(
                await VnArrivalPhotos.LoadAsync(_unitOfWork, AttachmentEntityTypes.Parcel, new[] { parcelId }), parcelId);
            if ((quantityMismatch || weightMismatch || IsProblemCondition(condition)) && photos.Count == 0)
            {
                throw new ArgumentException(
                    $"Kiện {parcel.PackageCode} lệch khai báo / hư hỏng: phải chụp ảnh kiện làm bằng chứng trước khi lưu "
                    + $"(documentType = {AttachmentDocumentTypes.VnArrivalProof}, entityType = {AttachmentEntityTypes.Parcel}, entityId = {parcelId}).");
            }

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
            // Cân đo lại chỉ gửi phần thay đổi: kích thước bỏ trống thì giữ số đã đo trước đó.
            entity.Length = request?.Length ?? entity.Length;
            entity.Width = request?.Width ?? entity.Width;
            entity.Height = request?.Height ?? entity.Height;
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
                Photos = photos,
            };
        }

        public async Task<ParcelInspectionListDto> GetParcelInspectionsAsync(
            bool onlyDiscrepancy, Guid? shipmentId, string userRole, Guid? userId = null)
        {
            var normalizedRole = userRole?.Trim().ToUpperInvariant();
            if (!StaffRoleHelper.IsRoleAllowed(InspectionViewRoles, normalizedRole))
            {
                throw new UnauthorizedAccessException("Vai trò hiện tại không được xem biên bản kiểm đếm.");
            }

            var all = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync()).ToList();

            // Nhân viên đã được gán kho chỉ thấy biên bản (và ảnh) của lô về đúng kho mình.
            var warehouseScope = await WarehouseAccessPolicy.GetListScopeAsync(_unitOfWork, userId, userRole);
            if (warehouseScope != null)
            {
                var scopedShipmentIds = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                    .Where(sh => sh.DestinationWarehouseId.HasValue && warehouseScope.Contains(sh.DestinationWarehouseId.Value))
                    .Select(sh => sh.Id)
                    .ToHashSet();
                all = all.Where(i => i.ShipmentId.HasValue && scopedShipmentIds.Contains(i.ShipmentId.Value)).ToList();
            }

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
            var photosByParcel = await VnArrivalPhotos.LoadAsync(
                _unitOfWork, AttachmentEntityTypes.Parcel, filtered.Select(i => i.ParcelId));

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
                    Photos = VnArrivalPhotos.Of(photosByParcel, ins.ParcelId),
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

            /// <summary>Kiện của phiếu xuất theo luồng mới (WRO_PARCELS, bỏ dòng REMOVED).</summary>
            public Dictionary<Guid, List<WroParcel>> WroParcelsByWroRequest { get; init; } = new();
            public Dictionary<Guid, Warehouse> WarehousesById { get; init; } = new();
            public Dictionary<Guid, Carrier> CarriersById { get; init; } = new();
            public Dictionary<Guid, ShippingRoute> RoutesById { get; init; } = new();
            public Dictionary<Guid, Inventory> InventoriesById { get; init; } = new();
            public Dictionary<Guid, Parcel> ParcelsById { get; init; } = new();
            public Dictionary<Guid, Order> OrdersById { get; init; } = new();
            public Dictionary<Guid, Customer> CustomersById { get; init; } = new();

            /// <summary>Nhãn Mua hộ / Ký gửi theo đơn kho — nạp một lần cho cả trang lô.</summary>
            public Dictionary<Guid, OrderPurchaseLabel> PurchaseLabelsByOrder { get; init; } = new();

            /// <summary>Ảnh tiếp nhận kho VN theo kiện / theo lô (VN_ARRIVAL_PROOF).</summary>
            public Dictionary<Guid, List<AttachmentDto>> ArrivalPhotosByParcel { get; init; } = new();
            public Dictionary<Guid, List<AttachmentDto>> ArrivalPhotosByShipment { get; init; } = new();
        }

        private async Task<ShipmentLookups> LoadShipmentLookupsAsync()
        {
            var ordersById = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id, o => o);
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
                WroParcelsByWroRequest = (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
                    .Where(wp => !string.Equals(wp.Status, WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(wp => wp.WroRequestId)
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
                OrdersById = ordersById,
                PurchaseLabelsByOrder = await OrderPurchaseLabels.LoadAsync(_unitOfWork, ordersById.Values),
                CustomersById = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .ToDictionary(c => c.Id, c => c),
                ArrivalPhotosByParcel = await VnArrivalPhotos.LoadAsync(_unitOfWork, AttachmentEntityTypes.Parcel),
                ArrivalPhotosByShipment = await VnArrivalPhotos.LoadAsync(_unitOfWork, AttachmentEntityTypes.Shipment),
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
                    var invLabel = OrderPurchaseLabels.Of(db.PurchaseLabelsByOrder, order?.Id ?? inv.OrderId);

                    // Khách lấy theo đơn trước, vì đó mới là chủ đơn thật sự. Tồn kho cũng có
                    // cột khách nên dùng làm đường lui khi đơn chưa gắn khách.
                    var customerId = order?.CustomerId ?? inv.CustomerId;
                    customersById.TryGetValue(customerId, out var customer);

                    parcels.Add(new InternationalShipmentParcelDto
                    {
                        ParcelId = parcel.Id,
                        PackageCode = parcel.PackageCode ?? string.Empty,
                        Weight = parcel.ActualWeight ?? parcel.ChargeableWeight,
                        Length = parcel.Length,
                        Width = parcel.Width,
                        Height = parcel.Height,
                        OrderId = order?.Id ?? inv.OrderId,
                        OrderCode = order?.ConsignmentCode,
                        IsPurchase = invLabel.IsPurchase,
                        OrderType = invLabel.OrderType,
                        PurchaseCode = invLabel.PurchaseCode,
                        CustomerId = customerId == Guid.Empty ? null : customerId,
                        CustomerName = customer?.FullName,
                        PackageStatus = parcel.PackageStatus,
                        DestinationHandling = parcel.DestinationHandling,
                        // Ý khách theo từng kiện (khách chọn trước khi lô mở), lùi về lựa chọn lúc đặt đơn.
                        CustomerIntent = DestinationHandlings.Normalize(parcel.DestinationHandling) ?? order?.DefaultDestinationHandling,
                        CustomerIntentText = DescribeHandling(
                            DestinationHandlings.Normalize(parcel.DestinationHandling) ?? order?.DefaultDestinationHandling),
                        ArrivalPhotos = VnArrivalPhotos.Of(db.ArrivalPhotosByParcel, parcel.Id)
                    });
                }
            }

            // Phiếu xuất theo luồng mới không có WroItem — kiện nằm ở WRO_PARCELS. Thiếu nhánh này
            // thì lô lập từ luồng mới trả danh sách kiện rỗng, kho VN không thấy lô để tiếp nhận.
            var listedParcelIds = parcels.Select(p => p.ParcelId).ToHashSet();
            foreach (var wroParcel in wroIds.SelectMany(id =>
                         db.WroParcelsByWroRequest.TryGetValue(id, out var list) ? list : new List<WroParcel>()))
            {
                if (!listedParcelIds.Add(wroParcel.ParcelId)) continue;
                if (!allParcels.TryGetValue(wroParcel.ParcelId, out var parcel)) continue;

                Order? order = parcel.ConsignmentId.HasValue && ordersById.TryGetValue(parcel.ConsignmentId.Value, out var o) ? o : null;
                if (order != null) orderIds.Add(order.Id);
                Customer? customer = order?.CustomerId != null && customersById.TryGetValue(order.CustomerId.Value, out var c) ? c : null;
                var label = OrderPurchaseLabels.Of(db.PurchaseLabelsByOrder, order?.Id);

                parcels.Add(new InternationalShipmentParcelDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode ?? string.Empty,
                    Weight = parcel.ActualWeight ?? parcel.ChargeableWeight,
                    Length = parcel.Length,
                    Width = parcel.Width,
                    Height = parcel.Height,
                    OrderId = order?.Id ?? parcel.ConsignmentId,
                    OrderCode = order?.ConsignmentCode,
                    IsPurchase = label.IsPurchase,
                    OrderType = label.OrderType,
                    PurchaseCode = label.PurchaseCode,
                    CustomerId = customer?.Id,
                    CustomerName = customer?.FullName,
                    PackageStatus = parcel.PackageStatus,
                    DestinationHandling = parcel.DestinationHandling,
                    // Ý khách theo từng kiện (khách chọn trước khi lô mở), lùi về lựa chọn lúc đặt đơn.
                    CustomerIntent = DestinationHandlings.Normalize(parcel.DestinationHandling) ?? order?.DefaultDestinationHandling,
                    CustomerIntentText = DescribeHandling(
                        DestinationHandlings.Normalize(parcel.DestinationHandling) ?? order?.DefaultDestinationHandling),
                    ArrivalPhotos = VnArrivalPhotos.Of(db.ArrivalPhotosByParcel, parcel.Id)
                });
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
                Parcels = parcels,
                ArrivalPhotos = VnArrivalPhotos.Of(db.ArrivalPhotosByShipment, shipment.Id)
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


        /// <summary>
        /// Phiếu xuất kho đã bốc xong, gom được vào lô.
        /// </summary>
        /// <remarks>
        /// <c>PICKED</c> là trạng thái cuối của phiếu theo vòng đời mới. Bốn giá trị còn lại là
        /// của dữ liệu cũ — phiếu đã đi qua các bước đóng gói/bàn giao trước khi những bước đó
        /// chuyển sang lô. Không nhận chúng thì hàng đang dở dang kẹt vĩnh viễn ở kho nguồn.
        /// </remarks>
        private static readonly string[] WroReadyForShipmentStatuses =
            { "PICKED", "PACKING", "PACKED", "RELEASED", "HANDED_OVER", "IN_TRANSIT" };

        private static bool IsWroReadyForShipment(string? status) =>
            !string.IsNullOrWhiteSpace(status)
            && WroReadyForShipmentStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Lô vừa gom xong, kho còn đang xếp hàng.</summary>
        public const string StatusCreated = "CREATED";

        /// <summary>Kho chốt "hàng đã sẵn sàng, chờ xe tới lấy".</summary>
        public const string StatusReadyToShip = "READY_TO_SHIP";

        /// <summary>Đối tác đã lấy hàng — hàng rời kho nguồn, đang trên đường về VN.</summary>
        public const string StatusInTransit = "IN_TRANSIT";

        /// <summary>Hàng đã tới Việt Nam, chờ xe chuyển về kho.</summary>
        public const string StatusArrivedVn = "ARRIVED_VN";

        /// <summary>Lô đã về tới kho VN — mốc mở khoá cho kho đối soát.</summary>
        public const string StatusArrivedDestination = "ARRIVED_DESTINATION";

        /// <summary>
        /// Gom trạng thái về đúng các tab của màn Vận chuyển, để FE lọc theo một giá trị thay vì
        /// phải tự nhớ trạng thái nào thuộc tab nào. Nhận cả trạng thái của vòng đời cũ vì dữ
        /// liệu tồn vẫn nằm trong danh sách.
        /// </summary>
        public static string MapStatusToTab(string? status)
        {
            var s = status?.Trim().ToUpperInvariant() ?? string.Empty;

            return s switch
            {
                "DRAFT" or "CREATED" or "MANIFESTED" or "LOT_CREATED" or "PREPARING" or "READY_TO_SHIP" => "PREPARING",
                "HANDED_OVER" or "DEPARTED" or "IN_TRANSIT" or "CUSTOMS_CLEARED" => "IN_TRANSIT",
                "ARRIVED_VN" or "ARRIVED_DESTINATION" or "ARRIVED_IN_VN" => "ARRIVED",
                "ISSUE" or "CUSTOMS_REJECTED" or "HOLD" or "ON_HOLD" or "DELAYED" => "ISSUE",
                "CANCELLED" => "CANCELLED",

                // Lô cũ còn kẹt ở chặng thông quan: xếp vào "đang đi" thay vì rơi về PREPARING,
                // vì hàng của chúng đã rời kho nguồn thật.
                "CUSTOMS_EXPORT_PENDING" or "CUSTOMS_IMPORT_PENDING" => "IN_TRANSIT",
                _ => "PREPARING"
            };
        }


        /// <summary>Thêm thông báo cho chủ của từng đơn có kiện trong danh sách (một tin mỗi đơn).</summary>
        private async Task NotifyCustomersOfShipmentAsync(
            List<Parcel> parcels, Func<string, string> title, Func<string, string> content, DateTime now)
        {
            var orderIds = parcels.Where(p => p.ConsignmentId.HasValue).Select(p => p.ConsignmentId!.Value).ToHashSet();
            if (orderIds.Count == 0) return;

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).Where(o => orderIds.Contains(o.Id)).ToList();
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);
            var notificationRepo = _unitOfWork.Repository<Notification>();

            foreach (var order in orders)
            {
                if (!order.CustomerId.HasValue || !customers.TryGetValue(order.CustomerId.Value, out var customer)) continue;
                if (customer.UserId is not Guid customerUserId || customerUserId == Guid.Empty) continue;

                await notificationRepo.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = customerUserId,
                    Title = title(order.ConsignmentCode),
                    Content = content(order.ConsignmentCode),
                    IsRead = false,
                    CreatedAt = now
                });
            }
        }

        /// <summary>Kiện đã về tới kho đích nhưng kho VN chưa tiếp nhận (dữ liệu cũ có thể còn IN_TRANSIT / RELEASED).</summary>
        private static readonly HashSet<string> AwaitingReceivingStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "ARRIVED_DESTINATION", "IN_TRANSIT", "RELEASED" };

        /// <summary>Cân VN lệch quá tỷ lệ này so với cân ở kho nguồn thì mở sự cố cho quản lý kho xem.</summary>
        private const decimal WeightDeviationThreshold = 0.10m;

        /// <summary>
        /// Quy tình trạng kho ghi về loại sự cố; null là hàng tốt. Nhận cả bộ giá trị cũ của app kho
        /// (INTACT, MISSING, WET, SEAL_BROKEN) để app chưa cập nhật vẫn gửi được.
        /// </summary>
        private static string? MapReceivingCondition(string? condition, string? packageCode)
        {
            var c = condition?.Trim().ToUpperInvariant();
            return c switch
            {
                null or "" or "GOOD" or "INTACT" => null,
                "DAMAGED" or "WET" or "SEAL_BROKEN" => IncidentTypes.Damaged,
                "MISSING" or "MISSING_ITEMS" => IncidentTypes.MissingItems,
                "WRONG_ITEM" => IncidentTypes.WrongItem,
                _ => throw new ArgumentException(
                    $"Kiện {packageCode}: tình trạng không hợp lệ. Chỉ chọn: {StatusNames.QuoteAny("GOOD", "DAMAGED", "WRONG_ITEM", "MISSING_ITEMS")}.")
            };
        }

        /// <summary>Kích thước VN lệch quá tỷ lệ này so với kho nguồn thì coi là lệch cân đo (cần ảnh làm bằng chứng).</summary>
        private const decimal DimensionDeviationThreshold = 0.10m;

        private static bool Deviates(decimal? measured, decimal? origin, decimal threshold) =>
            measured is > 0m && origin is > 0m && Math.Abs(measured.Value - origin.Value) / origin.Value > threshold;

        /// <summary>
        /// Chặn xác nhận tiếp nhận khi còn kiện chưa có ảnh (VN_ARRIVAL_PROOF). Câu lỗi nêu đích
        /// danh từng kiện, và nói rõ kiện nào hỏng / lệch cân đo — kho biết phải chụp gì.
        /// </summary>
        private static void EnsureArrivalPhotos(
            IEnumerable<Parcel> targets,
            IReadOnlyDictionary<Guid, DestinationReceivingItemDto> items,
            Dictionary<Guid, List<AttachmentDto>> photos)
        {
            var missing = new List<string>();
            foreach (var parcel in targets)
            {
                if (VnArrivalPhotos.Of(photos, parcel.Id).Count > 0) continue;

                var item = items[parcel.Id];
                var reasons = new List<string>();
                if (MapReceivingCondition(item.Condition, parcel.PackageCode) != null)
                    reasons.Add(DescribeCondition(item.Condition)?.ToLowerInvariant() ?? "tình trạng bất thường");
                if (Deviates(item.ActualWeight, parcel.ActualWeight, WeightDeviationThreshold))
                    reasons.Add("lệch cân");
                if (Deviates(item.Length, parcel.Length, DimensionDeviationThreshold)
                    || Deviates(item.Width, parcel.Width, DimensionDeviationThreshold)
                    || Deviates(item.Height, parcel.Height, DimensionDeviationThreshold))
                    reasons.Add("lệch kích thước");

                var code = parcel.PackageCode ?? parcel.Id.ToString();
                missing.Add(reasons.Count == 0 ? code : $"{code} ({string.Join(", ", reasons)} — bắt buộc ảnh hiện trạng)");
            }

            if (missing.Count == 0) return;

            throw new ArgumentException(
                "Phải chụp ảnh từng kiện lúc tiếp nhận (nhãn kiện trên cân / hiện trạng thùng) trước khi xác nhận — "
                + $"mỗi kiện ít nhất 1 ảnh (documentType = {AttachmentDocumentTypes.VnArrivalProof}, entityType = {AttachmentEntityTypes.Parcel}). "
                + $"Kiện chưa có ảnh: {string.Join("; ", missing)}.");
        }

        /// <summary>Các kiện thuộc một lô.</summary>
        private async Task<List<Parcel>> GetShipmentParcelsAsync(Guid shipmentId)
        {
            var parcelIds = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .Where(sp => sp.ShipmentId == shipmentId)
                .Select(sp => sp.ParcelId)
                .ToHashSet();

            return (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToList();
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

            // 3. Chặn xác nhận nhiều lần, và chỉ nhận từ đúng mốc trước đó.
            var currentStatus = shipment.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (currentStatus == StatusArrivedDestination)
            {
                throw new ArgumentException("Lô vận chuyển này đã được xác nhận đến kho đích trước đó.");
            }

            // Khớp đúng bảng AllowedTransitions. CUSTOMS_IMPORT_PENDING giữ lại cho những lô cũ
            // đang kẹt ở chặng thông quan trước khi bỏ hai trạng thái đó khỏi vòng đời — không
            // nhận thì chúng không bao giờ về kho được nữa.
            var arrivableFrom = new[] { StatusArrivedVn, StatusInTransit, "CUSTOMS_IMPORT_PENDING" };
            if (!arrivableFrom.Contains(currentStatus))
            {
                throw new ArgumentException(
                    $"Lô chưa đủ điều kiện xác nhận về tới kho (phải đang {StatusNames.Quote(StatusArrivedVn)}, " +
                    $"hiện tại: {StatusNames.Quote(shipment.Status)}).");
            }

            var now = DateTime.UtcNow;

            // 4. Cập nhật lô -> ARRIVED_DESTINATION
            shipment.Status = StatusArrivedDestination;
            shipment.DeliveredAt = now;
            shipmentRepo.Update(shipment);

            // 5. Cập nhật các Parcel trong lô -> ARRIVED_DESTINATION (sẵn sàng cho kho VN đối soát)
            //
            // Ô kệ kho nguồn đã được nhả từ mốc IN_TRANSIT (ReleaseParcelsFromOriginAsync), và
            // bước đó xoá luôn BinId. Nên kiện nào tới đây mà VẪN còn BinId là kiện của lô cũ đi
            // thẳng từ chặng thông quan lên, chưa ai nhả ô cho nó — trừ thể tích ở đây. Kiện đã
            // nhả rồi thì không đụng vào, trừ hai lần là ô trống mà báo đầy.
            var parcels = await GetShipmentParcelsAsync(shipment.Id);
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var binRepo = _unitOfWork.Repository<Bin>();
            var binsById = (await binRepo.GetAllAsync()).ToDictionary(b => b.Id);

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
                parcel.PackageStatus = StatusArrivedDestination;
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
                CustomerMessage = request?.CustomerMessage,
                CreatedById = userId,
                TrackingTime = now
            });

            // Báo khách ngay tại đây chứ không ở nơi gọi: mốc này có hai đường vào (PUT /status và
            // PUT /arrive), đặt ở chỗ chung thì không đường nào quên báo.
            await NotifyCustomersOfShipmentAsync(parcels,
                code => $"Đơn {code} đã về kho Việt Nam",
                code => string.IsNullOrWhiteSpace(request?.CustomerMessage)
                    ? $"Hàng của đơn {code} đã về tới kho tại Việt Nam, kho đang kiểm đếm. Nhân viên sẽ liên hệ để tất toán và giao hàng."
                    : request.CustomerMessage.Trim(),
                now);

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
            bool allowed = StaffRoleHelper.IsRoleAllowed(ReceivingRoles, userRole);
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
                throw new ArgumentException($"Lô chưa đến kho đích (phải ở trạng thái {StatusNames.Quote("ARRIVED_DESTINATION")}, hiện tại: {StatusNames.Quote(shipment.Status)}).");
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

            // 5. Kho phải cân đo lại TỪNG kiện: số cân đo ở VN là số tính cước cuối, nên không
            // còn cách "bấm nhận cả lô" hay gửi parcelIds trần như trước.
            if (request?.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException(
                    "Kho phải cân đo từng kiện khi tiếp nhận: gửi items = [{ parcelId, actualWeight, length, width, height, condition }].");
            }

            var itemsByParcel = new Dictionary<Guid, DestinationReceivingItemDto>();
            foreach (var item in request.Items)
            {
                if (itemsByParcel.ContainsKey(item.ParcelId))
                    throw new ArgumentException($"Kiện {item.ParcelId} bị gửi hai lần.");
                itemsByParcel[item.ParcelId] = item;
            }

            var targets = new List<Parcel>();
            foreach (var (pid, item) in itemsByParcel)
            {
                var parcel = shipmentParcels.FirstOrDefault(p => p.Id == pid)
                    ?? throw new ArgumentException($"Parcel {pid} không thuộc lô vận chuyển này.");

                if (!AwaitingReceivingStatuses.Contains(parcel.PackageStatus?.Trim() ?? string.Empty))
                {
                    throw new ArgumentException(
                        $"Kiện {parcel.PackageCode ?? pid.ToString()} đã được kho tiếp nhận trước đó (hiện tại: {StatusNames.Quote(parcel.PackageStatus)}).");
                }

                if (item.ActualWeight is not > 0m || item.Length is not > 0m || item.Width is not > 0m || item.Height is not > 0m)
                {
                    throw new ArgumentException(
                        $"Kiện {parcel.PackageCode}: phải nhập đủ cân nặng và dài/rộng/cao đo tại kho VN.");
                }

                MapReceivingCondition(item.Condition, parcel.PackageCode);
                targets.Add(parcel);
            }

            // 5b. Ảnh tiếp nhận: MỖI kiện phải có ít nhất một ảnh (nhãn kiện trên cân / hiện trạng
            // thùng) trước khi xác nhận — cùng luật với ảnh xếp kệ ở kho nguồn. Kiện hỏng hoặc cân
            // đo lệch thì ảnh chính là bằng chứng để đòi hãng / trả lời khách.
            var arrivalPhotos = await VnArrivalPhotos.LoadAsync(
                _unitOfWork, AttachmentEntityTypes.Parcel, targets.Select(p => p.Id));
            EnsureArrivalPhotos(targets, itemsByParcel, arrivalPhotos);

            // Hướng xử lý là quyết định của khách: lấy lựa chọn khách đã đặt cho kiện, không có thì
            // lấy lựa chọn lúc đặt đơn. Kho không còn ghi đè — khách đổi ở
            // PUT /api/orders/consignments/{orderId}/destination-handling.
            var orderIds = targets.Where(p => p.ConsignmentId.HasValue)
                .Select(p => p.ConsignmentId!.Value)
                .ToHashSet();
            var customerIntentByOrder = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id) && !string.IsNullOrWhiteSpace(o.DefaultDestinationHandling))
                .ToDictionary(o => o.Id, o => o.DefaultDestinationHandling!.Trim().ToUpperInvariant());

            // 6. Cập nhật từng kiện: kiện tốt vào khu nhận, kiện có vấn đề vào khu cách ly + mở sự cố.
            var now = DateTime.UtcNow;
            var reservedIncidentCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var openedIncidents = new List<(ParcelIncident Incident, Parcel Parcel)>();

            foreach (var parcel in targets)
            {
                var item = itemsByParcel[parcel.Id];
                var incidentType = MapReceivingCondition(item.Condition, parcel.PackageCode);
                var originWeight = parcel.ActualWeight;

                await SaveParcelInspectionAsync(parcel, shipment.Id, item, userId, now);

                parcel.CheckInTime = now;
                string? customerIntent = null;
                if (parcel.ConsignmentId.HasValue)
                    customerIntentByOrder.TryGetValue(parcel.ConsignmentId.Value, out customerIntent);
                parcel.DestinationHandling ??= DestinationHandlings.Normalize(customerIntent) ?? DirectDelivery;

                if (incidentType != null)
                {
                    parcel.PackageStatus = VnParcelStatuses.Quarantined;
                    var incident = await ParcelIncidentFactory.OpenAsync(
                        _unitOfWork, parcel, incidentType, IncidentStages.Receiving,
                        string.IsNullOrWhiteSpace(item.InspectionNote)
                            ? $"Kho Việt Nam ghi nhận: {IncidentTypes.Describe(incidentType)}."
                            : item.InspectionNote,
                        userId, reservedIncidentCodes);
                    openedIncidents.Add((incident, parcel));
                    await VnArrivalPhotos.CopyToIncidentAsync(
                        _unitOfWork, VnArrivalPhotos.Of(arrivalPhotos, parcel.Id), incident.Id, parcel.PackageCode ?? string.Empty, now);
                }
                else
                {
                    parcel.PackageStatus = VnParcelStatuses.ReceivedAtDestination;

                    // Cân lệch nhiều so với lúc nhận ở kho nguồn: có thể cân sai hoặc hàng bị
                    // đổi trong lúc vận chuyển. Không chặn hàng nhưng phải có người xem trước khi
                    // lấy số này tính tiền khách.
                    if (originWeight is > 0m
                        && Math.Abs(item.ActualWeight!.Value - originWeight.Value) / originWeight.Value > WeightDeviationThreshold)
                    {
                        var incident = await ParcelIncidentFactory.OpenAsync(
                            _unitOfWork, parcel, IncidentTypes.WeightDeviation, IncidentStages.Receiving,
                            $"Cân tại kho VN {item.ActualWeight:0.###} kg, lúc nhận ở kho nguồn {originWeight:0.###} kg "
                            + $"(lệch {Math.Abs(item.ActualWeight.Value - originWeight.Value) / originWeight.Value:P0}).",
                            userId, reservedIncidentCodes);
                        openedIncidents.Add((incident, parcel));
                        await VnArrivalPhotos.CopyToIncidentAsync(
                            _unitOfWork, VnArrivalPhotos.Of(arrivalPhotos, parcel.Id), incident.Id, parcel.PackageCode ?? string.Empty, now);
                    }
                }

                parcelRepo.Update(parcel);
            }

            // 7. Lưu lịch sử nhận kiện tại kho đích
            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = "RECEIVED_AT_DESTINATION",
                TrackingStage = "DESTINATION_RECEIVING",
                Note = request?.Note,
                CreatedById = userId,
                TrackingTime = now
            });

            // Báo khách: kho đã nhận hàng (kèm việc cần làm tiếp) và kiện nào gặp sự cố cần chọn
            // cách xử lý. Cân lệch là việc nội bộ — quản lý kho xem, khách chưa cần biết.
            await NotifyCustomersOfShipmentAsync(targets,
                code => $"Đơn {code}: kho Việt Nam đã nhận hàng",
                code => $"Kho Việt Nam đã nhận và cân đo hàng của đơn {code}. "
                        + "Bạn có thể chọn giao ngay hay gửi lại kho cho từng kiện, rồi thanh toán đợt cuối theo cân đo thực tế.",
                now);

            foreach (var (incident, parcel) in openedIncidents.Where(x =>
                         !string.Equals(x.Incident.IncidentType, IncidentTypes.WeightDeviation, StringComparison.OrdinalIgnoreCase)))
            {
                await NotifyCustomersOfShipmentAsync(new List<Parcel> { parcel },
                    code => $"Kiện {parcel.PackageCode} gặp sự cố: {IncidentTypes.Describe(incident.IncidentType)}",
                    code => $"{incident.Description} Vui lòng vào đơn {code} để chọn cách xử lý (nhận như hiện trạng / bồi thường / huỷ hàng).",
                    now);
            }

            if (openedIncidents.Count > 0)
            {
                var managerId = shipment.DestinationWarehouseId.HasValue
                    ? (await _unitOfWork.Repository<Warehouse>().GetByIdAsync(shipment.DestinationWarehouseId.Value))?.ManagerId
                    : null;
                var recipients = managerId.HasValue
                    ? new List<Guid> { managerId.Value }
                    : (await _unitOfWork.Repository<User>().GetAllAsync())
                        .Where(u => u.Role != null && (u.Role.Replace(" ", string.Empty).Equals("OperationsManager", StringComparison.OrdinalIgnoreCase)))
                        .Select(u => u.Id).ToList();

                foreach (var recipient in recipients)
                {
                    await _unitOfWork.Repository<Notification>().AddAsync(new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = recipient,
                        Title = $"Lô {shipment.ShipmentCode}: {openedIncidents.Count} sự cố khi tiếp nhận",
                        Content = string.Join("; ", openedIncidents.Select(x =>
                            $"{x.Incident.IncidentCode} {x.Parcel.PackageCode} — {IncidentTypes.Describe(x.Incident.IncidentType)}")),
                        IsRead = false,
                        CreatedAt = now
                    });
                }
            }

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
                        CustomerIntent = DestinationHandlings.Normalize(p.DestinationHandling) ?? intent,
                        CustomerIntentText = DescribeHandling(DestinationHandlings.Normalize(p.DestinationHandling) ?? intent),
                        IncidentCode = openedIncidents.Where(x => x.Parcel.Id == p.Id).Select(x => x.Incident.IncidentCode).FirstOrDefault(),
                        IncidentType = openedIncidents.Where(x => x.Parcel.Id == p.Id).Select(x => x.Incident.IncidentType).FirstOrDefault(),
                        Inspection = inspectionsJustSaved.TryGetValue(p.Id, out var ins)
                            ? new ParcelInspectionResultDto
                            {
                                DeclaredQuantity = ins.DeclaredQuantity,
                                ActualQuantity = ins.ActualQuantity,
                                DeclaredWeight = ins.DeclaredWeight,
                                ActualWeight = ins.ActualWeight,
                                Length = ins.Length,
                                Width = ins.Width,
                                Height = ins.Height,
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
                                Photos = VnArrivalPhotos.Of(arrivalPhotos, p.Id),
                            }
                            : null,
                    };
                }).ToList()
            };
        }
    }
}
