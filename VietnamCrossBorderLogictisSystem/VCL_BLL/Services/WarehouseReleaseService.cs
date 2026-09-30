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
    /// <summary>
    /// Phiếu xuất kho quốc tế theo luồng chốt ở docs/migrate-source-cu/06-luong-xuat-kho-quoc-te.md:
    /// nhân viên kho lập phiếu → quản lý kho duyệt → kho bốc hàng sang khu xuất → phiếu vào lô.
    /// <para>
    /// Hai chốt chặn duy nhất là gửi duyệt (khoá hàng) và duyệt (người khác người lập); phần còn
    /// lại chạy theo hàng thật, nên mỗi lần bốc kiện là một lần chuyển chỗ có thật trong sổ sách.
    /// </para>
    /// </summary>
    public class WarehouseReleaseService : IWarehouseReleaseService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;
        private readonly IAttachmentService _attachmentService;

        public WarehouseReleaseService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            IAttachmentService attachmentService)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _attachmentService = attachmentService;
        }

        // ── Đọc ────────────────────────────────────────────────────────────────────────────

        public async Task<List<EligibleParcelDto>> GetEligibleParcelsAsync(
            Guid userId, string role, Guid shippingRouteId, string? search, bool includeBlocked)
        {
            var route = await _unitOfWork.Repository<ShippingRoute>().GetByIdAsync(shippingRouteId)
                ?? throw new NotFoundException($"Không tìm thấy tuyến vận chuyển {shippingRouteId}.");

            var originWarehouseId = route.OriginWarehouseId
                ?? throw new ArgumentException($"Tuyến {route.RouteCode} chưa khai kho nguồn nên không lập phiếu xuất được.");

            await EnsureWarehouseScopeAsync(userId, role, originWarehouseId);

            var ctx = await ExportEligibilityContext.LoadAsync(_unitOfWork);
            var candidates = ctx.EvaluateWarehouse(originWarehouseId);

            var customerNames = await LoadCustomerNamesAsync();
            var purchaseLabels = await OrderPurchaseLabels.LoadAsync(
                _unitOfWork, candidates.Where(c => includeBlocked || c.IsEligible).Select(c => c.Order));

            var items = candidates
                .Where(c => includeBlocked || c.IsEligible)
                .Select(c => (c, label: OrderPurchaseLabels.Of(purchaseLabels, c.Order?.Id)))
                .Select(t => new EligibleParcelDto
                {
                    ParcelId = t.c.Parcel.Id,
                    PackageCode = t.c.Parcel.PackageCode,
                    OrderId = t.c.Order?.Id,
                    ConsignmentCode = t.c.Order?.ConsignmentCode,
                    CustomerName = CustomerNameOf(customerNames, t.c.Order),
                    ProductName = t.c.OrderItem?.ProductName,
                    Weight = t.c.Parcel.ActualWeight,
                    Length = t.c.Parcel.Length,
                    Width = t.c.Parcel.Width,
                    Height = t.c.Parcel.Height,
                    BinCode = t.c.Bin?.BinCode,
                    ShelfCode = t.c.Shelf?.ShelfCode,
                    ZoneName = t.c.Zone?.ZoneName,
                    IsPurchase = t.label.IsPurchase,
                    OrderType = t.label.OrderType,
                    PurchaseCode = t.label.PurchaseCode,
                    IsEligible = t.c.IsEligible,
                    Reasons = t.c.Reasons
                        .Select(r => new ExportBlockReasonDto { Code = r, Message = ExportBlockReasons.Describe(r) })
                        .ToList()
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                items = items.Where(i =>
                        Contains(i.PackageCode, keyword)
                        || Contains(i.ConsignmentCode, keyword)
                        || Contains(i.PurchaseCode, keyword)
                        || Contains(i.CustomerName, keyword)
                        || Contains(i.ProductName, keyword)
                        || Contains(i.BinCode, keyword))
                    .ToList();
            }

            return items;
        }

        public async Task<PagedWroListDto> GetListAsync(Guid userId, string role, WroListQuery query)
        {
            query ??= new WroListQuery();
            var lookup = await LoadLookupAsync();

            var requests = lookup.Requests.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var wanted = query.Status.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                requests = requests.Where(r => wanted.Contains(r.Status?.Trim() ?? string.Empty));
            }

            if (query.WarehouseId.HasValue)
                requests = requests.Where(r => r.OriginWarehouseId == query.WarehouseId.Value);

            var list = requests.ToList();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var keyword = query.Search.Trim();
                var parcelCodesByRequest = lookup.Parcels
                    .GroupBy(wp => wp.WroRequestId)
                    .ToDictionary(g => g.Key, g => g
                        .Select(wp => lookup.ParcelOf(wp.ParcelId)?.PackageCode ?? string.Empty)
                        .ToList());

                list = list.Where(r =>
                        Contains(r.WroCode, keyword)
                        || (parcelCodesByRequest.TryGetValue(r.Id, out var codes)
                            && codes.Any(c => Contains(c, keyword))))
                    .ToList();
            }

            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);
            var total = list.Count;

            var page = list
                .OrderByDescending(r => r.CreatedAt ?? DateTime.MinValue)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(r => BuildListItem(r, lookup))
                .ToList();

            return new PagedWroListDto
            {
                Items = page,
                TotalCount = total,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(total / (double)pageSize)
            };
        }

        public async Task<WroDetailDto> GetDetailAsync(Guid userId, string role, Guid requestId)
        {
            var lookup = await LoadLookupAsync();
            var request = lookup.Requests.FirstOrDefault(r => r.Id == requestId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu xuất kho {requestId}.");

            return await BuildDetailAsync(request, lookup, userId, role);
        }

        // ── Lập phiếu ──────────────────────────────────────────────────────────────────────

        public async Task<WroDetailDto> CreateAsync(Guid userId, string role, CreateWarehouseReleaseRequest request)
        {
            EnsureWarehouseOperator(role);
            if (request == null || request.ParcelIds == null || request.ParcelIds.Count == 0)
                throw new ArgumentException("Phiếu xuất kho phải có ít nhất một kiện.");

            var route = await _unitOfWork.Repository<ShippingRoute>().GetByIdAsync(request.ShippingRouteId)
                ?? throw new NotFoundException($"Không tìm thấy tuyến vận chuyển {request.ShippingRouteId}.");
            if (!route.IsActive)
                throw new ArgumentException($"Tuyến {route.RouteCode} đang ngừng sử dụng.");

            var originWarehouseId = route.OriginWarehouseId
                ?? throw new ArgumentException($"Tuyến {route.RouteCode} chưa khai kho nguồn.");
            var destinationWarehouseId = route.DestinationWarehouseId
                ?? throw new ArgumentException($"Tuyến {route.RouteCode} chưa khai kho đích.");

            await EnsureWarehouseScopeAsync(userId, role, originWarehouseId);

            var ctx = await ExportEligibilityContext.LoadAsync(_unitOfWork);
            var parcels = ResolveParcels(ctx, request.ParcelIds);
            EnsureAllEligible(ctx, parcels, originWarehouseId, null);

            var now = DateTime.UtcNow;
            var wro = new WroRequest
            {
                Id = Guid.NewGuid(),
                WroCode = await GenerateWroCodeAsync(),
                Status = WroStatuses.Draft,
                ShippingRouteId = route.Id,
                OriginWarehouseId = originWarehouseId,
                DestinationWarehouseId = destinationWarehouseId,
                UserId = userId,
                CreatedAt = now,
                ExportReason = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                IsLegacy = false
            };

            await _unitOfWork.Repository<WroRequest>().AddAsync(wro);

            foreach (var parcel in parcels)
                await AddParcelRowAsync(wro.Id, parcel, now);

            await _unitOfWork.SaveChangesAsync();
            return await GetDetailAsync(userId, role, wro.Id);
        }

        public async Task<WroDetailDto> UpdateAsync(Guid userId, string role, Guid requestId, CreateWarehouseReleaseRequest request)
        {
            EnsureWarehouseOperator(role);
            if (request == null || request.ParcelIds == null || request.ParcelIds.Count == 0)
                throw new ArgumentException("Phiếu xuất kho phải có ít nhất một kiện.");

            var wro = await GetRequestAsync(requestId);
            EnsureStatus(wro, WroStatuses.Draft);
            EnsureCreatorOrAdmin(wro, userId, role);
            await EnsureWarehouseScopeAsync(userId, role, wro.OriginWarehouseId);

            var route = await _unitOfWork.Repository<ShippingRoute>().GetByIdAsync(request.ShippingRouteId)
                ?? throw new NotFoundException($"Không tìm thấy tuyến vận chuyển {request.ShippingRouteId}.");
            var originWarehouseId = route.OriginWarehouseId
                ?? throw new ArgumentException($"Tuyến {route.RouteCode} chưa khai kho nguồn.");
            await EnsureWarehouseScopeAsync(userId, role, originWarehouseId);

            var ctx = await ExportEligibilityContext.LoadAsync(_unitOfWork);
            var parcels = ResolveParcels(ctx, request.ParcelIds);
            EnsureAllEligible(ctx, parcels, originWarehouseId, wro.Id);

            var now = DateTime.UtcNow;
            var existing = (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
                .Where(wp => wp.WroRequestId == wro.Id && !IsRemoved(wp))
                .ToList();

            var wantedIds = parcels.Select(p => p.Id).ToHashSet();

            // Bỏ kiện khỏi phiếu nháp là thao tác sửa, không phải sự cố kho: xoá hẳn dòng thay vì
            // ghi REMOVED, để lịch sử "bỏ kiện" chỉ còn các lần thật sự có vấn đề ngoài hiện trường.
            foreach (var row in existing.Where(r => !wantedIds.Contains(r.ParcelId)))
                _unitOfWork.Repository<WroParcel>().Delete(row);

            var currentIds = existing.Select(r => r.ParcelId).ToHashSet();
            foreach (var parcel in parcels.Where(p => !currentIds.Contains(p.Id)))
                await AddParcelRowAsync(wro.Id, parcel, now);

            wro.ShippingRouteId = route.Id;
            wro.OriginWarehouseId = originWarehouseId;
            wro.DestinationWarehouseId = route.DestinationWarehouseId;
            if (request.Note != null)
                wro.ExportReason = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

            _unitOfWork.Repository<WroRequest>().Update(wro);
            await _unitOfWork.SaveChangesAsync();
            return await GetDetailAsync(userId, role, wro.Id);
        }

        // ── Gửi duyệt / rút / huỷ / quyết định ─────────────────────────────────────────────

        public async Task<WroDetailDto> SubmitAsync(Guid userId, string role, Guid requestId)
        {
            EnsureWarehouseOperator(role);
            var wro = await GetRequestAsync(requestId);
            EnsureStatus(wro, WroStatuses.Draft);
            EnsureCreatorOrAdmin(wro, userId, role);
            await EnsureWarehouseScopeAsync(userId, role, wro.OriginWarehouseId);

            var rows = await ActiveRowsAsync(wro.Id);
            if (rows.Count == 0)
                throw new ArgumentException("Phiếu không còn kiện nào để gửi duyệt.");

            // Xét lại toàn bộ điều kiện ngay trước khi khoá hàng: từ lúc lập phiếu tới giờ đơn có
            // thể đã bị huỷ, bị giữ hàng, hoặc kiện đã bị tách/chuyển chỗ.
            var ctx = await ExportEligibilityContext.LoadAsync(_unitOfWork);
            var parcels = rows.Select(r => ctx.FindParcel(r.ParcelId)).Where(p => p != null).ToList();
            EnsureAllEligible(ctx, parcels, wro.OriginWarehouseId ?? Guid.Empty, wro.Id);

            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            foreach (var parcel in parcels)
            {
                var inventory = ctx.AvailableInventoryOf(parcel.Id)
                    ?? throw new ArgumentException($"Kiện {parcel.PackageCode} không có dòng tồn kho sẵn sàng để khoá.");
                inventory.Status = InventoryStatuses.Reserved;
                inventoryRepo.Update(inventory);
            }

            var now = DateTime.UtcNow;
            wro.Status = WroStatuses.PendingApproval;
            wro.SubmittedById = userId;
            wro.SubmittedAt = now;
            _unitOfWork.Repository<WroRequest>().Update(wro);

            await _unitOfWork.SaveChangesAsync();

            var warehouse = wro.OriginWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(wro.OriginWarehouseId.Value)
                : null;
            await NotifyApproversAsync(wro, warehouse, rows.Count, userId);

            return await GetDetailAsync(userId, role, wro.Id);
        }

        public async Task<WroDetailDto> WithdrawAsync(Guid userId, string role, Guid requestId)
        {
            EnsureWarehouseOperator(role);
            var wro = await GetRequestAsync(requestId);
            EnsureStatus(wro, WroStatuses.PendingApproval);
            EnsureCreatorOrAdmin(wro, userId, role);

            await UnlockInventoryAsync(wro);
            wro.Status = WroStatuses.Draft;
            wro.SubmittedById = null;
            wro.SubmittedAt = null;
            _unitOfWork.Repository<WroRequest>().Update(wro);
            await _unitOfWork.SaveChangesAsync();

            return await GetDetailAsync(userId, role, wro.Id);
        }

        public async Task<WroDetailDto> CancelAsync(Guid userId, string role, Guid requestId)
        {
            EnsureWarehouseOperator(role);
            var wro = await GetRequestAsync(requestId);
            EnsureStatus(wro, WroStatuses.Draft);
            EnsureCreatorOrAdmin(wro, userId, role);

            var rows = await ActiveRowsAsync(wro.Id);
            foreach (var row in rows)
                _unitOfWork.Repository<WroParcel>().Delete(row);

            wro.Status = WroStatuses.Cancelled;
            _unitOfWork.Repository<WroRequest>().Update(wro);
            await _unitOfWork.SaveChangesAsync();

            return await GetDetailAsync(userId, role, wro.Id);
        }

        public async Task<WroDetailDto> DecideAsync(Guid userId, string role, Guid requestId, DecideWarehouseReleaseRequest request)
        {
            var decision = request?.Decision?.Trim().ToUpperInvariant();
            if (decision != WroStatuses.Approved && decision != WroStatuses.Rejected)
                throw new ArgumentException("decision chỉ nhận APPROVED hoặc REJECTED.");

            var wro = await GetRequestAsync(requestId);
            EnsureStatus(wro, WroStatuses.PendingApproval);

            if (!wro.OriginWarehouseId.HasValue)
                throw new ArgumentException("Phiếu chưa gắn kho nguồn nên không xác định được người duyệt.");

            await WarehouseApprovalPolicy.EnsureCanDecideAsync(
                _unitOfWork, wro.OriginWarehouseId.Value, userId, role,
                wro.SubmittedById ?? wro.UserId, request?.Reason, "phiếu xuất kho");

            if (decision == WroStatuses.Rejected && string.IsNullOrWhiteSpace(request?.Reason))
                throw new ArgumentException("Từ chối phiếu xuất kho phải ghi lý do.");

            var now = DateTime.UtcNow;

            if (decision == WroStatuses.Approved)
            {
                /*
                 * BẮT BUỘC CÓ ẢNH HIỆN TRẠNG LÔ TRƯỚC KHI DUYỆT.
                 *
                 * Duyệt xong là hàng rời kho — sau đó không còn chụp lại được nữa. Ảnh phải
                 * có tại thời điểm quyết định, đúng nguyên tắc "trạng thái phải có chứng cứ".
                 *
                 * Chỉ ép ở nhánh DUYỆT: từ chối thì hàng vẫn nằm trên kệ, bắt chụp ảnh là
                 * làm khó người đang chặn một phiếu sai.
                 */
                var approvalPhotos = (await _unitOfWork.Repository<Attachment>().GetAllAsync())
                    .Count(a => a.EntityId == wro.Id
                                && string.Equals(a.EntityType, AttachmentEntityTypes.Wro, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(a.DocumentType, AttachmentDocumentTypes.WroApprovalProof, StringComparison.OrdinalIgnoreCase));

                if (approvalPhotos == 0)
                {
                    throw new ArgumentException(
                        "Phải chụp ảnh hiện trạng lô hàng trước khi duyệt phiếu xuất kho "
                        + $"(documentType = {AttachmentDocumentTypes.WroApprovalProof}, "
                        + $"entityType = {AttachmentEntityTypes.Wro}, entityId = {wro.Id}).");
                }

                // Duyệt xong là hàng được phép rời kệ, nên xét lại điều kiện lần cuối ở đây.
                var ctx = await ExportEligibilityContext.LoadAsync(_unitOfWork);
                var rows = await ActiveRowsAsync(wro.Id);
                var parcels = rows.Select(r => ctx.FindParcel(r.ParcelId)).Where(p => p != null).ToList();
                EnsureAllEligibleForApproval(ctx, parcels, wro.OriginWarehouseId.Value, wro.Id);

                wro.Status = WroStatuses.Approved;
            }
            else
            {
                await UnlockInventoryAsync(wro);

                // Phiếu bị từ chối thì các kiện phải rời phiếu hẳn, không chỉ nhả khoá hàng:
                // một kiện chỉ được nằm trong đúng một phiếu còn hiệu lực, giữ dòng cũ lại thì
                // kho không đưa kiện đó vào phiếu mới được nữa.
                foreach (var row in await ActiveRowsAsync(wro.Id))
                {
                    row.Status = WroParcelStatuses.Removed;
                    row.RemovedById = userId;
                    row.RemovedAt = now;
                    row.RemovedReason = $"Phiếu bị từ chối: {request.Reason.Trim()}";
                    _unitOfWork.Repository<WroParcel>().Update(row);
                }

                wro.Status = WroStatuses.Rejected;
                wro.RejectionReason = request.Reason.Trim();
            }

            wro.DecidedById = userId;
            wro.DecidedAt = now;
            wro.DecisionNote = string.IsNullOrWhiteSpace(request?.Reason) ? null : request.Reason.Trim();
            _unitOfWork.Repository<WroRequest>().Update(wro);
            await _unitOfWork.SaveChangesAsync();

            var target = wro.SubmittedById ?? wro.UserId;
            if (target.HasValue)
            {
                await _notificationService.NotifyUserAsync(target.Value,
                    decision == WroStatuses.Approved ? "Phiếu xuất kho đã được duyệt" : "Phiếu xuất kho bị từ chối",
                    decision == WroStatuses.Approved
                        ? $"Phiếu {wro.WroCode} đã được duyệt. Kho bốc hàng sang khu xuất rồi lập lô."
                        : $"Phiếu {wro.WroCode} bị từ chối: {wro.RejectionReason}");
            }

            return await GetDetailAsync(userId, role, wro.Id);
        }

        // ── Bốc hàng ───────────────────────────────────────────────────────────────────────

        public async Task<WroDetailDto> PickAsync(Guid userId, string role, Guid requestId, PickParcelRequest request)
        {
            EnsureWarehouseOperator(role);
            if (request == null)
                throw new ArgumentException("Thiếu dữ liệu bốc hàng.");

            var wro = await GetRequestAsync(requestId);
            if (!IsOneOf(wro.Status, WroStatuses.Approved, WroStatuses.Picking))
            {
                throw new ArgumentException(
                    $"Chỉ bốc hàng được khi phiếu đã duyệt. Phiếu {wro.WroCode} đang ở {WroStatuses.Describe(wro.Status)}.");
            }

            var warehouseId = wro.OriginWarehouseId
                ?? throw new ArgumentException("Phiếu chưa gắn kho nguồn.");
            await EnsureWarehouseScopeAsync(userId, role, warehouseId);

            var rows = await ActiveRowsAsync(wro.Id);
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = (await parcelRepo.GetAllAsync()).ToList();

            Parcel parcel = null;
            if (request.ParcelId.HasValue && request.ParcelId.Value != Guid.Empty)
                parcel = allParcels.FirstOrDefault(p => p.Id == request.ParcelId.Value);
            else if (!string.IsNullOrWhiteSpace(request.PackageCode))
                parcel = allParcels.FirstOrDefault(p => string.Equals(p.PackageCode?.Trim(), request.PackageCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (parcel == null)
                throw new NotFoundException("Không tìm thấy kiện theo mã vừa quét.");

            var row = rows.FirstOrDefault(r => r.ParcelId == parcel.Id)
                ?? throw new ArgumentException($"Kiện {parcel.PackageCode} không nằm trong phiếu {wro.WroCode}.");

            if (IsStatus(row, WroParcelStatuses.Picked))
                throw new ArgumentException($"Kiện {parcel.PackageCode} đã được bốc sang khu xuất rồi.");
            if (!IsStatus(row, WroParcelStatuses.Reserved))
                throw new ArgumentException($"Kiện {parcel.PackageCode} đang ở trạng thái {StatusNames.Quote(row.Status)}, không bốc được.");

            var sourceBinId = parcel.BinId;
            var stagingBin = await BinPlacementPolicy.ResolveBinAsync(_unitOfWork, warehouseId, request.StagingBinId, request.StagingBinCode);

            // Khu xuất chỉ nhận kiện của phiếu đã duyệt — đây là ngoại lệ duy nhất của luật
            // "hàng chỉ nằm ở khu lưu kho", và nó đi qua đúng bước này.
            var placement = await BinPlacementPolicy.EnsureCanStoreAsync(
                _unitOfWork, stagingBin, warehouseId, new[] { parcel }, null, ZoneTypes.Outbound);

            var now = DateTime.UtcNow;

            parcel.BinId = stagingBin.Id;
            parcelRepo.Update(parcel);

            stagingBin.CurrentVolume = placement.OccupiedVolumeAfter;
            _unitOfWork.Repository<Bin>().Update(stagingBin);

            if (sourceBinId.HasValue && sourceBinId.Value != stagingBin.Id)
            {
                var sourceBin = await _unitOfWork.Repository<Bin>().GetByIdAsync(sourceBinId.Value);
                if (sourceBin != null)
                {
                    sourceBin.CurrentVolume = await BinPlacementPolicy.VolumeAfterLeavingAsync(
                        _unitOfWork, sourceBin.Id, new[] { parcel.Id });
                    _unitOfWork.Repository<Bin>().Update(sourceBin);
                }
            }

            var inventory = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                .FirstOrDefault(i => i.ParcelId == parcel.Id
                                     && string.Equals(i.Status, InventoryStatuses.Reserved, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Kiện {parcel.PackageCode} không có dòng tồn đang khoá theo phiếu này.");

            inventory.Status = InventoryStatuses.Picked;
            inventory.BinId = stagingBin.Id;
            _unitOfWork.Repository<Inventory>().Update(inventory);

            await _unitOfWork.Repository<InventoryMovement>().AddAsync(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                InventoryId = inventory.Id,
                ParcelId = parcel.Id,
                FromBinId = sourceBinId ?? stagingBin.Id,
                ToBinId = stagingBin.Id,
                MovementType = "RELOCATION",
                Reason = $"Bốc hàng sang khu xuất theo phiếu {wro.WroCode}",
                Quantity = inventory.Quantity > 0 ? inventory.Quantity : 1,
                MovedById = userId,
                MovedAt = now
            });

            row.Status = WroParcelStatuses.Picked;
            row.SourceBinId = sourceBinId;
            row.StagingBinId = stagingBin.Id;
            row.PickedById = userId;
            row.PickedAt = now;
            _unitOfWork.Repository<WroParcel>().Update(row);

            var remaining = rows.Count(r => r.Id != row.Id && IsStatus(r, WroParcelStatuses.Reserved));
            wro.Status = remaining == 0 ? WroStatuses.Ready : WroStatuses.Picking;
            _unitOfWork.Repository<WroRequest>().Update(wro);

            await _unitOfWork.SaveChangesAsync();

            if (remaining == 0)
                await NotifySalesReadyAsync(wro, rows);

            return await GetDetailAsync(userId, role, wro.Id);
        }

        public async Task<WroDetailDto> RemoveParcelAsync(
            Guid userId, string role, Guid requestId, Guid parcelId, RemoveWroParcelRequest request)
        {
            EnsureWarehouseOperator(role);
            if (string.IsNullOrWhiteSpace(request?.Reason))
                throw new ArgumentException("Bỏ kiện khỏi phiếu phải ghi lý do.");

            var wro = await GetRequestAsync(requestId);
            if (IsOneOf(wro.Status, WroStatuses.InShipment, WroStatuses.HandedOver, WroStatuses.Cancelled, WroStatuses.Rejected))
                throw new ArgumentException($"Phiếu {wro.WroCode} đang ở {WroStatuses.Describe(wro.Status)}, không bỏ kiện được.");

            var warehouseId = wro.OriginWarehouseId ?? throw new ArgumentException("Phiếu chưa gắn kho nguồn.");
            await EnsureWarehouseScopeAsync(userId, role, warehouseId);

            var rows = await ActiveRowsAsync(wro.Id);
            var row = rows.FirstOrDefault(r => r.ParcelId == parcelId)
                ?? throw new NotFoundException("Kiện không nằm trong phiếu này.");

            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(parcelId)
                ?? throw new NotFoundException("Không tìm thấy kiện.");

            // Ảnh hiện trường là thứ duy nhất chứng minh vì sao kiện không đi được — bắt buộc có
            // trước khi hàng được nhả ra, không cho bổ sung sau.
            var documentTypes = await _attachmentService.DocumentTypesOfAsync(AttachmentEntityTypes.WroParcel, row.Id);
            if (!documentTypes.Contains(AttachmentDocumentTypes.PickingIssue))
            {
                throw new ArgumentException(
                    $"Phải đính kèm ảnh/biên bản (documentType = {AttachmentDocumentTypes.PickingIssue}, "
                    + $"entityType = {AttachmentEntityTypes.WroParcel}, entityId = {row.Id}) trước khi bỏ kiện khỏi phiếu.");
            }

            var now = DateTime.UtcNow;
            var wasPicked = IsStatus(row, WroParcelStatuses.Picked);

            if (wasPicked)
            {
                // Kiện đã nằm ở khu xuất: phải quét ô khu lưu kho để trả hàng về đúng chỗ, đi qua
                // đúng luật khu như mọi lần xếp kệ khác.
                if (request.ReturnBinId == null && string.IsNullOrWhiteSpace(request.ReturnBinCode))
                    throw new ArgumentException("Kiện đã bốc sang khu xuất: phải quét ô kệ khu lưu kho (returnBinCode) để trả hàng về.");

                var returnBin = await BinPlacementPolicy.ResolveBinAsync(_unitOfWork, warehouseId, request.ReturnBinId, request.ReturnBinCode);
                var placement = await BinPlacementPolicy.EnsureCanStoreAsync(_unitOfWork, returnBin, warehouseId, new[] { parcel });

                var stagingBinId = parcel.BinId;
                parcel.BinId = returnBin.Id;
                _unitOfWork.Repository<Parcel>().Update(parcel);

                returnBin.CurrentVolume = placement.OccupiedVolumeAfter;
                _unitOfWork.Repository<Bin>().Update(returnBin);

                if (stagingBinId.HasValue && stagingBinId.Value != returnBin.Id)
                {
                    var stagingBin = await _unitOfWork.Repository<Bin>().GetByIdAsync(stagingBinId.Value);
                    if (stagingBin != null)
                    {
                        stagingBin.CurrentVolume = await BinPlacementPolicy.VolumeAfterLeavingAsync(
                            _unitOfWork, stagingBin.Id, new[] { parcel.Id });
                        _unitOfWork.Repository<Bin>().Update(stagingBin);
                    }
                }

                var pickedInventory = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                    .FirstOrDefault(i => i.ParcelId == parcel.Id
                                         && string.Equals(i.Status, InventoryStatuses.Picked, StringComparison.OrdinalIgnoreCase));
                if (pickedInventory != null)
                {
                    pickedInventory.Status = InventoryStatuses.Available;
                    pickedInventory.BinId = returnBin.Id;
                    _unitOfWork.Repository<Inventory>().Update(pickedInventory);

                    await _unitOfWork.Repository<InventoryMovement>().AddAsync(new InventoryMovement
                    {
                        Id = Guid.NewGuid(),
                        InventoryId = pickedInventory.Id,
                        ParcelId = parcel.Id,
                        FromBinId = stagingBinId ?? returnBin.Id,
                        ToBinId = returnBin.Id,
                        MovementType = "RELOCATION",
                        Reason = $"Trả kiện về khu lưu kho, bỏ khỏi phiếu {wro.WroCode}: {request.Reason.Trim()}",
                        Quantity = pickedInventory.Quantity > 0 ? pickedInventory.Quantity : 1,
                        MovedById = userId,
                        MovedAt = now
                    });
                }
            }
            else
            {
                var reserved = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                    .FirstOrDefault(i => i.ParcelId == parcel.Id
                                         && string.Equals(i.Status, InventoryStatuses.Reserved, StringComparison.OrdinalIgnoreCase));
                if (reserved != null)
                {
                    reserved.Status = InventoryStatuses.Available;
                    _unitOfWork.Repository<Inventory>().Update(reserved);
                }
            }

            row.Status = WroParcelStatuses.Removed;
            row.RemovedById = userId;
            row.RemovedAt = now;
            row.RemovedReason = request.Reason.Trim();
            _unitOfWork.Repository<WroParcel>().Update(row);

            var remainingRows = rows.Where(r => r.Id != row.Id).ToList();
            if (remainingRows.Count == 0)
            {
                wro.Status = WroStatuses.Cancelled;
            }
            else if (IsOneOf(wro.Status, WroStatuses.Picking, WroStatuses.Ready))
            {
                wro.Status = remainingRows.All(r => IsStatus(r, WroParcelStatuses.Picked))
                    ? WroStatuses.Ready
                    : WroStatuses.Picking;
            }

            _unitOfWork.Repository<WroRequest>().Update(wro);
            await _unitOfWork.SaveChangesAsync();

            await NotifyParcelRemovedAsync(wro, parcel, request.Reason.Trim(), userId);

            return await GetDetailAsync(userId, role, wro.Id);
        }

        // ── Tách phiếu ─────────────────────────────────────────────────────────────────────

        public async Task<WroDetailDto> SplitAsync(Guid userId, string role, Guid requestId, SplitWarehouseReleaseRequest request)
        {
            EnsureWarehouseOperator(role);
            if (request?.ParcelIds == null || request.ParcelIds.Count == 0)
                throw new ArgumentException("Chọn ít nhất một kiện để tách sang phiếu con.");

            var wro = await GetRequestAsync(requestId);
            if (IsOneOf(wro.Status, WroStatuses.InShipment, WroStatuses.HandedOver, WroStatuses.Cancelled, WroStatuses.Rejected))
                throw new ArgumentException($"Phiếu {wro.WroCode} đang ở {WroStatuses.Describe(wro.Status)}, không tách được.");

            await EnsureWarehouseScopeAsync(userId, role, wro.OriginWarehouseId);

            var rows = await ActiveRowsAsync(wro.Id);
            var moving = rows.Where(r => request.ParcelIds.Contains(r.ParcelId)).ToList();
            if (moving.Count == 0)
                throw new ArgumentException("Không có kiện nào trong danh sách thuộc phiếu này.");
            if (moving.Count == rows.Count)
                throw new ArgumentException("Không tách được toàn bộ kiện — phiếu gốc phải còn ít nhất một kiện.");

            var child = new WroRequest
            {
                Id = Guid.NewGuid(),
                WroCode = await GenerateWroCodeAsync(),
                // Phiếu con giữ nguyên trạng thái và người duyệt: hàng đã khoá và đã được duyệt
                // rồi, tách chỉ là chia theo chuyến xe nên không bắt duyệt lại.
                Status = wro.Status,
                ShippingRouteId = wro.ShippingRouteId,
                OriginWarehouseId = wro.OriginWarehouseId,
                DestinationWarehouseId = wro.DestinationWarehouseId,
                UserId = wro.UserId,
                CreatedAt = DateTime.UtcNow,
                SubmittedById = wro.SubmittedById,
                SubmittedAt = wro.SubmittedAt,
                DecidedById = wro.DecidedById,
                DecidedAt = wro.DecidedAt,
                DecisionNote = wro.DecisionNote,
                SplitFromId = wro.Id,
                ExportReason = $"Tách từ phiếu {wro.WroCode}",
                IsLegacy = false
            };

            await _unitOfWork.Repository<WroRequest>().AddAsync(child);

            foreach (var row in moving)
            {
                row.WroRequestId = child.Id;
                _unitOfWork.Repository<WroParcel>().Update(row);
            }

            var parentRemaining = rows.Where(r => !moving.Contains(r)).ToList();
            wro.Status = ResolveStatusAfterMove(wro.Status, parentRemaining);
            child.Status = ResolveStatusAfterMove(child.Status, moving);

            _unitOfWork.Repository<WroRequest>().Update(wro);
            await _unitOfWork.SaveChangesAsync();

            return await GetDetailAsync(userId, role, child.Id);
        }

        /// <summary>Sau khi chia kiện, mỗi phiếu tự tính lại đã bốc xong hay chưa.</summary>
        private static string ResolveStatusAfterMove(string status, List<WroParcel> rows)
        {
            if (!IsOneOf(status, WroStatuses.Approved, WroStatuses.Picking, WroStatuses.Ready)) return status;
            if (rows.Count == 0) return status;

            if (rows.All(r => IsStatus(r, WroParcelStatuses.Picked))) return WroStatuses.Ready;
            return rows.Any(r => IsStatus(r, WroParcelStatuses.Picked)) ? WroStatuses.Picking : WroStatuses.Approved;
        }

        // ── Dùng chung ─────────────────────────────────────────────────────────────────────

        private async Task<WroRequest> GetRequestAsync(Guid requestId) =>
            await _unitOfWork.Repository<WroRequest>().GetByIdAsync(requestId)
            ?? throw new NotFoundException($"Không tìm thấy phiếu xuất kho {requestId}.");

        private async Task<List<WroParcel>> ActiveRowsAsync(Guid requestId) =>
            (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
            .Where(wp => wp.WroRequestId == requestId && !IsRemoved(wp))
            .ToList();

        private async Task AddParcelRowAsync(Guid requestId, Parcel parcel, DateTime now)
        {
            await _unitOfWork.Repository<WroParcel>().AddAsync(new WroParcel
            {
                Id = Guid.NewGuid(),
                WroRequestId = requestId,
                ParcelId = parcel.Id,
                Status = WroParcelStatuses.Reserved,
                SourceBinId = parcel.BinId,
                CreatedAt = now
            });
        }

        /// <summary>Nhả hàng đang khoá của phiếu về lại AVAILABLE (rút phiếu hoặc bị từ chối).</summary>
        private async Task UnlockInventoryAsync(WroRequest wro)
        {
            var parcelIds = (await ActiveRowsAsync(wro.Id)).Select(r => r.ParcelId).ToHashSet();
            if (parcelIds.Count == 0) return;

            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var reserved = (await inventoryRepo.GetAllAsync())
                .Where(i => parcelIds.Contains(i.ParcelId)
                            && string.Equals(i.Status, InventoryStatuses.Reserved, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var inventory in reserved)
            {
                inventory.Status = InventoryStatuses.Available;
                inventoryRepo.Update(inventory);
            }
        }

        private static List<Parcel> ResolveParcels(ExportEligibilityContext ctx, List<Guid> parcelIds)
        {
            var parcels = new List<Parcel>();
            foreach (var id in parcelIds.Distinct())
            {
                var parcel = ctx.FindParcel(id) ?? throw new NotFoundException($"Không tìm thấy kiện {id}.");
                parcels.Add(parcel);
            }
            return parcels;
        }

        private static void EnsureAllEligible(
            ExportEligibilityContext ctx, List<Parcel> parcels, Guid originWarehouseId, Guid? ignoreWroId)
        {
            var problems = new List<string>();
            foreach (var parcel in parcels)
            {
                var candidate = ctx.Evaluate(parcel, originWarehouseId, ignoreWroId);
                if (candidate.IsEligible) continue;

                problems.Add($"{parcel.PackageCode}: "
                    + string.Join("; ", candidate.Reasons.Select(ExportBlockReasons.Describe)));
            }

            if (problems.Count > 0)
                throw new ArgumentException("Có kiện chưa đủ điều kiện xuất — " + string.Join(" | ", problems));
        }

        /// <summary>
        /// Xét lại lúc duyệt: hàng lúc này đã bị khoá (tồn RESERVED) nên hai lý do "chưa có tồn
        /// sẵn sàng" và "đã nằm trong phiếu khác" không còn đúng với chính phiếu đang duyệt.
        /// </summary>
        private static void EnsureAllEligibleForApproval(
            ExportEligibilityContext ctx, List<Parcel> parcels, Guid originWarehouseId, Guid wroId)
        {
            var ignored = new[] { ExportBlockReasons.InventoryNotAvailable, ExportBlockReasons.AlreadyInExport };
            var problems = new List<string>();

            foreach (var parcel in parcels)
            {
                var candidate = ctx.Evaluate(parcel, originWarehouseId, wroId);
                var reasons = candidate.Reasons.Where(r => !ignored.Contains(r)).ToList();
                if (reasons.Count == 0) continue;

                problems.Add($"{parcel.PackageCode}: " + string.Join("; ", reasons.Select(ExportBlockReasons.Describe)));
            }

            if (problems.Count > 0)
                throw new ArgumentException("Không duyệt được, có kiện đã hết điều kiện xuất — " + string.Join(" | ", problems));
        }

        private static void EnsureWarehouseOperator(string role)
        {
            if (!StaffRoleHelper.CanOperateWarehouse(role))
                throw new UnauthorizedAccessException("Chỉ nhân viên kho (hoặc Admin) mới thao tác được phiếu xuất kho.");
        }

        /// <summary>
        /// Nhân viên kho chỉ thao tác ở kho mình phụ trách — kho được Admin gán, hoặc (chưa gán) kho
        /// thuộc vùng của mình: người đứng ở Quảng Châu không lập phiếu cho hàng ở Seoul được.
        /// Quy tắc nằm ở <see cref="WarehouseAccessPolicy"/>.
        /// </summary>
        private Task EnsureWarehouseScopeAsync(Guid userId, string role, Guid? warehouseId) =>
            WarehouseAccessPolicy.EnsureCanOperateAsync(
                _unitOfWork, userId, role, warehouseId,
                notFoundMessage: "Không tìm thấy kho nguồn của phiếu.",
                actionLabel: "tại kho",
                missingRegionHint: " Nhờ Admin gán kho hoặc cập nhật vùng cho tài khoản.");

        private static void EnsureCreatorOrAdmin(WroRequest wro, Guid userId, string role)
        {
            if (WarehouseApprovalPolicy.IsAdmin(role)) return;
            if (wro.UserId.HasValue && wro.UserId.Value == userId) return;
            throw new UnauthorizedAccessException($"Chỉ người lập phiếu {wro.WroCode} hoặc Admin mới sửa được phiếu.");
        }

        private static void EnsureStatus(WroRequest wro, string expected)
        {
            if (!string.Equals(wro.Status?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Phiếu {wro.WroCode} đang ở {WroStatuses.Describe(wro.Status)}, thao tác này chỉ dùng khi phiếu ở {WroStatuses.Describe(expected)}.");
            }
        }

        private async Task<string> GenerateWroCodeAsync()
        {
            var prefix = $"WRO-{DateTime.UtcNow:yyyyMMdd}-";
            var existing = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                .Where(w => w.WroCode != null && w.WroCode.StartsWith(prefix))
                .Select(w => w.WroCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Số chạy theo ngày thay vì số ngẫu nhiên: mã cũ dùng Random nên thỉnh thoảng trùng,
            // và kho không đoán được thứ tự phiếu trong ngày.
            for (var i = 1; i <= 9999; i++)
            {
                var code = prefix + i.ToString("D4");
                if (!existing.Contains(code)) return code;
            }

            return prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        }

        private static bool IsRemoved(WroParcel row) =>
            string.Equals(row.Status?.Trim(), WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase);

        private static bool IsStatus(WroParcel row, string status) =>
            string.Equals(row.Status?.Trim(), status, StringComparison.OrdinalIgnoreCase);

        private static bool IsOneOf(string status, params string[] candidates) =>
            candidates.Any(c => string.Equals(status?.Trim(), c, StringComparison.OrdinalIgnoreCase));

        private static bool Contains(string value, string keyword) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        // ── Thông báo ──────────────────────────────────────────────────────────────────────

        private async Task NotifyApproversAsync(WroRequest wro, Warehouse warehouse, int parcelCount, Guid excludeUserId)
        {
            var title = "Phiếu xuất kho chờ duyệt";
            var content = $"Phiếu {wro.WroCode} ({parcelCount} kiện) tại kho {warehouse?.WarehouseName} chờ quản lý kho duyệt.";

            if (warehouse?.ManagerId is Guid managerId && managerId != excludeUserId)
            {
                await _notificationService.NotifyUserAsync(managerId, title, content);
                return;
            }

            await _notificationService.NotifyRolesAsync(
                new[] { "OperationsManager", "Operations", "Manager", "Admin" }, title, content, excludeUserId);
        }

        /// <summary>Phiếu bốc xong: Sale của các đơn trong phiếu cần biết để theo dõi chuyến.</summary>
        private async Task NotifySalesReadyAsync(WroRequest wro, List<WroParcel> rows)
        {
            var saleIds = await ResolveSaleIdsAsync(rows.Select(r => r.ParcelId).ToHashSet());
            var title = "Phiếu xuất kho đã bốc xong";
            var content = $"Phiếu {wro.WroCode} đã bốc đủ hàng sang khu xuất, chờ lập lô vận chuyển.";

            if (saleIds.Count > 0)
            {
                await _notificationService.NotifyUsersAsync(saleIds, title, content);
                return;
            }

            await _notificationService.NotifyRolesAsync(new[] { "Sale", "Sales" }, title, content, null);
        }

        private async Task NotifyParcelRemovedAsync(WroRequest wro, Parcel parcel, string reason, Guid actorId)
        {
            var title = "Kiện bị bỏ khỏi phiếu xuất kho";
            var content = $"Kiện {parcel.PackageCode} bị bỏ khỏi phiếu {wro.WroCode}: {reason}. Hàng đã trả về khu lưu kho.";

            var saleIds = await ResolveSaleIdsAsync(new HashSet<Guid> { parcel.Id });
            if (saleIds.Count > 0)
                await _notificationService.NotifyUsersAsync(saleIds, title, content);

            var warehouse = wro.OriginWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(wro.OriginWarehouseId.Value)
                : null;

            if (warehouse?.ManagerId is Guid managerId && managerId != actorId)
                await _notificationService.NotifyUserAsync(managerId, title, content);
            else
                await _notificationService.NotifyRolesAsync(new[] { "OperationsManager", "Operations", "Admin" }, title, content, actorId);
        }

        /// <summary>Sale phụ trách đơn = người đã lập báo giá cho đơn đó.</summary>
        private async Task<List<Guid>> ResolveSaleIdsAsync(HashSet<Guid> parcelIds)
        {
            var orderIds = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id) && p.ConsignmentId.HasValue)
                .Select(p => p.ConsignmentId!.Value)
                .ToHashSet();
            if (orderIds.Count == 0) return new List<Guid>();

            return (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId.HasValue && orderIds.Contains(q.OrderId.Value) && q.CreatedById.HasValue)
                .Select(q => q.CreatedById!.Value)
                .Distinct()
                .ToList();
        }

        // ── Dựng DTO ───────────────────────────────────────────────────────────────────────

        private sealed class WroLookup
        {
            public List<WroRequest> Requests { get; init; }
            public List<WroParcel> Parcels { get; init; }
            public Dictionary<Guid, Parcel> ParcelsById { get; init; }
            public Dictionary<Guid, Order> Orders { get; init; }
            public Dictionary<Guid, OrderItem> OrderItems { get; init; }
            public Dictionary<Guid, Bin> Bins { get; init; }
            public Dictionary<Guid, Warehouse> Warehouses { get; init; }
            public Dictionary<Guid, ShippingRoute> Routes { get; init; }
            public Dictionary<Guid, string> UserNames { get; init; }
            public Dictionary<Guid, string> CustomerNames { get; init; }
            public Dictionary<Guid, (Guid ShipmentId, string ShipmentCode)> ShipmentByWro { get; init; }
            public Dictionary<Guid, OrderPurchaseLabel> PurchaseLabels { get; init; }

            public OrderPurchaseLabel LabelOfParcel(Parcel parcel) =>
                OrderPurchaseLabels.Of(PurchaseLabels, parcel?.ConsignmentId);

            public Parcel ParcelOf(Guid parcelId) => ParcelsById.TryGetValue(parcelId, out var p) ? p : null;
        }

        private async Task<WroLookup> LoadLookupAsync()
        {
            var shipments = (await _unitOfWork.Repository<Shipment>().GetAllAsync()).ToDictionary(s => s.Id);
            var shipmentByWro = new Dictionary<Guid, (Guid, string)>();
            foreach (var link in await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
            {
                if (!shipments.TryGetValue(link.ShipmentId, out var shipment)) continue;
                shipmentByWro[link.WroRequestId] = (shipment.Id, shipment.ShipmentCode);
            }

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);

            return new WroLookup
            {
                Requests = (await _unitOfWork.Repository<WroRequest>().GetAllAsync()).ToList(),
                Parcels = (await _unitOfWork.Repository<WroParcel>().GetAllAsync()).ToList(),
                ParcelsById = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id),
                Orders = orders,
                PurchaseLabels = await OrderPurchaseLabels.LoadAsync(_unitOfWork, orders.Values),
                OrderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync()).ToDictionary(i => i.Id),
                Bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id),
                Warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id),
                Routes = (await _unitOfWork.Repository<ShippingRoute>().GetAllAsync()).ToDictionary(r => r.Id),
                UserNames = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id, u => u.FullName),
                CustomerNames = await LoadCustomerNamesAsync(),
                ShipmentByWro = shipmentByWro
            };
        }

        private async Task<Dictionary<Guid, string>> LoadCustomerNamesAsync()
        {
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id, u => u.FullName);
            return (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .ToDictionary(
                    c => c.Id,
                    c => c.UserId.HasValue && users.TryGetValue(c.UserId.Value, out var name) ? name : null);
        }

        private static string CustomerNameOf(Dictionary<Guid, string> names, Order order) =>
            order?.CustomerId != null && names.TryGetValue(order.CustomerId.Value, out var name) ? name : null;

        private WroListItemDto BuildListItem(WroRequest r, WroLookup lookup)
        {
            var rows = lookup.Parcels.Where(wp => wp.WroRequestId == r.Id && !IsRemoved(wp)).ToList();
            var parcels = rows.Select(wp => lookup.ParcelOf(wp.ParcelId)).Where(p => p != null).ToList();

            lookup.ShipmentByWro.TryGetValue(r.Id, out var shipment);

            return new WroListItemDto
            {
                Id = r.Id,
                WroCode = r.WroCode,
                Status = r.Status,
                StatusText = WroStatuses.Describe(r.Status),
                OriginWarehouseId = r.OriginWarehouseId,
                OriginWarehouseName = NameOfWarehouse(lookup, r.OriginWarehouseId),
                DestinationWarehouseId = r.DestinationWarehouseId,
                DestinationWarehouseName = NameOfWarehouse(lookup, r.DestinationWarehouseId),
                ShippingRouteId = r.ShippingRouteId,
                ShippingRouteCode = r.ShippingRouteId.HasValue && lookup.Routes.TryGetValue(r.ShippingRouteId.Value, out var route)
                    ? route.RouteCode
                    : null,
                ParcelCount = rows.Count,
                PickedCount = rows.Count(wp => IsStatus(wp, WroParcelStatuses.Picked) || IsStatus(wp, WroParcelStatuses.HandedOver)),
                PurchaseParcelCount = parcels.Count(p => lookup.LabelOfParcel(p).IsPurchase),
                TotalWeight = parcels.Sum(p => p.ActualWeight ?? 0m),
                CreatedAt = r.CreatedAt,
                CreatedByName = NameOfUser(lookup, r.UserId),
                SubmittedAt = r.SubmittedAt,
                DecidedAt = r.DecidedAt,
                DecidedByName = NameOfUser(lookup, r.DecidedById),
                DecisionNote = r.DecisionNote ?? r.RejectionReason,
                ShipmentId = shipment.ShipmentId == Guid.Empty ? null : shipment.ShipmentId,
                ShipmentCode = shipment.ShipmentCode,
                IsLegacy = r.IsLegacy
            };
        }

        private async Task<WroDetailDto> BuildDetailAsync(WroRequest r, WroLookup lookup, Guid userId, string role)
        {
            var item = BuildListItem(r, lookup);
            var detail = new WroDetailDto
            {
                Id = item.Id,
                WroCode = item.WroCode,
                Status = item.Status,
                StatusText = item.StatusText,
                OriginWarehouseId = item.OriginWarehouseId,
                OriginWarehouseName = item.OriginWarehouseName,
                DestinationWarehouseId = item.DestinationWarehouseId,
                DestinationWarehouseName = item.DestinationWarehouseName,
                ShippingRouteId = item.ShippingRouteId,
                ShippingRouteCode = item.ShippingRouteCode,
                ParcelCount = item.ParcelCount,
                PickedCount = item.PickedCount,
                PurchaseParcelCount = item.PurchaseParcelCount,
                TotalWeight = item.TotalWeight,
                CreatedAt = item.CreatedAt,
                CreatedByName = item.CreatedByName,
                SubmittedAt = item.SubmittedAt,
                DecidedAt = item.DecidedAt,
                DecidedByName = item.DecidedByName,
                DecisionNote = item.DecisionNote,
                ShipmentId = item.ShipmentId,
                ShipmentCode = item.ShipmentCode,
                IsLegacy = item.IsLegacy,
                Note = r.ExportReason,
                SplitFromId = r.SplitFromId,
                SplitFromCode = r.SplitFromId.HasValue
                    ? lookup.Requests.FirstOrDefault(x => x.Id == r.SplitFromId.Value)?.WroCode
                    : null
            };

            foreach (var row in lookup.Parcels.Where(wp => wp.WroRequestId == r.Id).OrderBy(wp => wp.CreatedAt))
            {
                var parcel = lookup.ParcelOf(row.ParcelId);
                var order = parcel?.ConsignmentId != null && lookup.Orders.TryGetValue(parcel.ConsignmentId.Value, out var o) ? o : null;
                var orderItem = parcel?.OrderItemId != null && lookup.OrderItems.TryGetValue(parcel.OrderItemId.Value, out var oi) ? oi : null;
                var label = OrderPurchaseLabels.Of(lookup.PurchaseLabels, order?.Id);

                detail.Parcels.Add(new WroParcelDto
                {
                    Id = row.Id,
                    ParcelId = row.ParcelId,
                    PackageCode = parcel?.PackageCode,
                    Status = row.Status,
                    OrderId = order?.Id,
                    ConsignmentCode = order?.ConsignmentCode,
                    CustomerName = CustomerNameOf(lookup.CustomerNames, order),
                    ProductName = orderItem?.ProductName,
                    Weight = parcel?.ActualWeight,
                    Length = parcel?.Length,
                    Width = parcel?.Width,
                    Height = parcel?.Height,
                    SourceBinCode = BinCode(lookup, row.SourceBinId),
                    StagingBinCode = BinCode(lookup, row.StagingBinId),
                    PickedAt = row.PickedAt,
                    PickedByName = NameOfUser(lookup, row.PickedById),
                    RemovedAt = row.RemovedAt,
                    RemovedReason = row.RemovedReason,
                    IsPurchase = label.IsPurchase,
                    OrderType = label.OrderType,
                    PurchaseCode = label.PurchaseCode
                });
            }

            if (StaffRoleHelper.IsInternalStaff(role))
                detail.Attachments = await _attachmentService.ListAsync(userId, role, AttachmentEntityTypes.Wro, r.Id);

            return detail;
        }

        private static string BinCode(WroLookup lookup, Guid? binId) =>
            binId.HasValue && lookup.Bins.TryGetValue(binId.Value, out var bin) ? bin.BinCode : null;

        private static string NameOfUser(WroLookup lookup, Guid? userId) =>
            userId.HasValue && lookup.UserNames.TryGetValue(userId.Value, out var name) ? name : null;

        private static string NameOfWarehouse(WroLookup lookup, Guid? warehouseId) =>
            warehouseId.HasValue && lookup.Warehouses.TryGetValue(warehouseId.Value, out var w) ? w.WarehouseName : null;
    }
}
