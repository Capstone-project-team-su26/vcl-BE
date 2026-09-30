using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Sự cố hàng hoá ở chặng Việt Nam.
    /// <para>
    /// Hàng là của khách nên khách được nói muốn xử lý thế nào (nhận như hiện trạng / đòi bồi
    /// thường / huỷ hàng), nhưng quyết định cuối là của quản lý kho — người khác người lập biên
    /// bản — vì đó là chỗ phát sinh tiền đền.
    /// </para>
    /// </summary>
    public class ParcelIncidentService : IParcelIncidentService
    {
        private const int DefaultComplaintWindowDays = 3;

        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;
        private readonly IAttachmentService _attachmentService;
        private readonly IConfiguration _configuration;

        public ParcelIncidentService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            IAttachmentService attachmentService,
            IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _attachmentService = attachmentService;
            _configuration = configuration;
        }

        /// <summary>Số ngày khách được khiếu nại sau khi giao xong — cũng là hạn tự đóng đơn.</summary>
        public static int ComplaintWindowDays(IConfiguration configuration) =>
            int.TryParse(configuration["Closing:ComplaintWindowDays"], out var d) && d >= 0 ? d : DefaultComplaintWindowDays;

        // ── Mở sự cố ───────────────────────────────────────────────────────────────────────

        public async Task<ParcelIncidentDto> CreateAsync(Guid userId, string role, CreateParcelIncidentRequest request)
        {
            if (!StaffRoleHelper.CanOperateWarehouse(role) && !StaffRoleHelper.CanApproveWarehouseStore(role))
                throw new UnauthorizedAccessException("Chỉ nhân viên kho mới mở được sự cố hàng hoá.");

            var type = request?.IncidentType?.Trim().ToUpperInvariant();
            if (type == null || !IncidentTypes.ReportableByWarehouse.Contains(type))
                throw new ArgumentException($"Loại sự cố chỉ nhận {StatusNames.QuoteAny("DAMAGED", "WRONG_ITEM", "MISSING_ITEMS")}.");
            if (string.IsNullOrWhiteSpace(request!.Description))
                throw new ArgumentException("Phải mô tả hiện trạng kiện.");

            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(request.ParcelId)
                ?? throw new NotFoundException("Không tìm thấy kiện.");

            var status = parcel.PackageStatus?.Trim().ToUpperInvariant();
            if (status != VnParcelStatuses.ReceivedAtDestination && status != VnParcelStatuses.Stored)
            {
                throw new ArgumentException(
                    $"Chỉ mở sự cố cho kiện đang ở kho Việt Nam (khu nhận hoặc đã lưu kho). Kiện {parcel.PackageCode} đang ở {StatusNames.Quote(parcel.PackageStatus)}.");
            }

            if (await ParcelIncidentFactory.HasOpenIncidentAsync(_unitOfWork, parcel.Id))
                throw new ArgumentException($"Kiện {parcel.PackageCode} đang có sự cố chưa xử lý xong.");

            var incident = await ParcelIncidentFactory.OpenAsync(
                _unitOfWork, parcel, type, IncidentStages.Receiving, request.Description, userId);

            // Hàng ở khu nhận thì chuyển sang cách ly; hàng đã lên kệ để nguyên chỗ nhưng bị chặn
            // lập yêu cầu giao cho tới khi xử lý xong.
            if (status == VnParcelStatuses.ReceivedAtDestination)
            {
                parcel.PackageStatus = VnParcelStatuses.Quarantined;
                _unitOfWork.Repository<Parcel>().Update(parcel);
            }

            await _unitOfWork.SaveChangesAsync();
            await NotifyOpenedAsync(incident, parcel);
            return await GetDetailAsync(userId, role, incident.Id);
        }

        public async Task<List<ParcelIncidentDto>> CreateComplaintAsync(
            Guid userId, string role, Guid orderId, CreateComplaintRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Description))
                throw new ArgumentException("Phải mô tả vấn đề khiếu nại.");

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
            await EnsureOrderOwnerOrStaffAsync(order, userId, role);

            if (!string.Equals(order.Status, "DELIVERED", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(order.Status, "CUSTOMER_CONFIRMED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Chỉ khiếu nại được sau khi đơn đã giao xong và trước khi đơn hoàn thành.");
            }

            var window = ComplaintWindowDays(_configuration);
            if (order.DeliveredAt.HasValue && DateTime.UtcNow > order.DeliveredAt.Value.AddDays(window))
                throw new ArgumentException($"Đã quá {window} ngày kể từ khi giao hàng, không mở khiếu nại được nữa.");

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == orderId
                            && string.Equals(p.PackageStatus, VnParcelStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (request!.ParcelIds is { Count: > 0 })
            {
                var wanted = request.ParcelIds.ToHashSet();
                parcels = parcels.Where(p => wanted.Contains(p.Id)).ToList();
                if (parcels.Count != wanted.Count)
                    throw new ArgumentException("Có kiện không thuộc đơn hoặc chưa được giao.");
            }

            if (parcels.Count == 0)
                throw new ArgumentException("Đơn không có kiện nào đã giao để khiếu nại.");

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var created = new List<ParcelIncident>();
            foreach (var parcel in parcels)
            {
                if (await ParcelIncidentFactory.HasOpenIncidentAsync(_unitOfWork, parcel.Id)) continue;
                created.Add(await ParcelIncidentFactory.OpenAsync(
                    _unitOfWork, parcel, IncidentTypes.Complaint, IncidentStages.AfterDelivery, request.Description, userId, reserved));
            }

            if (created.Count == 0)
                throw new ArgumentException("Các kiện này đang có khiếu nại chưa xử lý xong.");

            await _unitOfWork.SaveChangesAsync();

            var title = $"Khách khiếu nại đơn {order.ConsignmentCode}";
            var content = $"{created.Count} kiện của đơn {order.ConsignmentCode} bị khiếu nại: {request.Description.Trim()}";
            await _notificationService.NotifyRolesAsync(new[] { "OperationsManager", "Operations", "Sale", "Admin" }, title, content, userId);

            var result = new List<ParcelIncidentDto>();
            foreach (var incident in created)
                result.Add(await GetDetailAsync(userId, role, incident.Id));
            return result;
        }

        // ── Đọc ────────────────────────────────────────────────────────────────────────────

        public async Task<PagedParcelIncidentsDto> GetListAsync(Guid userId, string role, ParcelIncidentQuery query)
        {
            query ??= new ParcelIncidentQuery();
            var incidents = (await _unitOfWork.Repository<ParcelIncident>().GetAllAsync()).AsEnumerable();

            if (!StaffRoleHelper.IsInternalStaff(role))
            {
                var orderIds = await OwnedOrderIdsAsync(userId);
                incidents = incidents.Where(i => i.OrderId.HasValue && orderIds.Contains(i.OrderId.Value));
            }

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var wanted = query.Status.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                incidents = incidents.Where(i => wanted.Contains(i.Status ?? string.Empty));
            }

            if (query.OrderId.HasValue)
                incidents = incidents.Where(i => i.OrderId == query.OrderId.Value);
            if (!string.IsNullOrWhiteSpace(query.IncidentType))
                incidents = incidents.Where(i => string.Equals(i.IncidentType, query.IncidentType.Trim(), StringComparison.OrdinalIgnoreCase));

            var list = incidents.OrderByDescending(i => i.ReportedAt).ToList();
            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);
            var lookup = await LoadLookupAsync();

            return new PagedParcelIncidentsDto
            {
                Items = list.Skip((pageNumber - 1) * pageSize).Take(pageSize).Select(i => ToDto(i, lookup)).ToList(),
                TotalCount = list.Count,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(list.Count / (double)pageSize)
            };
        }

        public async Task<ParcelIncidentDto> GetDetailAsync(Guid userId, string role, Guid incidentId)
        {
            var incident = await GetIncidentAsync(incidentId);
            await EnsureCanViewAsync(incident, userId, role);

            var dto = ToDto(incident, await LoadLookupAsync());
            dto.Attachments = await _attachmentService.ListAsync(userId, role, AttachmentEntityTypes.Incident, incident.Id);
            return dto;
        }

        // ── Khách chọn cách xử lý ──────────────────────────────────────────────────────────

        public async Task<ParcelIncidentDto> RespondAsync(Guid userId, string role, Guid incidentId, IncidentCustomerResponseRequest request)
        {
            var incident = await GetIncidentAsync(incidentId);
            var order = incident.OrderId.HasValue ? await _unitOfWork.Repository<Order>().GetByIdAsync(incident.OrderId.Value) : null;
            if (order == null) throw new NotFoundException("Sự cố không gắn với đơn nào.");
            await EnsureOrderOwnerOrStaffAsync(order, userId, role);

            if (!AwaitsCustomerChoice(incident))
                throw new ArgumentException("Loại sự cố này không cần khách chọn cách xử lý.");
            if (string.Equals(incident.Status, IncidentStatuses.Resolved, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Sự cố đã được xử lý xong.");

            var choice = request?.Choice?.Trim().ToUpperInvariant();
            if (choice == null || !IncidentResolutions.All.Contains(choice))
                throw new ArgumentException("choice chỉ nhận ACCEPT, COMPENSATE hoặc DISPOSE.");
            if (choice == IncidentResolutions.Dispose && IsComplaint(incident))
                throw new ArgumentException("Hàng đã giao thì không chọn huỷ hàng được — chọn ACCEPT hoặc COMPENSATE.");

            incident.CustomerChoice = choice;
            incident.CustomerNote = string.IsNullOrWhiteSpace(request!.Note) ? null : request.Note.Trim();
            incident.CustomerRespondedAt = DateTime.UtcNow;
            incident.Status = IncidentStatuses.CustomerResponded;
            _unitOfWork.Repository<ParcelIncident>().Update(incident);
            await _unitOfWork.SaveChangesAsync();

            var warehouse = await WarehouseOfAsync(incident);
            var title = $"Khách đã chọn cách xử lý sự cố {incident.IncidentCode}";
            var content = $"Đơn {order.ConsignmentCode}: khách chọn {DescribeResolution(choice)}. Chờ quản lý kho quyết định.";
            if (warehouse?.ManagerId is Guid managerId)
                await _notificationService.NotifyUserAsync(managerId, title, content);
            else
                await _notificationService.NotifyRolesAsync(new[] { "OperationsManager", "Operations", "Admin" }, title, content, userId);

            return await GetDetailAsync(userId, role, incident.Id);
        }

        // ── Quản lý kho quyết định ─────────────────────────────────────────────────────────

        public async Task<ParcelIncidentDto> ResolveAsync(Guid userId, string role, Guid incidentId, ResolveIncidentRequest request)
        {
            var incident = await GetIncidentAsync(incidentId);
            if (string.Equals(incident.Status, IncidentStatuses.Resolved, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Sự cố đã được xử lý xong.");

            var resolution = request?.Resolution?.Trim().ToUpperInvariant();
            if (resolution == null || !IncidentResolutions.All.Contains(resolution))
                throw new ArgumentException("resolution chỉ nhận ACCEPT, COMPENSATE hoặc DISPOSE.");

            var type = incident.IncidentType?.Trim().ToUpperInvariant();
            if (type == IncidentTypes.WeightDeviation && resolution != IncidentResolutions.Accept)
                throw new ArgumentException("Cân lệch chỉ có một cách xử lý: ACCEPT (dùng số cân đo tại kho VN, cân lại trước nếu cần).");
            if (resolution == IncidentResolutions.Dispose && IsComplaint(incident))
                throw new ArgumentException("Hàng đã giao thì không huỷ hàng được.");

            if (resolution == IncidentResolutions.Compensate && (request!.CompensationAmount is not > 0m))
                throw new ArgumentException("Bồi thường phải ghi số tiền (compensationAmount > 0).");

            // Khách chưa trả lời mà quản lý kho vẫn quyết thì phải ghi vì sao — thường là khách
            // không phản hồi, hàng không thể nằm cách ly mãi.
            if (AwaitsCustomerChoice(incident) && incident.CustomerChoice == null && string.IsNullOrWhiteSpace(request!.Note))
                throw new ArgumentException("Khách chưa chọn cách xử lý — quyết định thay thì phải ghi lý do (note).");

            if (IncidentTypes.ReportableByWarehouse.Contains(type ?? string.Empty) || type == IncidentTypes.Complaint)
            {
                var docs = await _attachmentService.DocumentTypesOfAsync(AttachmentEntityTypes.Incident, incident.Id);
                if (!docs.Contains(AttachmentDocumentTypes.IncidentPhoto))
                {
                    throw new ArgumentException(
                        $"Phải có ảnh hiện trạng (documentType = {AttachmentDocumentTypes.IncidentPhoto}, entityType = {AttachmentEntityTypes.Incident}, "
                        + $"entityId = {incident.Id}) trước khi quyết định.");
                }
            }

            var warehouse = await WarehouseOfAsync(incident);
            if (warehouse != null)
            {
                await WarehouseApprovalPolicy.EnsureCanDecideAsync(
                    _unitOfWork, warehouse.Id, userId, role, incident.ReportedById, request!.Note, "sự cố hàng hoá");
            }
            else
            {
                if (incident.ReportedById == userId)
                    throw new UnauthorizedAccessException("Người lập biên bản sự cố không được tự quyết định.");
                if (!StaffRoleHelper.CanApproveWarehouseStore(role))
                    throw new UnauthorizedAccessException("Chỉ quản lý kho / Operations Manager được quyết định sự cố.");
            }

            var now = DateTime.UtcNow;
            incident.Resolution = resolution;
            incident.ResolutionNote = string.IsNullOrWhiteSpace(request!.Note) ? null : request.Note.Trim();
            incident.CompensationAmount = resolution == IncidentResolutions.Compensate ? request.CompensationAmount : null;
            incident.ResolvedById = userId;
            incident.ResolvedAt = now;
            incident.Status = IncidentStatuses.Resolved;
            _unitOfWork.Repository<ParcelIncident>().Update(incident);

            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(incident.ParcelId);
            if (parcel != null)
            {
                if (resolution == IncidentResolutions.Dispose)
                {
                    await DisposeParcelAsync(parcel, incident, userId, now);
                }
                else if (string.Equals(parcel.PackageStatus, VnParcelStatuses.Quarantined, StringComparison.OrdinalIgnoreCase))
                {
                    // Nhận như hiện trạng / bồi thường: hàng vẫn đi tiếp, về lại khu nhận.
                    parcel.PackageStatus = VnParcelStatuses.ReceivedAtDestination;
                    _unitOfWork.Repository<Parcel>().Update(parcel);
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await NotifyCustomerAsync(incident,
                $"Sự cố {incident.IncidentCode} đã được xử lý",
                $"Kiện {parcel?.PackageCode}: {DescribeResolution(resolution)}"
                + (incident.CompensationAmount is > 0m ? $" — bồi thường {incident.CompensationAmount:#,##0}đ." : ".")
                + (incident.ResolutionNote != null ? $" Ghi chú: {incident.ResolutionNote}" : string.Empty));

            return await GetDetailAsync(userId, role, incident.Id);
        }

        /// <summary>
        /// Huỷ hàng theo quyết định sự cố: kiện rời mọi quy trình — không tính cước, không giao,
        /// nhả ô kệ nếu đang nằm trên kệ.
        /// </summary>
        private async Task DisposeParcelAsync(Parcel parcel, ParcelIncident incident, Guid userId, DateTime now)
        {
            var binId = parcel.BinId;
            parcel.PackageStatus = VnParcelStatuses.Disposed;
            parcel.BinId = null;
            _unitOfWork.Repository<Parcel>().Update(parcel);

            if (binId.HasValue)
            {
                var bin = await _unitOfWork.Repository<Bin>().GetByIdAsync(binId.Value);
                if (bin != null)
                {
                    bin.CurrentVolume = await BinPlacementPolicy.VolumeAfterLeavingAsync(_unitOfWork, bin.Id, new[] { parcel.Id });
                    _unitOfWork.Repository<Bin>().Update(bin);
                }
            }

            var inventories = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                .Where(i => i.ParcelId == parcel.Id && !string.Equals(i.Status, InventoryStatuses.Released, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var inventory in inventories)
            {
                inventory.Status = InventoryStatuses.Released;
                _unitOfWork.Repository<Inventory>().Update(inventory);
                await _unitOfWork.Repository<InventoryMovement>().AddAsync(new InventoryMovement
                {
                    Id = Guid.NewGuid(),
                    InventoryId = inventory.Id,
                    ParcelId = parcel.Id,
                    FromBinId = binId ?? inventory.BinId,
                    ToBinId = binId ?? inventory.BinId,
                    MovementType = "DISPOSAL",
                    Reason = $"Huỷ hàng theo sự cố {incident.IncidentCode}",
                    Quantity = inventory.Quantity > 0 ? inventory.Quantity : 1,
                    MovedById = userId,
                    MovedAt = now
                });
            }
        }

        public async Task<ParcelIncidentDto> MarkCompensationPaidAsync(Guid userId, string role, Guid incidentId, CompensationPaidRequest request)
        {
            if (!WarehouseApprovalPolicy.IsAdmin(role))
                throw new UnauthorizedAccessException("Chỉ Admin ghi nhận đã chi bồi thường.");
            if (string.IsNullOrWhiteSpace(request?.Reference))
                throw new ArgumentException("Phải ghi mã giao dịch chi bồi thường (reference).");

            var incident = await GetIncidentAsync(incidentId);
            if (!string.Equals(incident.Resolution, IncidentResolutions.Compensate, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Sự cố này không có quyết định bồi thường.");
            if (incident.CompensationPaidAt.HasValue)
                throw new ArgumentException("Bồi thường đã được ghi nhận chi trước đó.");

            var docs = await _attachmentService.DocumentTypesOfAsync(AttachmentEntityTypes.Incident, incident.Id);
            if (!docs.Contains(AttachmentDocumentTypes.CompensationReceipt))
                throw new ArgumentException($"Phải đính kèm chứng từ chi ({AttachmentDocumentTypes.CompensationReceipt}) vào sự cố trước.");

            incident.CompensationPaidAt = DateTime.UtcNow;
            incident.CompensationPaidById = userId;
            incident.CompensationReference = request!.Reference.Trim();
            _unitOfWork.Repository<ParcelIncident>().Update(incident);
            await _unitOfWork.SaveChangesAsync();

            await NotifyCustomerAsync(incident,
                $"Đã chi bồi thường sự cố {incident.IncidentCode}",
                $"Công ty đã chuyển {incident.CompensationAmount:#,##0}đ bồi thường, mã giao dịch {incident.CompensationReference}.");

            return await GetDetailAsync(userId, role, incident.Id);
        }

        // ── Dùng chung ─────────────────────────────────────────────────────────────────────

        private static bool IsComplaint(ParcelIncident incident) =>
            string.Equals(incident.IncidentType, IncidentTypes.Complaint, StringComparison.OrdinalIgnoreCase);

        private static bool AwaitsCustomerChoice(ParcelIncident incident) =>
            !string.Equals(incident.IncidentType, IncidentTypes.WeightDeviation, StringComparison.OrdinalIgnoreCase);

        private static string DescribeResolution(string? resolution) => resolution?.Trim().ToUpperInvariant() switch
        {
            IncidentResolutions.Accept => "nhận hàng như hiện trạng",
            IncidentResolutions.Compensate => "bồi thường",
            IncidentResolutions.Dispose => "huỷ hàng",
            _ => resolution ?? string.Empty
        };

        private async Task<ParcelIncident> GetIncidentAsync(Guid incidentId) =>
            await _unitOfWork.Repository<ParcelIncident>().GetByIdAsync(incidentId)
            ?? throw new NotFoundException("Không tìm thấy sự cố.");

        private async Task<HashSet<Guid>> OwnedOrderIdsAsync(Guid userId)
        {
            var customerIds = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .Where(c => c.UserId == userId).Select(c => c.Id).ToHashSet();
            return (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => o.CustomerId.HasValue && customerIds.Contains(o.CustomerId.Value))
                .Select(o => o.Id).ToHashSet();
        }

        private async Task EnsureOrderOwnerOrStaffAsync(Order order, Guid userId, string role)
        {
            if (StaffRoleHelper.IsInternalStaff(role)) return;
            var customer = order.CustomerId.HasValue ? await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value) : null;
            if (customer?.UserId != userId)
                throw new UnauthorizedAccessException("Bạn chỉ thao tác được trên đơn của mình.");
        }

        private async Task EnsureCanViewAsync(ParcelIncident incident, Guid userId, string role)
        {
            if (StaffRoleHelper.IsInternalStaff(role)) return;
            var order = incident.OrderId.HasValue ? await _unitOfWork.Repository<Order>().GetByIdAsync(incident.OrderId.Value) : null;
            if (order == null) throw new UnauthorizedAccessException("Bạn chỉ xem được sự cố của đơn mình.");
            await EnsureOrderOwnerOrStaffAsync(order, userId, role);
        }

        /// <summary>Kho đang giữ kiện: ô kệ hiện tại, không có thì kho đích của lô đã chở kiện về.</summary>
        private async Task<Warehouse?> WarehouseOfAsync(ParcelIncident incident)
        {
            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(incident.ParcelId);
            if (parcel?.BinId != null)
            {
                var bin = await _unitOfWork.Repository<Bin>().GetByIdAsync(parcel.BinId.Value);
                if (bin?.WarehouseId != null)
                    return await _unitOfWork.Repository<Warehouse>().GetByIdAsync(bin.WarehouseId.Value);
            }

            var shipmentId = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync())
                .Where(i => i.ParcelId == incident.ParcelId && i.ShipmentId.HasValue)
                .OrderByDescending(i => i.InspectedAt)
                .Select(i => i.ShipmentId)
                .FirstOrDefault();
            if (shipmentId.HasValue)
            {
                var shipment = await _unitOfWork.Repository<Shipment>().GetByIdAsync(shipmentId.Value);
                if (shipment?.DestinationWarehouseId != null)
                    return await _unitOfWork.Repository<Warehouse>().GetByIdAsync(shipment.DestinationWarehouseId.Value);
            }

            return null;
        }

        private async Task NotifyOpenedAsync(ParcelIncident incident, Parcel parcel)
        {
            await NotifyCustomerAsync(incident,
                $"Kiện {parcel.PackageCode} gặp sự cố: {IncidentTypes.Describe(incident.IncidentType)}",
                $"{incident.Description} Vui lòng vào đơn để chọn cách xử lý (nhận như hiện trạng / bồi thường / huỷ hàng).");
        }

        private async Task NotifyCustomerAsync(ParcelIncident incident, string title, string content)
        {
            if (!incident.OrderId.HasValue) return;
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(incident.OrderId.Value);
            if (order?.CustomerId == null) return;
            var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value);
            if (customer?.UserId is Guid customerUserId && customerUserId != Guid.Empty)
                await _notificationService.NotifyUserAsync(customerUserId, title, content);
        }

        private sealed class Lookup
        {
            public Dictionary<Guid, Parcel> Parcels { get; init; } = new();
            public Dictionary<Guid, Order> Orders { get; init; } = new();
            public Dictionary<Guid, string?> CustomerNames { get; init; } = new();
            public Dictionary<Guid, string> UserNames { get; init; } = new();
        }

        private async Task<Lookup> LoadLookupAsync()
        {
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id, u => u.FullName);
            return new Lookup
            {
                Parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).ToDictionary(p => p.Id),
                Orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id),
                CustomerNames = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .ToDictionary(c => c.Id, c => !string.IsNullOrWhiteSpace(c.FullName) ? c.FullName
                        : c.UserId.HasValue && users.TryGetValue(c.UserId.Value, out var n) ? n : null),
                UserNames = users
            };
        }

        private static ParcelIncidentDto ToDto(ParcelIncident i, Lookup lookup)
        {
            lookup.Parcels.TryGetValue(i.ParcelId, out var parcel);
            var order = i.OrderId.HasValue && lookup.Orders.TryGetValue(i.OrderId.Value, out var o) ? o : null;

            return new ParcelIncidentDto
            {
                Id = i.Id,
                IncidentCode = i.IncidentCode,
                ParcelId = i.ParcelId,
                PackageCode = parcel?.PackageCode,
                OrderId = i.OrderId,
                ConsignmentCode = order?.ConsignmentCode,
                CustomerName = order?.CustomerId != null && lookup.CustomerNames.TryGetValue(order.CustomerId.Value, out var cn) ? cn : null,
                IncidentType = i.IncidentType,
                IncidentTypeText = IncidentTypes.Describe(i.IncidentType),
                Status = i.Status,
                Stage = i.Stage,
                Description = i.Description,
                ReportedByName = i.ReportedById.HasValue && lookup.UserNames.TryGetValue(i.ReportedById.Value, out var rn) ? rn : null,
                ReportedAt = i.ReportedAt,
                CustomerChoice = i.CustomerChoice,
                CustomerNote = i.CustomerNote,
                CustomerRespondedAt = i.CustomerRespondedAt,
                Resolution = i.Resolution,
                ResolutionNote = i.ResolutionNote,
                CompensationAmount = i.CompensationAmount,
                ResolvedByName = i.ResolvedById.HasValue && lookup.UserNames.TryGetValue(i.ResolvedById.Value, out var vn) ? vn : null,
                ResolvedAt = i.ResolvedAt,
                CompensationPaidAt = i.CompensationPaidAt,
                CompensationReference = i.CompensationReference,
                AwaitsCustomerChoice = AwaitsCustomerChoice(i)
                                       && string.Equals(i.Status, IncidentStatuses.Open, StringComparison.OrdinalIgnoreCase)
            };
        }
    }
}
