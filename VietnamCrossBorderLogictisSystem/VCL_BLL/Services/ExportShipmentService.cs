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
    /// Lô vận chuyển quốc tế: gom các phiếu xuất kho đã bốc xong rồi bàn giao một lần cho hãng
    /// vận chuyển.
    /// <para>
    /// Bàn giao là mốc hàng RỜI KHO THẬT nên mọi tác động lên tồn kho dồn vào đúng đây: trước kia
    /// việc này nằm ở từng phiếu, mà xe chỉ tới lấy một lần cho cả lô nên chốt theo phiếu vừa lặp
    /// vừa dễ sót.
    /// </para>
    /// </summary>
    public class ExportShipmentService : IExportShipmentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;
        private readonly IAttachmentService _attachmentService;
        private readonly IInternationalShipmentService _shipmentQueries;

        public ExportShipmentService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            IAttachmentService attachmentService,
            IInternationalShipmentService shipmentQueries)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _attachmentService = attachmentService;
            _shipmentQueries = shipmentQueries;
        }

        // ── Gom lô ─────────────────────────────────────────────────────────────────────────

        public async Task<List<ReadyWroForShipmentDto>> GetReadyWroRequestsAsync(
            Guid userId, string role, Guid? originWarehouseId, string? search)
        {
            var requests = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                .Where(w => string.Equals(w.Status?.Trim(), WroStatuses.Ready, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (originWarehouseId.HasValue)
                requests = requests.Where(w => w.OriginWarehouseId == originWarehouseId.Value).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                requests = requests
                    .Where(w => (w.WroCode ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var rows = (await _unitOfWork.Repository<WroParcel>().GetAllAsync()).ToList();
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id);
            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id);
            var routes = (await _unitOfWork.Repository<ShippingRoute>().GetAllAsync()).ToDictionary(r => r.Id);

            var requestIds = requests.Select(w => w.Id).ToHashSet();
            var orderIds = rows
                .Where(r => requestIds.Contains(r.WroRequestId) && IsPickedOrReserved(r))
                .Select(r => parcels.TryGetValue(r.ParcelId, out var p) ? p.ConsignmentId : null)
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var orders = orderIds.Count == 0
                ? new List<Order>()
                : (await _unitOfWork.Repository<Order>().FindAsync(o => orderIds.Contains(o.Id))).ToList();
            var purchaseLabels = await OrderPurchaseLabels.LoadAsync(_unitOfWork, orders);

            return requests.Select(w =>
            {
                var active = rows.Where(r => r.WroRequestId == w.Id && IsPickedOrReserved(r)).ToList();
                return new ReadyWroForShipmentDto
                {
                    WroRequestId = w.Id,
                    WroCode = w.WroCode,
                    Status = w.Status,
                    OriginWarehouseId = w.OriginWarehouseId,
                    OriginWarehouseName = NameOf(warehouses, w.OriginWarehouseId),
                    DestinationWarehouseId = w.DestinationWarehouseId,
                    DestinationWarehouseName = NameOf(warehouses, w.DestinationWarehouseId),
                    ShippingRouteId = w.ShippingRouteId,
                    ShippingRouteCode = w.ShippingRouteId.HasValue && routes.TryGetValue(w.ShippingRouteId.Value, out var route)
                        ? route.RouteCode
                        : null,
                    ParcelCount = active.Count,
                    PurchaseParcelCount = active.Count(r =>
                        parcels.TryGetValue(r.ParcelId, out var p)
                        && OrderPurchaseLabels.Of(purchaseLabels, p.ConsignmentId).IsPurchase),
                    TotalWeight = active
                        .Select(r => parcels.TryGetValue(r.ParcelId, out var p) ? p.ActualWeight ?? 0m : 0m)
                        .Sum(),
                    DecidedAt = w.DecidedAt,
                    IsLegacy = w.IsLegacy
                };
            })
            .OrderBy(dto => dto.WroCode)
            .ToList();
        }

        public async Task<InternationalShipmentResponseDto> CreateAsync(Guid userId, string role, CreateExportShipmentRequest request)
        {
            EnsureWarehouseOperator(role);
            if (request?.WroRequestIds == null || request.WroRequestIds.Count == 0)
                throw new ArgumentException("Lô phải có ít nhất một phiếu xuất kho.");

            var requests = await LoadRequestsAsync(request.WroRequestIds);
            foreach (var wro in requests) EnsureReadyForShipment(wro);

            var originIds = requests.Select(w => w.OriginWarehouseId).Distinct().ToList();
            var destinationIds = requests.Select(w => w.DestinationWarehouseId).Distinct().ToList();
            if (originIds.Count > 1 || destinationIds.Count > 1)
                throw new ArgumentException("Các phiếu trong cùng một lô phải cùng kho nguồn và cùng kho đích.");

            var originWarehouseId = originIds[0] ?? throw new ArgumentException("Phiếu chưa gắn kho nguồn.");
            var destinationWarehouseId = destinationIds[0] ?? throw new ArgumentException("Phiếu chưa gắn kho đích.");
            await EnsureWarehouseScopeAsync(userId, role, originWarehouseId);

            var now = DateTime.UtcNow;
            var shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                ShipmentCode = await GenerateShipmentCodeAsync(),
                ShipmentStage = "INTERNATIONAL",
                ShipmentType = "INTERNATIONAL",
                Status = ExportShipmentStatuses.Draft,
                OriginWarehouseId = originWarehouseId,
                DestinationWarehouseId = destinationWarehouseId,
                ShippingMethod = string.IsNullOrWhiteSpace(request.ShippingMethod) ? null : request.ShippingMethod.Trim(),
                CarrierId = request.CarrierId,
                CreatedById = userId
            };

            await _unitOfWork.Repository<Shipment>().AddAsync(shipment);
            await AttachRequestsAsync(shipment, requests, now);
            await _unitOfWork.SaveChangesAsync();

            return await _shipmentQueries.GetByIdAsync(shipment.Id);
        }

        public async Task<InternationalShipmentResponseDto> AddWroRequestsAsync(
            Guid userId, string role, Guid shipmentId, AddWroRequestsToShipmentRequest request)
        {
            EnsureWarehouseOperator(role);
            if (request?.WroRequestIds == null || request.WroRequestIds.Count == 0)
                throw new ArgumentException("Chưa chọn phiếu nào để thêm vào lô.");

            var shipment = await GetShipmentAsync(shipmentId);
            EnsureDraft(shipment);
            await EnsureWarehouseScopeAsync(userId, role, shipment.OriginWarehouseId);

            var requests = await LoadRequestsAsync(request.WroRequestIds);
            foreach (var wro in requests)
            {
                EnsureReadyForShipment(wro);
                if (wro.OriginWarehouseId != shipment.OriginWarehouseId || wro.DestinationWarehouseId != shipment.DestinationWarehouseId)
                    throw new ArgumentException($"Phiếu {wro.WroCode} khác kho nguồn/kho đích của lô {shipment.ShipmentCode}.");
            }

            await AttachRequestsAsync(shipment, requests, DateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync();

            return await _shipmentQueries.GetByIdAsync(shipment.Id);
        }

        public async Task<InternationalShipmentResponseDto> RemoveWroRequestAsync(
            Guid userId, string role, Guid shipmentId, Guid wroRequestId)
        {
            EnsureWarehouseOperator(role);
            var shipment = await GetShipmentAsync(shipmentId);
            EnsureDraft(shipment);
            await EnsureWarehouseScopeAsync(userId, role, shipment.OriginWarehouseId);

            var link = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                .FirstOrDefault(l => l.ShipmentId == shipment.Id && l.WroRequestId == wroRequestId)
                ?? throw new NotFoundException("Phiếu này không nằm trong lô.");

            await DetachRequestAsync(shipment, wroRequestId);
            _unitOfWork.Repository<ShipmentWroRequest>().Delete(link);
            await _unitOfWork.SaveChangesAsync();

            return await _shipmentQueries.GetByIdAsync(shipment.Id);
        }

        public async Task<InternationalShipmentResponseDto> CancelAsync(Guid userId, string role, Guid shipmentId)
        {
            EnsureWarehouseOperator(role);
            var shipment = await GetShipmentAsync(shipmentId);
            EnsureDraft(shipment);
            await EnsureWarehouseScopeAsync(userId, role, shipment.OriginWarehouseId);

            var links = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                .Where(l => l.ShipmentId == shipment.Id)
                .ToList();

            foreach (var link in links)
            {
                await DetachRequestAsync(shipment, link.WroRequestId);
                _unitOfWork.Repository<ShipmentWroRequest>().Delete(link);
            }

            shipment.Status = ExportShipmentStatuses.Cancelled;
            _unitOfWork.Repository<Shipment>().Update(shipment);
            await _unitOfWork.SaveChangesAsync();

            return await _shipmentQueries.GetByIdAsync(shipment.Id);
        }

        // ── Bàn giao ───────────────────────────────────────────────────────────────────────

        public async Task<InternationalShipmentResponseDto> HandoverAsync(
            Guid userId, string role, Guid shipmentId, ShipmentHandoverRequest request)
        {
            EnsureWarehouseOperator(role);
            if (request == null) throw new ArgumentException("Thiếu dữ liệu bàn giao.");
            if (string.IsNullOrWhiteSpace(request.RecipientName))
                throw new ArgumentException("Phải ghi tên người nhận bên hãng vận chuyển.");
            if (string.IsNullOrWhiteSpace(request.WaybillCode))
                throw new ArgumentException("Phải nhập mã vận đơn.");

            var shipment = await GetShipmentAsync(shipmentId);
            EnsureDraft(shipment);
            await EnsureWarehouseScopeAsync(userId, role, shipment.OriginWarehouseId);

            var carrier = await _unitOfWork.Repository<Carrier>().GetByIdAsync(request.CarrierId)
                ?? throw new NotFoundException($"Không tìm thấy đơn vị vận chuyển {request.CarrierId}.");

            var links = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                .Where(l => l.ShipmentId == shipment.Id)
                .ToList();
            if (links.Count == 0)
                throw new ArgumentException("Lô chưa có phiếu xuất kho nào.");

            var requests = await LoadRequestsAsync(links.Select(l => l.WroRequestId).ToList());
            var rows = (await _unitOfWork.Repository<WroParcel>().GetAllAsync()).ToList();
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id);

            var shipmentRows = new List<WroParcel>();
            foreach (var wro in requests)
            {
                if (!string.Equals(wro.Status?.Trim(), WroStatuses.InShipment, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Phiếu {wro.WroCode} đang ở {WroStatuses.Describe(wro.Status)}, chưa bàn giao được.");

                var active = rows.Where(r => r.WroRequestId == wro.Id && IsPickedOrReserved(r)).ToList();
                if (active.Count == 0)
                    throw new ArgumentException($"Phiếu {wro.WroCode} không còn kiện nào.");

                var notPicked = active.Where(r => !IsStatus(r, WroParcelStatuses.Picked)).ToList();
                if (notPicked.Count > 0)
                {
                    var codes = notPicked.Select(r => parcels.TryGetValue(r.ParcelId, out var p) ? p.PackageCode : r.ParcelId.ToString());
                    throw new ArgumentException(
                        $"Phiếu {wro.WroCode} còn kiện chưa bốc sang khu xuất: {string.Join(", ", codes)}.");
                }

                shipmentRows.AddRange(active);
            }

            await EnsureOrdersStillPayableAsync(shipmentRows, parcels);
            await EnsureHandoverDocumentsAsync(shipment, requests);

            var now = DateTime.UtcNow;
            var binRepo = _unitOfWork.Repository<Bin>();
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var inventories = (await inventoryRepo.GetAllAsync()).ToList();
            var touchedBins = new HashSet<Guid>();

            foreach (var row in shipmentRows)
            {
                if (!parcels.TryGetValue(row.ParcelId, out var parcel)) continue;

                // Chỉ đóng đúng dòng tồn của phiếu này. Dòng tồn khác của cùng kiện (dữ liệu cũ
                // sai sổ) để nguyên và báo ở màn tồn kho, không tự ý xoá số của người khác.
                var inventory = inventories.FirstOrDefault(i => i.ParcelId == parcel.Id
                    && string.Equals(i.Status, InventoryStatuses.Picked, StringComparison.OrdinalIgnoreCase));

                var binId = parcel.BinId ?? row.StagingBinId;
                if (binId.HasValue) touchedBins.Add(binId.Value);

                if (inventory != null)
                {
                    inventory.Status = InventoryStatuses.Released;
                    inventoryRepo.Update(inventory);

                    await _unitOfWork.Repository<InventoryMovement>().AddAsync(new InventoryMovement
                    {
                        Id = Guid.NewGuid(),
                        InventoryId = inventory.Id,
                        ParcelId = parcel.Id,
                        FromBinId = binId ?? inventory.BinId,
                        ToBinId = binId ?? inventory.BinId,
                        MovementType = "OUTBOUND",
                        Reason = $"Bàn giao lô {shipment.ShipmentCode} cho {carrier.CarrierName}",
                        Quantity = inventory.Quantity > 0 ? inventory.Quantity : 1,
                        MovedById = userId,
                        MovedAt = now
                    });
                }

                parcel.BinId = null;
                parcel.PackageStatus = ParcelStatuses.InTransit;
                parcelRepo.Update(parcel);

                row.Status = WroParcelStatuses.HandedOver;
                _unitOfWork.Repository<WroParcel>().Update(row);

                await _unitOfWork.Repository<ShipmentParcel>().AddAsync(new ShipmentParcel
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    ParcelId = parcel.Id
                });
            }

            foreach (var binId in touchedBins)
            {
                var bin = await binRepo.GetByIdAsync(binId);
                if (bin == null) continue;
                bin.CurrentVolume = await BinPlacementPolicy.VolumeAfterLeavingAsync(
                    _unitOfWork, binId, shipmentRows.Select(r => r.ParcelId).ToList());
                binRepo.Update(bin);
            }

            foreach (var wro in requests)
            {
                wro.Status = WroStatuses.HandedOver;
                wro.CarrierId = carrier.Id;
                wro.TrackingNumber = request.WaybillCode.Trim();
                wro.HandoverAt = now;
                wro.ReleasedBy = userId;
                wro.ReleasedAt = now;
                _unitOfWork.Repository<WroRequest>().Update(wro);
            }

            shipment.Status = ExportShipmentStatuses.HandedOver;
            shipment.CarrierId = carrier.Id;
            shipment.CarrierTrackingCode = request.WaybillCode.Trim();
            shipment.ShippedAt = now;
            shipment.HandedOverAt = now;
            shipment.TotalPackages = shipmentRows.Count;
            shipment.TotalWeight = shipmentRows
                .Select(r => parcels.TryGetValue(r.ParcelId, out var p) ? p.ActualWeight ?? 0m : 0m)
                .Sum();
            _unitOfWork.Repository<Shipment>().Update(shipment);

            await _unitOfWork.Repository<ShipmentHandover>().AddAsync(new ShipmentHandover
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                ShipperId = userId,
                ToCarrierId = carrier.Id,
                HandoverLocation = request.HandoverLocation,
                HandoverTime = now,
                Status = ExportShipmentStatuses.HandedOver,
                RecipientName = request.RecipientName.Trim(),
                RecipientPhone = request.RecipientPhone,
                WaybillCode = request.WaybillCode.Trim(),
                VehicleNumber = request.VehicleNumber,
                SealNumber = request.SealNumber,
                Note = request.Note,
                HandedOverById = userId
            });

            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = ExportShipmentStatuses.HandedOver,
                TrackingStage = "INTERNATIONAL",
                Location = request.HandoverLocation,
                Note = $"Bàn giao cho {carrier.CarrierName}, vận đơn {request.WaybillCode.Trim()}",
                CreatedById = userId,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            await NotifyCustomersAsync(shipmentRows, parcels,
                orderCode => ShipmentMilestones.CustomerTitle(ExportShipmentStatuses.HandedOver),
                orderCode => $"{ShipmentMilestones.CustomerDefaultMessage(ExportShipmentStatuses.HandedOver, orderCode)} "
                             + $"Hãng: {carrier.CarrierName}, mã vận đơn {request.WaybillCode.Trim()}.");

            return await _shipmentQueries.GetByIdAsync(shipment.Id);
        }

        // ── Hành trình ─────────────────────────────────────────────────────────────────────

        public async Task<InternationalShipmentResponseDto> UpdateMilestoneAsync(
            Guid userId, string role, Guid shipmentId, ShipmentMilestoneRequest request)
        {
            if (!StaffRoleHelper.CanUpdateShipmentTracking(role))
                throw new UnauthorizedAccessException("Chỉ Sale, Operations hoặc Admin cập nhật được hành trình lô.");

            var target = request?.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!ShipmentMilestones.AllowedFrom.ContainsKey(target))
                throw new ArgumentException($"Mốc không hợp lệ. Hợp lệ: {string.Join(", ", ShipmentMilestones.DisplayOrder)}.");

            var shipment = await GetShipmentAsync(shipmentId);
            var current = shipment.Status?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!ShipmentMilestones.AllowedFrom[target].Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Lô đang ở {StatusNames.Quote(shipment.Status)}, không đặt được mốc “{ShipmentMilestones.StaffText(target)}”. "
                    + $"Chỉ đi từ: {string.Join(", ", ShipmentMilestones.AllowedFrom[target].Select(s => $"“{ShipmentMilestones.StaffText(s)}”"))}.");
            }

            if (IsOneOf(target, ExportShipmentStatuses.Delayed, ExportShipmentStatuses.OnHold)
                && string.IsNullOrWhiteSpace(request.Note))
            {
                throw new ArgumentException("Mốc trễ / tạm giữ phải ghi chú lý do.");
            }

            var documents = await _attachmentService.DocumentTypesOfAsync(AttachmentEntityTypes.Shipment, shipment.Id);

            if (target == ExportShipmentStatuses.CustomsCleared && !documents.Contains(AttachmentDocumentTypes.CustomsImport))
                throw new ArgumentException($"Phải đính kèm tờ khai nhập ({AttachmentDocumentTypes.CustomsImport}) trước khi ghi mốc thông quan.");

            if (target == ExportShipmentStatuses.OnHold && !documents.Contains(AttachmentDocumentTypes.Incident))
                throw new ArgumentException($"Mốc tạm giữ phải có biên bản sự cố ({AttachmentDocumentTypes.Incident}) đính kèm vào lô.");

            var now = DateTime.UtcNow;

            // Về tới kho đích kéo theo trạng thái từng kiện và mở khoá cho kho VN đối soát, nên
            // vẫn đi qua đúng một thân xử lý cũ của luồng 10.
            if (target == ExportShipmentStatuses.ArrivedDestination)
            {
                await _shipmentQueries.ConfirmArrivalAsync(shipment.Id, userId, role,
                    new ConfirmShipmentArrivalDto { Note = request.Note, CustomerMessage = request.CustomerMessage });
                return await _shipmentQueries.GetByIdAsync(shipment.Id);
            }

            shipment.Status = target;
            if (!string.IsNullOrWhiteSpace(request.CarrierTrackingCode))
                shipment.CarrierTrackingCode = request.CarrierTrackingCode.Trim();
            _unitOfWork.Repository<Shipment>().Update(shipment);

            var customerMessage = string.IsNullOrWhiteSpace(request.CustomerMessage) ? null : request.CustomerMessage.Trim();

            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = target,
                TrackingStage = "INTERNATIONAL",
                Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
                Note = request.Note,
                CustomerMessage = customerMessage,
                CreatedById = userId,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            // Mỗi mốc là một tin cho khách: khách không vào hệ thống hằng ngày, thông báo là cách
            // duy nhất họ biết hàng đang ở đâu mà không phải gọi hỏi Sale.
            var rows = await ShipmentParcelRowsAsync(shipment.Id);
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id);
            await NotifyCustomersAsync(rows, parcels,
                orderCode => $"Đơn {orderCode}: {ShipmentMilestones.CustomerTitle(target)}",
                orderCode => customerMessage ?? ShipmentMilestones.CustomerDefaultMessage(target, orderCode));

            return await _shipmentQueries.GetByIdAsync(shipment.Id);
        }

        // ── Dùng chung ─────────────────────────────────────────────────────────────────────

        private async Task<Shipment> GetShipmentAsync(Guid shipmentId) =>
            await _unitOfWork.Repository<Shipment>().GetByIdAsync(shipmentId)
            ?? throw new NotFoundException($"Không tìm thấy lô vận chuyển {shipmentId}.");

        private static void EnsureDraft(Shipment shipment)
        {
            if (!string.Equals(shipment.Status?.Trim(), ExportShipmentStatuses.Draft, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Lô {shipment.ShipmentCode} đang ở {StatusNames.Quote(shipment.Status)}, chỉ sửa được khi còn nháp.");
        }

        private static void EnsureReadyForShipment(WroRequest wro)
        {
            if (!string.Equals(wro.Status?.Trim(), WroStatuses.Ready, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Phiếu {wro.WroCode} đang ở {WroStatuses.Describe(wro.Status)} — chỉ gom được phiếu đã bốc xong (READY).");
            }
        }

        private async Task<List<WroRequest>> LoadRequestsAsync(List<Guid> ids)
        {
            var all = (await _unitOfWork.Repository<WroRequest>().GetAllAsync()).ToList();
            var result = new List<WroRequest>();
            foreach (var id in ids.Distinct())
            {
                result.Add(all.FirstOrDefault(w => w.Id == id)
                    ?? throw new NotFoundException($"Không tìm thấy phiếu xuất kho {id}."));
            }
            return result;
        }

        private async Task AttachRequestsAsync(Shipment shipment, List<WroRequest> requests, DateTime now)
        {
            var existing = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                .Where(l => l.ShipmentId == shipment.Id)
                .Select(l => l.WroRequestId)
                .ToHashSet();

            foreach (var wro in requests)
            {
                if (existing.Contains(wro.Id)) continue;

                await _unitOfWork.Repository<ShipmentWroRequest>().AddAsync(new ShipmentWroRequest
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    WroRequestId = wro.Id
                });

                wro.Status = WroStatuses.InShipment;
                _unitOfWork.Repository<WroRequest>().Update(wro);
            }
        }

        /// <summary>Gỡ phiếu khỏi lô nháp: phiếu về lại READY, hàng vẫn nằm ở khu xuất.</summary>
        private async Task DetachRequestAsync(Shipment shipment, Guid wroRequestId)
        {
            var wro = await _unitOfWork.Repository<WroRequest>().GetByIdAsync(wroRequestId);
            if (wro == null) return;

            if (string.Equals(wro.Status?.Trim(), WroStatuses.InShipment, StringComparison.OrdinalIgnoreCase))
            {
                wro.Status = WroStatuses.Ready;
                _unitOfWork.Repository<WroRequest>().Update(wro);
            }
        }

        /// <summary>
        /// Chốt tiền cuối cùng trước khi hàng rời kho: đơn phải còn cọc đã xác nhận và không bị
        /// giữ hàng. Trả về cả danh sách để kho biết gỡ phiếu nào ra khỏi lô.
        /// </summary>
        private async Task EnsureOrdersStillPayableAsync(List<WroParcel> rows, Dictionary<Guid, Parcel> parcels)
        {
            var orderIds = rows
                .Select(r => parcels.TryGetValue(r.ParcelId, out var p) ? p.ConsignmentId : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();
            if (orderIds.Count == 0) return;

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id))
                .ToList();

            var problems = new List<string>();
            foreach (var order in orders)
            {
                if (order.ExportHold)
                {
                    problems.Add($"{order.ConsignmentCode}: đang bật giữ hàng");
                    continue;
                }

                var isPurchase = string.Equals(order.OrderType?.Trim(), "PURCHASE", StringComparison.OrdinalIgnoreCase);
                if (!isPurchase && !await DepositQuery.HasConfirmedDepositAsync(_unitOfWork, order.Id))
                    problems.Add($"{order.ConsignmentCode}: không còn cọc đã xác nhận");
            }

            if (problems.Count > 0)
            {
                throw new ArgumentException(
                    "Không bàn giao được, các đơn sau đang vướng — gỡ phiếu chứa kiện của đơn ra khỏi lô: "
                    + string.Join(" | ", problems));
            }
        }

        private async Task EnsureHandoverDocumentsAsync(Shipment shipment, List<WroRequest> requests)
        {
            var documents = await _attachmentService.DocumentTypesOfAsync(AttachmentEntityTypes.Shipment, shipment.Id);

            var missing = new List<string>();
            if (!documents.Contains(AttachmentDocumentTypes.HandoverRecord)) missing.Add(AttachmentDocumentTypes.HandoverRecord);
            if (!documents.Contains(AttachmentDocumentTypes.Waybill)) missing.Add(AttachmentDocumentTypes.Waybill);

            var routeIds = requests.Where(w => w.ShippingRouteId.HasValue).Select(w => w.ShippingRouteId!.Value).ToHashSet();
            if (routeIds.Count > 0)
            {
                var needsCustoms = (await _unitOfWork.Repository<ShippingRoute>().GetAllAsync())
                    .Any(r => routeIds.Contains(r.Id) && r.RequiresCustomsExport);

                if (needsCustoms && !documents.Contains(AttachmentDocumentTypes.CustomsExport))
                    missing.Add(AttachmentDocumentTypes.CustomsExport);
            }

            if (missing.Count > 0)
            {
                throw new ArgumentException(
                    $"Thiếu giấy tờ bàn giao ({string.Join(", ", missing)}). Đính kèm vào lô qua POST /api/attachments "
                    + $"với entityType = {AttachmentEntityTypes.Shipment}, entityId = {shipment.Id}.");
            }
        }

        private async Task<List<WroParcel>> ShipmentParcelRowsAsync(Guid shipmentId)
        {
            var wroIds = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                .Where(l => l.ShipmentId == shipmentId)
                .Select(l => l.WroRequestId)
                .ToHashSet();

            return (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
                .Where(r => wroIds.Contains(r.WroRequestId) && !IsStatus(r, WroParcelStatuses.Removed))
                .ToList();
        }

        private async Task NotifyCustomersAsync(
            List<WroParcel> rows, Dictionary<Guid, Parcel> parcels,
            Func<string, string> title, Func<string, string> content)
        {
            var orderIds = rows
                .Select(r => parcels.TryGetValue(r.ParcelId, out var p) ? p.ConsignmentId : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();
            if (orderIds.Count == 0) return;

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id))
                .ToList();
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);

            foreach (var order in orders)
            {
                if (!order.CustomerId.HasValue) continue;
                if (!customers.TryGetValue(order.CustomerId.Value, out var customer)) continue;
                if (customer.UserId is not Guid userId || userId == Guid.Empty) continue;

                await _notificationService.NotifyUserAsync(userId, title(order.ConsignmentCode), content(order.ConsignmentCode));
            }
        }

        private async Task<string> GenerateShipmentCodeAsync()
        {
            var prefix = $"SHIP-{DateTime.UtcNow:yyyyMMdd}-";
            var existing = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .Where(s => s.ShipmentCode != null && s.ShipmentCode.StartsWith(prefix))
                .Select(s => s.ShipmentCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i <= 9999; i++)
            {
                var code = prefix + i.ToString("D3");
                if (!existing.Contains(code)) return code;
            }

            return prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        }

        private static void EnsureWarehouseOperator(string role)
        {
            if (!StaffRoleHelper.CanOperateWarehouse(role))
                throw new UnauthorizedAccessException("Chỉ nhân viên kho (hoặc Admin) mới thao tác được lô vận chuyển.");
        }

        /// <summary>Phạm vi kho của người thao tác lô — xem <see cref="WarehouseAccessPolicy"/>.</summary>
        private Task EnsureWarehouseScopeAsync(Guid userId, string role, Guid? warehouseId) =>
            WarehouseAccessPolicy.EnsureCanOperateAsync(
                _unitOfWork, userId, role, warehouseId,
                notFoundMessage: "Không tìm thấy kho nguồn của lô.",
                actionLabel: "lô của kho");

        private static string NameOf(Dictionary<Guid, Warehouse> warehouses, Guid? id) =>
            id.HasValue && warehouses.TryGetValue(id.Value, out var w) ? w.WarehouseName : null;

        private static bool IsStatus(WroParcel row, string status) =>
            string.Equals(row.Status?.Trim(), status, StringComparison.OrdinalIgnoreCase);

        private static bool IsPickedOrReserved(WroParcel row) =>
            IsStatus(row, WroParcelStatuses.Reserved) || IsStatus(row, WroParcelStatuses.Picked);

        private static bool IsOneOf(string value, params string[] candidates) =>
            candidates.Any(c => string.Equals(value?.Trim(), c, StringComparison.OrdinalIgnoreCase));
    }
}
