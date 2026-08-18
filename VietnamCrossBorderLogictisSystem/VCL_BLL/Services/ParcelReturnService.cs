using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <inheritdoc cref="IParcelReturnService"/>
    public class ParcelReturnService : IParcelReturnService
    {
        // --- Trạng thái hồ sơ ---
        private const string StatusPendingReturn = "PENDING_RETURN";
        private const string StatusInTransitBack = "IN_TRANSIT_BACK";
        private const string StatusReceived = "RECEIVED_AT_WAREHOUSE";
        private const string StatusResolved = "RESOLVED";
        private const string StatusLost = "LOST";

        // --- Hướng xử lý ---
        private const string ResolutionRedeliver = "REDELIVER";
        private const string ResolutionPickup = "CUSTOMER_PICKUP";
        private const string ResolutionDispose = "DISPOSE";
        private const string ResolutionCompensate = "COMPENSATE";

        /// <summary>Hồ sơ mở quá ngần này ngày mà chưa chốt xong thì coi là bỏ quên.</summary>
        private const int OverdueDays = 3;

        /// <summary>
        /// Trạng thái kiện do hãng báo → lý do hoàn. Chỉ những mã trong đây mới mở hồ sơ;
        /// mã khác là chuyện giao hàng bình thường, không phải hàng hoàn.
        /// </summary>
        private static readonly Dictionary<string, string> CarrierStatusToReason = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DELIVERY_FAILED"] = "FAILED_DELIVERY",
            ["RETURNING"] = "RETURNING",
            ["RETURNED"] = "RETURNING",
            ["LOST"] = "LOST",
            ["DELIVERY_CANCELLED"] = "CANCELLED",
            ["DELIVERY_ERROR"] = "FAILED_DELIVERY",
        };

        /// <summary>
        /// Trạng thái hồ sơ suy ra từ tin của hãng.
        /// <para>
        /// <c>RETURNED</c> nghĩa là hãng đã trả hàng về phía mình, nhưng vẫn để
        /// <c>IN_TRANSIT_BACK</c> chứ không nhảy thẳng sang đã nhận: phải có người ở kho cầm
        /// được kiện thật rồi bấm xác nhận thì mới coi là hàng đã về.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> CarrierStatusToCaseStatus = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DELIVERY_FAILED"] = StatusPendingReturn,
            ["RETURNING"] = StatusInTransitBack,
            ["RETURNED"] = StatusInTransitBack,
            ["LOST"] = StatusLost,
            ["DELIVERY_CANCELLED"] = StatusPendingReturn,
            ["DELIVERY_ERROR"] = StatusPendingReturn,
        };

        /// <summary>Hồ sơ còn đang chạy, tức là chưa chốt xong.</summary>
        private static readonly string[] OpenStatuses =
            { StatusPendingReturn, StatusInTransitBack, StatusReceived, StatusLost };

        private static readonly string[] ReceiveRoles =
            { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Chốt hướng xử lý động tới tiền (cước giao lại, bồi thường) nên chỉ OM và Admin.
        private static readonly string[] ResolveRoles =
            { "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        private static readonly string[] OpenRoles =
            { "WAREHOUSESTAFF", "STAFF", "SALE", "SALES", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<ParcelReturnService> _logger;

        public ParcelReturnService(IUnitOfWork unitOfWork, ILogger<ParcelReturnService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<string?> SyncFromCarrierAsync(
            Guid parcelId, string packageStatus, string? carrierStatusText, string? trackingCode)
        {
            if (!CarrierStatusToReason.TryGetValue(packageStatus ?? string.Empty, out var reason))
            {
                return null;
            }

            var caseStatus = CarrierStatusToCaseStatus[packageStatus!];
            var repo = _unitOfWork.Repository<ParcelReturn>();

            var existing = (await repo.GetAllAsync())
                .Where(r => r.ParcelId == parcelId && OpenStatuses.Contains(r.Status))
                .OrderByDescending(r => r.OpenedAt)
                .FirstOrDefault();

            if (existing != null)
            {
                // Hãng bắn lại đúng trạng thái cũ thì bỏ qua, đừng ghi đè mất dấu thời gian.
                if (string.Equals(existing.Status, caseStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return existing.ReturnCode;
                }

                // Kho đã cầm được hàng rồi thì tin muộn của hãng không được kéo lùi trạng thái.
                if (string.Equals(existing.Status, StatusReceived, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(caseStatus, StatusLost, StringComparison.OrdinalIgnoreCase))
                {
                    return existing.ReturnCode;
                }

                existing.Status = caseStatus;
                existing.Reason = reason;
                existing.CarrierStatusText = Truncate(carrierStatusText, 255);
                repo.Update(existing);

                _logger.LogInformation("Hồ sơ hàng hoàn {ReturnCode} chuyển sang {Status} theo tin hãng.",
                    existing.ReturnCode, caseStatus);
                return existing.ReturnCode;
            }

            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(parcelId);
            if (parcel == null)
            {
                _logger.LogWarning("Bỏ qua mở hồ sơ hàng hoàn: không tìm thấy kiện {ParcelId}.", parcelId);
                return null;
            }

            var created = await CreateCaseAsync(parcel, reason, caseStatus, carrierStatusText, trackingCode);

            _logger.LogInformation("Mở hồ sơ hàng hoàn {ReturnCode} cho kiện {PackageCode} — {Reason}.",
                created.ReturnCode, parcel.PackageCode, reason);

            return created.ReturnCode;
        }

        public async Task<ParcelReturnDto> OpenManuallyAsync(
            OpenParcelReturnRequest request, Guid userId, string userRole)
        {
            EnsureRole(userRole, OpenRoles, "mở hồ sơ hàng hoàn");

            var reason = NormalizeReason(request.Reason);
            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(request.ParcelId)
                ?? throw new NotFoundException($"Không tìm thấy kiện hàng với ID {request.ParcelId}");

            var duplicate = (await _unitOfWork.Repository<ParcelReturn>().GetAllAsync())
                .FirstOrDefault(r => r.ParcelId == request.ParcelId && OpenStatuses.Contains(r.Status));
            if (duplicate != null)
            {
                throw new ArgumentException(
                    $"Kiện {parcel.PackageCode} đã có hồ sơ hàng hoàn {duplicate.ReturnCode} đang mở.");
            }

            var caseStatus = string.Equals(reason, "LOST", StringComparison.OrdinalIgnoreCase)
                ? StatusLost
                : StatusPendingReturn;

            var created = await CreateCaseAsync(parcel, reason, caseStatus, null, null);
            created.Note = Truncate(request.Note, 500);

            await _unitOfWork.SaveChangesAsync();
            return await GetDetailAsync(created.Id);
        }

        public async Task<ParcelReturnListDto> ListAsync(string? status, Guid? warehouseId, string userRole)
        {
            var cases = (await _unitOfWork.Repository<ParcelReturn>().GetAllAsync()).ToList();

            if (warehouseId.HasValue)
            {
                // Hồ sơ chưa có kho (hàng chưa về tới nơi) vẫn phải hiện cho kho thấy mà chuẩn bị.
                cases = cases.Where(c => !c.WarehouseId.HasValue || c.WarehouseId == warehouseId.Value).ToList();
            }

            var normalizedStatus = status?.Trim().ToUpperInvariant();
            var filtered = string.IsNullOrWhiteSpace(normalizedStatus) || normalizedStatus == "ALL"
                ? cases
                : cases.Where(c => string.Equals(c.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase)).ToList();

            var items = await MapManyAsync(filtered.OrderByDescending(c => c.OpenedAt).ToList());

            var now = DateTime.UtcNow;
            return new ParcelReturnListDto
            {
                // Số liệu tính trên TOÀN BỘ hồ sơ chứ không theo bộ lọc: lọc đang xem "chờ
                // quyết định" mà ô "hàng mất" tụt về 0 thì nhân viên tưởng hết việc.
                Summary = new ParcelReturnSummaryDto
                {
                    Total = cases.Count,
                    AwaitingReturn = cases.Count(c =>
                        c.Status == StatusPendingReturn || c.Status == StatusInTransitBack),
                    AwaitingDecision = cases.Count(c => c.Status == StatusReceived),
                    Lost = cases.Count(c => c.Status == StatusLost),
                    Overdue = cases.Count(c =>
                        OpenStatuses.Contains(c.Status)
                        && c.OpenedAt.HasValue
                        && (now - c.OpenedAt.Value).TotalDays > OverdueDays),
                },
                Items = items,
            };
        }

        public async Task<ParcelReturnDto> GetDetailAsync(Guid returnId)
        {
            var found = await _unitOfWork.Repository<ParcelReturn>().GetByIdAsync(returnId)
                ?? throw new NotFoundException($"Không tìm thấy hồ sơ hàng hoàn với ID {returnId}");

            return (await MapManyAsync(new List<ParcelReturn> { found })).Single();
        }

        public async Task<ParcelReturnListDto> ListByOrderAsync(Guid orderId, Guid userId, string userRole)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {orderId}");

            // Khách chỉ xem đơn của mình. Hồ sơ hàng hoàn có tên, số điện thoại và địa chỉ nhà
            // người nhận, nên không có chốt này là đổi id trên URL xem được nhà người khác.
            var isStaff = !string.IsNullOrWhiteSpace(userRole)
                && !string.Equals(userRole.Trim(), "Customer", StringComparison.OrdinalIgnoreCase);

            if (!isStaff)
            {
                var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .FirstOrDefault(c => c.UserId == userId);

                if (customer == null || order.CustomerId != customer.Id)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền xem hồ sơ hàng hoàn của đơn này.");
                }
            }

            var cases = (await _unitOfWork.Repository<ParcelReturn>().GetAllAsync())
                .Where(r => r.OrderId == orderId)
                .OrderByDescending(r => r.OpenedAt)
                .ToList();

            return new ParcelReturnListDto
            {
                Summary = new ParcelReturnSummaryDto
                {
                    Total = cases.Count,
                    AwaitingReturn = cases.Count(c =>
                        c.Status == StatusPendingReturn || c.Status == StatusInTransitBack),
                    AwaitingDecision = cases.Count(c => c.Status == StatusReceived),
                    Lost = cases.Count(c => c.Status == StatusLost),
                },
                Items = await MapManyAsync(cases),
            };
        }

        public async Task<ParcelReturnDto> ConfirmReceivedAsync(
            Guid returnId, ReceiveParcelReturnRequest request, Guid userId, string userRole)
        {
            EnsureRole(userRole, ReceiveRoles, "xác nhận nhận lại hàng hoàn");

            var repo = _unitOfWork.Repository<ParcelReturn>();
            var found = await repo.GetByIdAsync(returnId)
                ?? throw new NotFoundException($"Không tìm thấy hồ sơ hàng hoàn với ID {returnId}");

            if (string.Equals(found.Status, StatusReceived, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Hồ sơ {found.ReturnCode} đã được xác nhận nhận lại trước đó.");
            }

            if (string.Equals(found.Status, StatusResolved, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Hồ sơ {found.ReturnCode} đã chốt xong, không nhận lại được nữa.");
            }

            if (string.Equals(found.Status, StatusLost, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Hồ sơ {found.ReturnCode} đang ghi nhận hàng thất lạc. Nếu hàng tìm lại được, "
                    + "cần sửa lại lý do trước khi nhận vào kho.");
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(found.ParcelId)
                ?? throw new NotFoundException($"Không tìm thấy kiện hàng của hồ sơ {found.ReturnCode}.");

            var now = DateTime.UtcNow;

            Guid? warehouseId = null;
            if (request?.BinId is Guid binId)
            {
                var bin = await _unitOfWork.Repository<Bin>().GetByIdAsync(binId)
                    ?? throw new ArgumentException("Ô kệ không tồn tại.");

                parcel.BinId = bin.Id;
                parcel.PutAwayTime = now;
                warehouseId = bin.WarehouseId;

                // Kiện quay lại kệ thì mở một lượt nằm kho mới. Không ghi nhật ký ở đây là phí
                // lưu kho của quãng hoàn về không có gì để tính.
                //
                // InventoryId là khoá ngoại BẮT BUỘC — thiếu nó thì insert vỡ ở tận DB. Kiện đã
                // từng nằm kho nên bao giờ cũng có dòng tồn kho; không có là dữ liệu hỏng và
                // phải dừng lại chứ không ghi bừa.
                var inventory = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                    .Where(i => i.ParcelId == parcel.Id)
                    .OrderByDescending(i => i.CreatedAt)
                    .FirstOrDefault()
                    ?? throw new ArgumentException(
                        $"Kiện {parcel.PackageCode} chưa có dữ liệu tồn kho nên không xếp kệ được. "
                        + "Vui lòng báo quản lý kiểm tra lại kiện này.");

                await _unitOfWork.Repository<InventoryMovement>().AddAsync(new InventoryMovement
                {
                    Id = Guid.NewGuid(),
                    InventoryId = inventory.Id,
                    ParcelId = parcel.Id,
                    FromBinId = parcel.BinId,
                    ToBinId = bin.Id,
                    MovementType = "INBOUND",
                    Reason = $"Nhận lại hàng hoàn {found.ReturnCode}",
                    Quantity = inventory.Quantity > 0 ? inventory.Quantity : 1,
                    MovedById = userId,
                    MovedAt = now,
                });

                // Kiện quay lại kệ thì dòng tồn kho phải sống lại, nếu không màn tồn kho vẫn
                // hiển thị đã xuất trong khi hàng đang nằm trên kệ.
                inventory.Status = "IN_STOCK";
                _unitOfWork.Repository<Inventory>().Update(inventory);
            }
            else
            {
                // Chưa xếp kệ: kiện nằm khu tạm, gỡ khỏi ô kệ cũ để tồn kho không hiểu nhầm.
                parcel.BinId = null;
            }

            parcel.PackageStatus = "RETURNED_TO_WAREHOUSE";
            parcelRepo.Update(parcel);

            found.Status = StatusReceived;
            found.ReceivedAt = now;
            found.ReceivedBy = userId;
            found.WarehouseId = warehouseId ?? found.WarehouseId;
            if (!string.IsNullOrWhiteSpace(request?.Note))
            {
                found.Note = Truncate(request!.Note, 500);
            }
            repo.Update(found);

            await NotifyAsync(found, parcel,
                $"Hàng hoàn về kho — hồ sơ {found.ReturnCode}",
                $"Kiện {parcel.PackageCode} đã về lại kho. Hồ sơ {found.ReturnCode} đang chờ chốt hướng xử lý.",
                includeCustomer: false);

            await _unitOfWork.SaveChangesAsync();
            return await GetDetailAsync(found.Id);
        }

        public async Task<ParcelReturnDto> ResolveAsync(
            Guid returnId, ResolveParcelReturnRequest request, Guid userId, string userRole)
        {
            EnsureRole(userRole, ResolveRoles, "chốt hướng xử lý hàng hoàn");

            var repo = _unitOfWork.Repository<ParcelReturn>();
            var found = await repo.GetByIdAsync(returnId)
                ?? throw new NotFoundException($"Không tìm thấy hồ sơ hàng hoàn với ID {returnId}");

            if (string.Equals(found.Status, StatusResolved, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Hồ sơ {found.ReturnCode} đã chốt xong trước đó.");
            }

            var resolution = NormalizeResolution(request.Resolution);

            // Hàng còn ngoài đường thì chưa chốt được — trừ trường hợp mất hẳn, lúc đó không
            // bao giờ có hàng để nhận nên phải cho bồi thường luôn.
            var isLost = string.Equals(found.Status, StatusLost, StringComparison.OrdinalIgnoreCase);
            if (!isLost && !string.Equals(found.Status, StatusReceived, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Chỉ chốt được sau khi kho xác nhận đã nhận lại hàng. "
                    + $"Hồ sơ {found.ReturnCode} đang ở trạng thái {DescribeStatus(found.Status)}.");
            }

            if (isLost && resolution != ResolutionCompensate)
            {
                throw new ArgumentException(
                    "Hàng đã thất lạc nên chỉ chốt được hướng bồi thường cho khách.");
            }

            string? feeBearer = null;
            if (resolution == ResolutionRedeliver)
            {
                feeBearer = NormalizeFeeBearer(request.FeeBearer)
                    ?? throw new ArgumentException(
                        "Giao lại thì phải chỉ rõ ai chịu cước: khách hàng hay công ty.");
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(found.ParcelId);

            if (parcel != null)
            {
                // Đưa kiện về đúng trạng thái để bước sau nhặt được nó:
                // giao lại và khách tự lấy đều cần kiện nằm ở diện sẵn sàng xuất kho.
                parcel.PackageStatus = resolution switch
                {
                    ResolutionRedeliver => "AWAITING_PICKUP",
                    ResolutionPickup => "AWAITING_PICKUP",
                    ResolutionDispose => "DISPOSED",
                    ResolutionCompensate => "LOST",
                    _ => parcel.PackageStatus,
                };
                parcelRepo.Update(parcel);
            }

            var now = DateTime.UtcNow;
            found.Status = StatusResolved;
            found.Resolution = resolution;
            found.FeeBearer = feeBearer;
            found.WaiveStorageFee = request.WaiveStorageFee;
            found.ResolutionNote = Truncate(request.ResolutionNote, 500);
            found.ResolvedAt = now;
            found.ResolvedBy = userId;
            repo.Update(found);

            await NotifyAsync(found, parcel,
                $"Đã chốt xử lý hàng hoàn — hồ sơ {found.ReturnCode}",
                BuildResolutionMessage(found, parcel, resolution, feeBearer),
                includeCustomer: true);

            await _unitOfWork.SaveChangesAsync();
            return await GetDetailAsync(found.Id);
        }

        // ---------------------------------------------------------------- helpers

        private async Task<ParcelReturn> CreateCaseAsync(
            Parcel parcel, string reason, string caseStatus, string? carrierStatusText, string? trackingCode)
        {
            var now = DateTime.UtcNow;

            // Kho đích của kiện: lần ngược qua phiếu giao gần nhất. Không ra thì để trống, kho
            // nào nhận lại hàng thì lúc bấm xác nhận sẽ tự điền.
            var waybill = string.IsNullOrWhiteSpace(trackingCode)
                ? (await _unitOfWork.Repository<DomesticWaybill>().GetAllAsync())
                    .Where(w => w.ParcelId == parcel.Id)
                    .OrderByDescending(w => w.CreatedAt)
                    .FirstOrDefault()
                : (await _unitOfWork.Repository<DomesticWaybill>().GetAllAsync())
                    .FirstOrDefault(w => w.ParcelId == parcel.Id && w.TrackingCode == trackingCode);

            var deliveryItem = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                .Where(i => i.ParcelId == parcel.Id)
                .OrderByDescending(i => i.Id)
                .FirstOrDefault();

            var entity = new ParcelReturn
            {
                Id = Guid.NewGuid(),
                ReturnCode = $"RTN-{now:yyyyMMdd}-{now.Ticks % 10000:0000}",
                ParcelId = parcel.Id,
                OrderId = parcel.ConsignmentId,
                DeliveryRequestId = deliveryItem?.DeliveryRequestId,
                TrackingCode = Truncate(trackingCode ?? waybill?.TrackingCode, 100),
                Reason = reason,
                CarrierStatusText = Truncate(carrierStatusText, 255),
                Status = caseStatus,
                OpenedAt = now,
                WaiveStorageFee = false,
            };

            await _unitOfWork.Repository<ParcelReturn>().AddAsync(entity);

            await NotifyAsync(entity, parcel,
                $"Giao không thành công — kiện {parcel.PackageCode}",
                $"Hãng vận chuyển báo \"{carrierStatusText ?? DescribeReason(reason)}\". "
                + $"Hệ thống đã mở hồ sơ hàng hoàn {entity.ReturnCode}, chờ hàng về kho để xử lý tiếp.",
                includeCustomer: true);

            return entity;
        }

        /// <summary>
        /// Báo cho người phụ trách đơn, và cho khách khi đó là tin khách cần biết.
        /// Không báo khách ở bước kho nhận lại hàng — đó là việc nội bộ, khách chỉ quan tâm
        /// lúc giao hỏng và lúc đã chốt xử lý tiếp.
        /// </summary>
        private async Task NotifyAsync(
            ParcelReturn entity, Parcel? parcel, string title, string content, bool includeCustomer)
        {
            if (!entity.OrderId.HasValue) return;

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(entity.OrderId.Value);
            if (order == null) return;

            var recipients = new List<Guid>();
            if (order.CreatedById.HasValue) recipients.Add(order.CreatedById.Value);

            if (includeCustomer && order.CustomerId.HasValue)
            {
                var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value);
                if (customer?.UserId is Guid uid && uid != Guid.Empty) recipients.Add(uid);
            }

            var repo = _unitOfWork.Repository<Notification>();
            foreach (var userId in recipients.Distinct())
            {
                await repo.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = title,
                    Content = content,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }

        private static string BuildResolutionMessage(
            ParcelReturn entity, Parcel? parcel, string resolution, string? feeBearer)
        {
            var code = parcel?.PackageCode ?? entity.ReturnCode;

            return resolution switch
            {
                ResolutionRedeliver => string.Equals(feeBearer, "CUSTOMER", StringComparison.OrdinalIgnoreCase)
                    ? $"Kiện {code} sẽ được giao lại. Cước giao lại tính cho khách hàng."
                    : $"Kiện {code} sẽ được giao lại, công ty chịu cước.",
                ResolutionPickup => $"Kiện {code} đang ở kho, khách vui lòng đến lấy trực tiếp.",
                ResolutionDispose => $"Kiện {code} được chốt thanh lý. Lý do: {entity.ResolutionNote}",
                ResolutionCompensate => $"Kiện {code} không tìm lại được. Công ty sẽ liên hệ để xử lý bồi thường.",
                _ => $"Hồ sơ {entity.ReturnCode} đã được chốt.",
            };
        }

        private async Task<List<ParcelReturnDto>> MapManyAsync(List<ParcelReturn> cases)
        {
            if (cases.Count == 0) return new List<ParcelReturnDto>();

            var parcelIds = cases.Select(c => c.ParcelId).ToHashSet();
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToDictionary(p => p.Id);

            var orderIds = cases.Where(c => c.OrderId.HasValue).Select(c => c.OrderId!.Value).ToHashSet();
            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id))
                .ToDictionary(o => o.Id);

            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);
            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id);
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);
            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id);

            var deliveryIds = cases.Where(c => c.DeliveryRequestId.HasValue)
                .Select(c => c.DeliveryRequestId!.Value).ToHashSet();
            var deliveries = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync())
                .Where(d => deliveryIds.Contains(d.Id))
                .ToDictionary(d => d.Id);

            var now = DateTime.UtcNow;
            var result = new List<ParcelReturnDto>();

            foreach (var entity in cases)
            {
                parcels.TryGetValue(entity.ParcelId, out var parcel);

                Order? order = null;
                if (entity.OrderId.HasValue) orders.TryGetValue(entity.OrderId.Value, out order);

                Customer? customer = null;
                if (order?.CustomerId is Guid cid) customers.TryGetValue(cid, out customer);

                DeliveryRequest? delivery = null;
                if (entity.DeliveryRequestId.HasValue) deliveries.TryGetValue(entity.DeliveryRequestId.Value, out delivery);

                string? binCode = null;
                if (parcel?.BinId is Guid bid && bins.TryGetValue(bid, out var bin)) binCode = bin.BinCode;

                string? warehouseName = null;
                if (entity.WarehouseId is Guid wid && warehouses.TryGetValue(wid, out var wh))
                {
                    warehouseName = wh.WarehouseName;
                }

                string? NameOf(Guid? id) =>
                    id.HasValue && users.TryGetValue(id.Value, out var u) ? u.FullName : null;

                var address = delivery == null
                    ? null
                    : string.Join(", ", new[] { delivery.AddressDetail, delivery.Ward, delivery.District, delivery.Province }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));

                result.Add(new ParcelReturnDto
                {
                    ReturnId = entity.Id,
                    ReturnCode = entity.ReturnCode ?? string.Empty,
                    ParcelId = entity.ParcelId,
                    PackageCode = parcel?.PackageCode ?? string.Empty,
                    OrderId = entity.OrderId,
                    OrderCode = order?.ConsignmentCode,
                    CustomerName = customer?.FullName,
                    CustomerPhone = customer?.Phone,
                    DeliveryRequestId = entity.DeliveryRequestId,
                    DeliveryCode = delivery?.DeliveryCode,
                    TrackingCode = entity.TrackingCode,
                    ReceiverName = delivery?.ReceiverName,
                    ReceiverPhone = delivery?.ReceiverPhone,
                    DeliveryAddress = string.IsNullOrWhiteSpace(address) ? null : address,
                    Reason = entity.Reason ?? string.Empty,
                    ReasonText = DescribeReason(entity.Reason),
                    CarrierStatusText = entity.CarrierStatusText,
                    Status = entity.Status ?? string.Empty,
                    StatusText = DescribeStatus(entity.Status),
                    WarehouseId = entity.WarehouseId,
                    WarehouseName = warehouseName,
                    BinCode = binCode,
                    OpenedAt = entity.OpenedAt,
                    ReceivedAt = entity.ReceivedAt,
                    ReceivedByName = NameOf(entity.ReceivedBy),
                    Resolution = entity.Resolution,
                    ResolutionText = DescribeResolution(entity.Resolution),
                    FeeBearer = entity.FeeBearer,
                    FeeBearerText = DescribeFeeBearer(entity.FeeBearer),
                    WaiveStorageFee = entity.WaiveStorageFee,
                    ResolvedAt = entity.ResolvedAt,
                    ResolvedByName = NameOf(entity.ResolvedBy),
                    Note = entity.Note,
                    ResolutionNote = entity.ResolutionNote,
                    DaysOpen = entity.OpenedAt.HasValue
                        ? Math.Max(0, (int)Math.Floor((now - entity.OpenedAt.Value).TotalDays))
                        : 0,
                });
            }

            return result;
        }

        private static void EnsureRole(string userRole, string[] allowed, string action)
        {
            var normalized = userRole?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || !allowed.Contains(normalized))
            {
                throw new UnauthorizedAccessException($"Vai trò hiện tại không được phép {action}.");
            }
        }

        private static string NormalizeReason(string? value) =>
            value?.Trim().ToUpperInvariant() switch
            {
                "FAILED_DELIVERY" => "FAILED_DELIVERY",
                "RETURNING" => "RETURNING",
                "LOST" => "LOST",
                "CANCELLED" => "CANCELLED",
                _ => throw new ArgumentException(
                    "Lý do hoàn không hợp lệ. Hợp lệ: FAILED_DELIVERY, RETURNING, LOST, CANCELLED."),
            };

        private static string NormalizeResolution(string? value) =>
            value?.Trim().ToUpperInvariant() switch
            {
                ResolutionRedeliver => ResolutionRedeliver,
                ResolutionPickup => ResolutionPickup,
                ResolutionDispose => ResolutionDispose,
                ResolutionCompensate => ResolutionCompensate,
                _ => throw new ArgumentException(
                    "Hướng xử lý không hợp lệ. Hợp lệ: REDELIVER, CUSTOMER_PICKUP, DISPOSE, COMPENSATE."),
            };

        private static string? NormalizeFeeBearer(string? value) =>
            value?.Trim().ToUpperInvariant() switch
            {
                "CUSTOMER" => "CUSTOMER",
                "COMPANY" => "COMPANY",
                _ => null,
            };

        private static string DescribeReason(string? reason) =>
            reason?.Trim().ToUpperInvariant() switch
            {
                "FAILED_DELIVERY" => "Giao không thành công",
                "RETURNING" => "Hãng đang chuyển hoàn",
                "LOST" => "Hãng báo thất lạc",
                "CANCELLED" => "Đơn giao bị huỷ",
                _ => "Không rõ",
            };

        private static string DescribeStatus(string? status) =>
            status?.Trim().ToUpperInvariant() switch
            {
                StatusPendingReturn => "Chờ hàng quay về",
                StatusInTransitBack => "Đang trên đường về kho",
                StatusReceived => "Đã về kho, chờ chốt xử lý",
                StatusResolved => "Đã chốt xử lý",
                StatusLost => "Hàng thất lạc",
                _ => status ?? string.Empty,
            };

        private static string? DescribeResolution(string? resolution) =>
            resolution?.Trim().ToUpperInvariant() switch
            {
                ResolutionRedeliver => "Giao lại cho khách",
                ResolutionPickup => "Khách tự đến kho lấy",
                ResolutionDispose => "Thanh lý",
                ResolutionCompensate => "Bồi thường cho khách",
                _ => null,
            };

        private static string? DescribeFeeBearer(string? feeBearer) =>
            feeBearer?.Trim().ToUpperInvariant() switch
            {
                "CUSTOMER" => "Khách hàng chịu cước giao lại",
                "COMPANY" => "Công ty chịu cước giao lại",
                _ => null,
            };

        private static string? Truncate(string? value, int max)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }
    }
}
