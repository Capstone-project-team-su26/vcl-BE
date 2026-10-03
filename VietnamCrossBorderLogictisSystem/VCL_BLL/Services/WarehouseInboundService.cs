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
    /// <inheritdoc cref="IWarehouseInboundService"/>
    public class WarehouseInboundService : IWarehouseInboundService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Duyệt phiếu xong là phải xếp kiện lên kệ ngay; mượn lại đúng hàm kho vẫn dùng để
        // put-away thủ công, khỏi đẻ ra một đường vào kho thứ hai với luật riêng.
        private readonly IParcelService _parcelService;

        /// <summary>Nhãn tiếng Việt của nguyện vọng khách, null khi khách chưa chọn.</summary>
        private static string? DescribeCustomerIntent(string? handling) =>
            handling?.Trim().ToUpperInvariant() switch
            {
                "DIRECT_DELIVERY" => "Khách muốn giao ngay khi về VN",
                "STORE_AT_VN" => "Khách muốn gửi lại kho VN",
                _ => null,
            };

        // Lập phiếu: kho VN là người trực tiếp thấy hàng, OM lập thay cũng được.
        private static readonly string[] CreateRoles =
            { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Xem danh sach don cho nhap kho: kho lam viec, OM doi chieu truoc khi duyet.
        private static readonly string[] ViewPendingRoles =
            { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Duyệt phiếu: chỉ OM, đúng như anh Hoàng chốt.
        private static readonly string[] ApproveRoles =
            { "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        public const string StatusPending = "INBOUND_PENDING";
        public const string StatusApproved = "INBOUND_APPROVED";
        public const string StatusRejected = "INBOUND_REJECTED";

        private const string StoreAtVn = "STORE_AT_VN";
        private const string ReceivedAtDestination = "RECEIVED_AT_DESTINATION";
        private const string Stored = "STORED";

        /// <summary>Mot cho duy nhat quyet dinh don da tat toan chua.</summary>
        private readonly ISettlementQueryService _settlementQueryService;

        public WarehouseInboundService(
            IUnitOfWork unitOfWork,
            IParcelService parcelService,
            ISettlementQueryService settlementQueryService)
        {
            _unitOfWork = unitOfWork;
            _parcelService = parcelService;
            _settlementQueryService = settlementQueryService;
        }

        public async Task<WarehouseInboundRequestDto> CreateAsync(
            Guid userId, string userRole, CreateWarehouseInboundRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu phiếu nhập kho không được để trống.");

            EnsureRole(userRole, CreateRoles, "Chỉ nhân viên kho hoặc Operations được lập phiếu nhập kho.");

            var byOrder = request.OrderId.HasValue && request.OrderId != Guid.Empty;
            var byShipment = request.ShipmentId.HasValue && request.ShipmentId != Guid.Empty;

            if (byOrder == byShipment)
            {
                throw new ArgumentException(byOrder
                    ? "Chỉ chọn một trong hai: đơn hàng hoặc lô vận chuyển."
                    : "Chọn đơn hàng hoặc lô vận chuyển để lập phiếu nhập kho.");
            }

            Shipment? shipment = null;
            List<Parcel> candidates;

            if (byShipment)
            {
                shipment = await _unitOfWork.Repository<Shipment>().GetByIdAsync(request.ShipmentId!.Value)
                    ?? throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {request.ShipmentId}.");

                var shipmentParcelIds = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                    .Where(sp => sp.ShipmentId == shipment.Id)
                    .Select(sp => sp.ParcelId)
                    .ToHashSet();

                candidates = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                    .Where(p => shipmentParcelIds.Contains(p.Id))
                    .ToList();
            }
            else
            {
                candidates = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                    .Where(p => p.ConsignmentId == request.OrderId!.Value)
                    .ToList();

                if (candidates.Count == 0)
                {
                    throw new NotFoundException($"Không tìm thấy kiện nào của đơn {request.OrderId}.");
                }
            }

            // Chi nhung phieu CON HIEU LUC moi giu cho kien. Phieu bi OM tu choi phai nha kien
            // ra, khong thi kho lap sai mot lan la kien do ket vinh vien, khong lap lai duoc.
            var rejectedRequestIds = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync())
                .Where(r => string.Equals(r.Status, StatusRejected, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Id)
                .ToHashSet();

            var alreadyInRequest = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Where(i => !rejectedRequestIds.Contains(i.InboundRequestId))
                .Select(i => i.ParcelId)
                .ToHashSet();

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);

            // Tien truoc, hang vao so sau - chot chan thu tien cua ca chang cuoi.
            var settledOrderIds = await _settlementQueryService.GetSettledOrderIdsAsync(orders.Values.ToList());

            List<Parcel> targets;
            if (request.ParcelIds != null && request.ParcelIds.Count > 0)
            {
                targets = new List<Parcel>();
                foreach (var pid in request.ParcelIds.Distinct())
                {
                    var parcel = candidates.FirstOrDefault(p => p.Id == pid)
                        ?? throw new ArgumentException(byShipment
                            ? $"Kiện {pid} không thuộc lô vận chuyển này."
                            : $"Kiện {pid} không thuộc đơn hàng này.");

                    EnsureParcelEligible(parcel, alreadyInRequest);
                    EnsureOrderReadyForInbound(parcel, orders, settledOrderIds);
                    targets.Add(parcel);
                }
            }
            else
            {
                var pending = candidates
                    .Where(p => IsStoreAtVn(p) && IsReceived(p) && !alreadyInRequest.Contains(p.Id))
                    .ToList();

                if (pending.Count == 0)
                {
                    throw new ArgumentException(byShipment
                        ? "Lô này không còn kiện nào cần nhập kho. Kiện phải được kho kiểm đếm và phân loại là khách gửi lại kho VN thì mới lập phiếu được."
                        : "Đơn này không còn kiện nào chờ nhập kho.");
                }

                targets = pending
                    .Where(p => IsOrderReadyForInbound(p, orders, settledOrderIds))
                    .ToList();

                if (targets.Count == 0)
                {
                    // Noi dung cai dang vuong thay vi mot cau chung chung.
                    EnsureOrderReadyForInbound(pending[0], orders, settledOrderIds);
                }
            }

            // Kho nhan hang: uu tien tham so, sau do toi kho dich cua lo cho kien.
            if (shipment == null)
            {
                var targetIds = targets.Select(t => t.Id).ToHashSet();
                var shipmentIds = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                    .Where(sp => targetIds.Contains(sp.ParcelId))
                    .Select(sp => sp.ShipmentId)
                    .Distinct()
                    .ToList();

                var relatedShipments = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                    .Where(sh => shipmentIds.Contains(sh.Id))
                    .ToList();

                // Kien cung mot lo thi ghi lo vao phieu cho de tra; rai nhieu lo thi de trong,
                // ghi bua mot lo dai dien la phieu noi doi cho hang nam.
                shipment = relatedShipments.Count == 1 ? relatedShipments[0] : null;

                if (request.WarehouseId == null && relatedShipments.Count > 0)
                {
                    var warehouses = relatedShipments
                        .Where(sh => sh.DestinationWarehouseId.HasValue)
                        .Select(sh => sh.DestinationWarehouseId!.Value)
                        .Distinct()
                        .ToList();

                    if (warehouses.Count == 1)
                    {
                        request.WarehouseId = warehouses[0];
                    }
                }
            }

            var warehouseId = request.WarehouseId ?? shipment?.DestinationWarehouseId;
            if (warehouseId == null || warehouseId == Guid.Empty)
            {
                throw new ArgumentException("Không xác định được kho VN nhận hàng. Truyền warehouseId hoặc gán kho đích cho lô.");
            }

            var now = DateTime.UtcNow;
            var inbound = new WarehouseInboundRequest
            {
                Id = Guid.NewGuid(),
                InboundCode = $"WIN-{now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                ShipmentId = shipment?.Id,
                WarehouseId = warehouseId,
                Status = StatusPending,
                Note = request.Note,
                CreatedBy = userId,
                CreatedAt = now
            };
            await _unitOfWork.Repository<WarehouseInboundRequest>().AddAsync(inbound);

            var itemRepo = _unitOfWork.Repository<WarehouseInboundItem>();
            foreach (var parcel in targets)
            {
                await itemRepo.AddAsync(new WarehouseInboundItem
                {
                    Id = Guid.NewGuid(),
                    InboundRequestId = inbound.Id,
                    ParcelId = parcel.Id
                });
            }

            await _unitOfWork.SaveChangesAsync();
            return await BuildDtoAsync(inbound);
        }

        /// <inheritdoc />
        public async Task<List<PendingInboundOrderDto>> GetPendingInboundOrdersAsync(string userRole)
        {
            EnsureRole(userRole, ViewPendingRoles, "Vai trò hiện tại không được xem danh sách đơn chờ nhập kho.");

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToList();
            var settledOrderIds = await _settlementQueryService.GetSettledOrderIdsAsync(orders);
            if (settledOrderIds.Count == 0) return new List<PendingInboundOrderDto>();

            var orderMap = orders.ToDictionary(o => o.Id);

            var rejectedRequests = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync())
                .Where(r => string.Equals(r.Status, StatusRejected, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(r => r.Id);

            var inboundItems = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync()).ToList();

            var activeParcelIds = inboundItems
                .Where(i => !rejectedRequests.ContainsKey(i.InboundRequestId))
                .Select(i => i.ParcelId)
                .ToHashSet();

            var rejectionByParcel = inboundItems
                .Where(i => rejectedRequests.ContainsKey(i.InboundRequestId))
                .GroupBy(i => i.ParcelId)
                .ToDictionary(g => g.Key, g => rejectedRequests[g.Last().InboundRequestId].RejectionReason);

            // Kien cho nhap kho: da kiem dem, duoc phan loai gui lai kho, chua nam phieu con hieu luc.
            var pendingParcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId.HasValue
                            && settledOrderIds.Contains(p.ConsignmentId.Value)
                            && IsStoreAtVn(p)
                            && IsReceived(p)
                            && !activeParcelIds.Contains(p.Id))
                .ToList();

            if (pendingParcels.Count == 0) return new List<PendingInboundOrderDto>();

            var parcelIds = pendingParcels.Select(p => p.Id).ToHashSet();

            var shipmentByParcel = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .Where(sp => parcelIds.Contains(sp.ParcelId))
                .GroupBy(sp => sp.ParcelId)
                .ToDictionary(g => g.Key, g => g.Last().ShipmentId);

            var shipments = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .ToDictionary(sh => sh.Id);

            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .ToDictionary(w => w.Id);

            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .ToDictionary(c => c.Id);

            var users = (await _unitOfWork.Repository<User>().GetAllAsync())
                .ToDictionary(u => u.Id);

            var inspections = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync())
                .Where(i => parcelIds.Contains(i.ParcelId))
                .GroupBy(i => i.ParcelId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.InspectedAt).First());

            var result = new List<PendingInboundOrderDto>();

            foreach (var group in pendingParcels.GroupBy(p => p.ConsignmentId!.Value))
            {
                if (!orderMap.TryGetValue(group.Key, out var order)) continue;

                var isPurchase = string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase);

                // Ky gui phai cho Sale bam bao kho; mua ho vao thang.
                if (!isPurchase && !order.WarehouseNotifiedAt.HasValue) continue;

                var groupParcels = group.ToList();

                var shipmentCodes = groupParcels
                    .Select(p => shipmentByParcel.TryGetValue(p.Id, out var sid) ? sid : (Guid?)null)
                    .Where(sid => sid.HasValue)
                    .Select(sid => shipments.TryGetValue(sid!.Value, out var sh) ? sh.ShipmentCode : null)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct()
                    .Select(code => code!)
                    .ToList();

                Guid? warehouseId = groupParcels
                    .Select(p => shipmentByParcel.TryGetValue(p.Id, out var sid)
                                 && shipments.TryGetValue(sid, out var sh)
                        ? sh.DestinationWarehouseId
                        : null)
                    .FirstOrDefault(w => w.HasValue);

                var customer = order.CustomerId.HasValue && customers.TryGetValue(order.CustomerId.Value, out var c)
                    ? c
                    : null;

                var notifiedBy = order.WarehouseNotifiedById.HasValue
                                 && users.TryGetValue(order.WarehouseNotifiedById.Value, out var u)
                    ? u.FullName
                    : null;

                result.Add(new PendingInboundOrderDto
                {
                    OrderId = order.Id,
                    OrderCode = order.ConsignmentCode,
                    OrderType = isPurchase ? "PURCHASE" : "CONSIGNMENT",
                    CustomerName = customer?.FullName,
                    CustomerPhone = customer?.Phone,
                    StoreAtVnParcelCount = groupParcels.Count,
                    StoreAtVnTotalWeight = groupParcels.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m),
                    ParcelIds = groupParcels.Select(p => p.Id).ToList(),
                    Parcels = groupParcels.Select(p => new PendingInboundParcelDto
                    {
                        ParcelId = p.Id,
                        PackageCode = p.PackageCode ?? string.Empty,
                        Weight = p.ActualWeight ?? p.ChargeableWeight,
                        ShipmentCode = shipmentByParcel.TryGetValue(p.Id, out var sid)
                                       && shipments.TryGetValue(sid, out var sh)
                            ? sh.ShipmentCode
                            : null,
                        HasDiscrepancy = inspections.TryGetValue(p.Id, out var ins) && ins.HasDiscrepancy,
                        DiscrepancySummary = inspections.TryGetValue(p.Id, out var ins2) && ins2.HasDiscrepancy
                            ? ins2.Note
                            : null,
                    }).ToList(),
                    ShipmentCodes = shipmentCodes,
                    WarehouseId = warehouseId,
                    WarehouseName = warehouseId.HasValue && warehouses.TryGetValue(warehouseId.Value, out var wh)
                        ? wh.WarehouseName
                        : null,
                    ArrivedAt = groupParcels.Max(p => p.CheckInTime ?? p.PutAwayTime),
                    WarehouseNotifiedAt = order.WarehouseNotifiedAt,
                    WarehouseNotifiedByName = notifiedBy,
                    WarehouseNotifiedNote = order.WarehouseNotifiedNote,
                    DiscrepancyParcelCount = groupParcels.Count(p =>
                        inspections.TryGetValue(p.Id, out var ins) && ins.HasDiscrepancy),
                    LastRejectionReason = groupParcels
                        .Select(p => rejectionByParcel.TryGetValue(p.Id, out var reason) ? reason : null)
                        .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason)),
                });
            }

            // Bao lau nhat len dau - do la don dang tre nhat.
            return result
                .OrderBy(r => r.WarehouseNotifiedAt ?? r.ArrivedAt ?? DateTime.MaxValue)
                .ToList();
        }

        public async Task<WarehouseInboundRequestDto> UpdateStatusAsync(
            Guid inboundRequestId, Guid userId, string userRole, UpdateWarehouseInboundStatusDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            EnsureRole(userRole, ApproveRoles, "Chỉ Operations Manager được duyệt phiếu nhập kho.");

            var repo = _unitOfWork.Repository<WarehouseInboundRequest>();
            var inbound = await repo.GetByIdAsync(inboundRequestId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu nhập kho với ID {inboundRequestId}");

            var target = request.Status?.Trim().ToUpperInvariant();
            if (target != StatusApproved && target != StatusRejected)
            {
                throw new ArgumentException("Trạng thái không hợp lệ. Chỉ được duyệt hoặc từ chối.");
            }

            var current = inbound.Status?.Trim().ToUpperInvariant();

            // Phiếu đã duyệt mà hàng chưa lên kệ thì cho duyệt lại. Bước xếp kệ chạy SAU khi
            // trạng thái phiếu đã ghi (bắt buộc: put-away đòi phiếu phải ở trạng thái đã duyệt),
            // nên nếu nó ném giữa chừng — kho hết ô trống chẳng hạn — sẽ còn lại phiếu ghi "đã
            // duyệt" mà hàng vẫn ngoài sổ. Chặn cứng ở đây là hàng kẹt vĩnh viễn; cho bấm lại
            // thì OM chỉ việc dọn ô kệ rồi duyệt lần nữa.
            var canRetryPutAway = current == StatusApproved
                                  && target == StatusApproved
                                  && await HasParcelsNotStoredAsync(inbound);

            if (current != StatusPending && !canRetryPutAway)
            {
                throw new ArgumentException($"Phiếu đã được xử lý trước đó (hiện tại: {StatusNames.Quote(inbound.Status)}), không duyệt lại được.");
            }

            // Từ chối mà không nói lý do thì kho không biết phải sửa gì.
            if (target == StatusRejected && string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                throw new ArgumentException("Từ chối phiếu nhập kho thì bắt buộc ghi lý do.");
            }

            inbound.Status = target;
            inbound.ApprovedBy = userId;
            inbound.ApprovedAt = DateTime.UtcNow;
            inbound.RejectionReason = target == StatusRejected ? request.RejectionReason!.Trim() : null;
            repo.Update(inbound);

            await _unitOfWork.SaveChangesAsync();

            // OM gật là hàng chính thức vào kho, không chỉ đổi trạng thái tờ phiếu.
            if (target == StatusApproved)
            {
                await StoreApprovedParcelsAsync(inbound, userId);
            }

            return await BuildDtoAsync(inbound);
        }

        /// <summary>Phiếu còn kiện nào chưa nằm trên kệ không.</summary>
        private async Task<bool> HasParcelsNotStoredAsync(WarehouseInboundRequest inbound)
        {
            var parcelIds = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Where(i => i.InboundRequestId == inbound.Id)
                .Select(i => i.ParcelId)
                .ToHashSet();

            if (parcelIds.Count == 0) return false;

            return (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .Any(p => !string.Equals(p.PackageStatus, Stored, StringComparison.OrdinalIgnoreCase)
                          || !p.BinId.HasValue);
        }

        /// <summary>
        /// Xếp mọi kiện của phiếu vừa duyệt lên kệ kho nhận.
        /// </summary>
        /// <remarks>
        /// Chọn ô kệ tự động theo sức chứa còn lại. Kho vẫn dời được sang ô khác sau đó, nhưng
        /// ngay lúc duyệt hàng đã có vị trí và có mặt trong tồn kho — đó mới là "vào kho".
        /// </remarks>
        private async Task StoreApprovedParcelsAsync(WarehouseInboundRequest inbound, Guid userId)
        {
            if (!inbound.WarehouseId.HasValue) return;

            var parcelIds = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Where(i => i.InboundRequestId == inbound.Id)
                .Select(i => i.ParcelId)
                .ToHashSet();

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToList();

            foreach (var parcel in parcels)
            {
                // Kiện kho đã tự xếp trước khi OM duyệt thì để nguyên chỗ cũ.
                if (string.Equals(parcel.PackageStatus, Stored, StringComparison.OrdinalIgnoreCase)
                    && parcel.BinId.HasValue)
                {
                    continue;
                }

                var location = await ResolveBinAsync(inbound.WarehouseId.Value, parcel);
                await _parcelService.PutAwayParcelAsync(parcel.Id, location);
            }
        }

        /// <summary>
        /// Ô kệ khu lưu kho đầu tiên còn chỗ trong kho nhận cho một kiện. Put-away kiểm lại đúng
        /// luật đó nên không chọn phải ô rồi bị chính nó từ chối.
        /// </summary>
        private async Task<ParcelPutAwayRequest> ResolveBinAsync(Guid warehouseId, Parcel parcel)
        {
            var bins = await BinPlacementPolicy.StorageBinsAsync(_unitOfWork, warehouseId);
            if (bins.Count == 0)
            {
                throw new ArgumentException(
                    "Kho nhận chưa có ô kệ nào thuộc khu lưu kho (STORAGE) đang hoạt động nên không xếp hàng vào được. " +
                    "Khai báo khu lưu kho và ô kệ rồi duyệt lại phiếu.");
            }

            var occupancy = await BinPlacementPolicy.OccupancyAsync(_unitOfWork);
            var bin = bins
                .Select(x => x.Bin)
                .FirstOrDefault(b => BinPlacementPolicy.Fits(b, occupancy.GetValueOrDefault(b.Id), parcel))
                ?? throw new ArgumentException(
                    $"Khu lưu kho của kho nhận không còn ô kệ nào đủ chỗ cho kiện {parcel.PackageCode}. " +
                    "Dọn bớt hoặc mở thêm ô kệ rồi duyệt lại phiếu.");

            return new ParcelPutAwayRequest { WarehouseId = warehouseId, BinId = bin.Id };
        }

        public async Task<WarehouseInboundRequestDto> GetByIdAsync(Guid inboundRequestId)
        {
            var inbound = await _unitOfWork.Repository<WarehouseInboundRequest>().GetByIdAsync(inboundRequestId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu nhập kho với ID {inboundRequestId}");

            return await BuildDtoAsync(inbound);
        }

        public async Task<List<WarehouseInboundRequestDto>> GetListAsync(string? status, Guid? shipmentId)
        {
            var all = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync()).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var s = status.Trim();
                all = all.Where(r => string.Equals(r.Status, s, StringComparison.OrdinalIgnoreCase));
            }

            if (shipmentId.HasValue && shipmentId.Value != Guid.Empty)
            {
                all = all.Where(r => r.ShipmentId == shipmentId.Value);
            }

            var result = new List<WarehouseInboundRequestDto>();
            foreach (var inbound in all.OrderByDescending(r => r.CreatedAt ?? DateTime.MinValue))
            {
                result.Add(await BuildDtoAsync(inbound));
            }
            return result;
        }

        // ----------------------------------------------------------------------------------

        private static void EnsureRole(string userRole, string[] allowed, string message)
        {
            // Biến thể tên vai trò nhân viên kho ("Warehouse Staff VN"…) khớp WAREHOUSESTAFF.
            var ok = StaffRoleHelper.IsRoleAllowed(allowed, userRole);
            if (!ok) throw new UnauthorizedAccessException(message);
        }

        private static bool IsStoreAtVn(Parcel parcel) =>
            string.Equals(parcel.DestinationHandling, StoreAtVn, StringComparison.OrdinalIgnoreCase);

        private static bool IsReceived(Parcel parcel) =>
            string.Equals(parcel.PackageStatus, ReceivedAtDestination, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Kiện chỉ vào được phiếu nhập kho khi kho đã mở lô nhận nó và khách đã nói là gửi lại.
        /// Chặn ở đây để không có chuyện hàng đang trên đường mà đã lập phiếu nhập kho, hoặc kiện
        /// khách chờ nhận ngay lại bị đem cất lên kệ rồi tính tiền lưu kho.
        /// </summary>
        private static void EnsureParcelEligible(Parcel parcel, HashSet<Guid> alreadyInRequest)
        {
            var code = parcel.PackageCode ?? parcel.Id.ToString();

            if (!IsReceived(parcel))
            {
                throw new ArgumentException(
                    $"Kiện {code} chưa được kho xác nhận nhận tại kho đích (hiện tại: {StatusNames.Quote(parcel.PackageStatus)}).");
            }

            if (!IsStoreAtVn(parcel))
            {
                throw new ArgumentException(
                    $"Kiện {code} không phải hàng khách gửi lại kho VN " +
                    $"(hướng xử lý hiện tại: {parcel.DestinationHandling ?? "chưa phân loại"}). " +
                    "Kiện giao thẳng cho khách thì không nhập kho.");
            }

            if (alreadyInRequest.Contains(parcel.Id))
            {
                throw new ArgumentException($"Kiện {code} đã nằm trong một phiếu nhập kho khác.");
            }
        }

        /// <summary>
        /// Don da du dieu kien cho hang vao so kho chua.
        /// </summary>
        /// <remarks>
        /// Hai dieu kien: khach da tra xong tien, va (rieng ky gui) Sale da bam bao cho kho.
        /// Don mua ho khong di qua buoc bao kho nen chi can dieu kien tien.
        /// </remarks>
        private static bool IsOrderReadyForInbound(
            Parcel parcel, IReadOnlyDictionary<Guid, Order> orders, IReadOnlySet<Guid> settledOrderIds)
        {
            if (!parcel.ConsignmentId.HasValue) return false;
            if (!orders.TryGetValue(parcel.ConsignmentId.Value, out var order)) return false;

            if (!settledOrderIds.Contains(order.Id)) return false;

            if (string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
                return true;

            return order.WarehouseNotifiedAt.HasValue;
        }

        private static void EnsureOrderReadyForInbound(
            Parcel parcel, IReadOnlyDictionary<Guid, Order> orders, IReadOnlySet<Guid> settledOrderIds)
        {
            if (IsOrderReadyForInbound(parcel, orders, settledOrderIds)) return;

            var code = parcel.PackageCode ?? parcel.Id.ToString();
            Order? order = null;
            if (parcel.ConsignmentId.HasValue) orders.TryGetValue(parcel.ConsignmentId.Value, out order);

            var label = order?.ConsignmentCode ?? ("của kiện " + code);

            if (order == null || !settledOrderIds.Contains(order.Id))
            {
                throw new ArgumentException(
                    $"Đơn {label} chưa tất toán. Khách trả nốt đợt cuối rồi kho mới nhập hàng vào kho được.");
            }

            throw new ArgumentException(
                $"Đơn {label} chưa được Sale thông báo cho kho. Đơn chỉ vào mục Đơn hàng cần xử lý của kho sau khi Sale bấm thông báo.");
        }

        private static string DescribeStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            StatusPending => "Chờ duyệt",
            StatusApproved => "Đã duyệt",
            StatusRejected => "Bị từ chối",
            _ => status ?? string.Empty
        };

        private async Task<WarehouseInboundRequestDto> BuildDtoAsync(WarehouseInboundRequest inbound)
        {
            var items = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Where(i => i.InboundRequestId == inbound.Id)
                .ToList();
            var parcelIds = items.Select(i => i.ParcelId).ToHashSet();

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToList();

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);
            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id);
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);

            var shipment = inbound.ShipmentId.HasValue
                ? await _unitOfWork.Repository<Shipment>().GetByIdAsync(inbound.ShipmentId.Value)
                : null;
            var warehouse = inbound.WarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(inbound.WarehouseId.Value)
                : null;

            var lines = new List<WarehouseInboundParcelDto>();
            foreach (var parcel in parcels)
            {
                Order? order = null;
                if (parcel.ConsignmentId.HasValue) orders.TryGetValue(parcel.ConsignmentId.Value, out order);

                Customer? customer = null;
                if (order?.CustomerId is Guid cid) customers.TryGetValue(cid, out customer);

                Bin? bin = null;
                if (parcel.BinId.HasValue) bins.TryGetValue(parcel.BinId.Value, out bin);

                lines.Add(new WarehouseInboundParcelDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode ?? string.Empty,
                    PackageStatus = parcel.PackageStatus ?? string.Empty,
                    OrderId = order?.Id,
                    OrderCode = order?.ConsignmentCode,
                    CustomerName = customer?.FullName,
                    // Ý khách theo TỪNG KIỆN (khách đổi ở PUT destination-handling), lùi về lựa chọn lúc
                    // đặt đơn. Trước đây chỉ lấy lựa chọn lúc đặt đơn nên kiện khách đã đổi sang gửi kho
                    // vẫn hiện "Muốn giao ngay" ở màn OM duyệt — OM tưởng kho lập nhầm.
                    CustomerIntent = DestinationHandlings.Normalize(parcel.DestinationHandling) ?? order?.DefaultDestinationHandling,
                    CustomerIntentText = DescribeCustomerIntent(
                        DestinationHandlings.Normalize(parcel.DestinationHandling) ?? order?.DefaultDestinationHandling),
                    BinId = parcel.BinId,
                    BinCode = bin?.BinCode
                });
            }

            string? NameOf(Guid? id) =>
                id.HasValue && users.TryGetValue(id.Value, out var u) ? u.FullName : null;

            return new WarehouseInboundRequestDto
            {
                InboundRequestId = inbound.Id,
                InboundCode = inbound.InboundCode ?? string.Empty,
                ShipmentId = inbound.ShipmentId,
                ShipmentCode = shipment?.ShipmentCode,
                WarehouseId = inbound.WarehouseId,
                WarehouseName = warehouse?.WarehouseName,
                Status = inbound.Status ?? string.Empty,
                StatusText = DescribeStatus(inbound.Status),
                Note = inbound.Note,
                RejectionReason = inbound.RejectionReason,
                CreatedByName = NameOf(inbound.CreatedBy),
                CreatedAt = inbound.CreatedAt,
                ApprovedByName = NameOf(inbound.ApprovedBy),
                ApprovedAt = inbound.ApprovedAt,
                TotalParcels = lines.Count,
                Parcels = lines
            };
        }
    }
}
