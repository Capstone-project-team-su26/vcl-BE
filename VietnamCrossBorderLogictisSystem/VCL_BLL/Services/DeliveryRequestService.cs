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
    /// <inheritdoc cref="IDeliveryRequestService"/>
    public class DeliveryRequestService : IDeliveryRequestService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IGoshipService _goshipService;
        private readonly INotificationService _notificationService;

        // Lập phiếu: sale là người liên hệ khách, nắm địa chỉ giao và biết khách trả tiền chưa.
        private static readonly string[] CreateRoles = { "SALE", "SALES", "ADMIN" };

        // Duyệt phiếu: chỉ OM.
        private static readonly string[] ApproveRoles = { "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Đặt đơn vị giao: kho là người gom hàng thật.
        private static readonly string[] DispatchRoles =
            { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        public const string StatusPending = "DELIVERY_PENDING";
        public const string StatusApproved = "DELIVERY_APPROVED";
        public const string StatusRejected = "DELIVERY_REJECTED";
        public const string StatusDispatched = "DELIVERY_DISPATCHED";

        private const string PickFromShelf = "FROM_SHELF";
        private const string PickFromStaging = "FROM_STAGING";

        private const string ReceivedAtDestination = "RECEIVED_AT_DESTINATION";
        private const string Stored = "STORED";
        private const string DirectDelivery = "DIRECT_DELIVERY";

        // Trùng đúng giá trị mà webhook bên giao hàng dùng cho mã 900/901, để hai đường không
        // sinh ra hai cách gọi khác nhau cho cùng một tình trạng.
        private const string AwaitingPickup = "AWAITING_PICKUP";

        public DeliveryRequestService(
            IUnitOfWork unitOfWork,
            IGoshipService goshipService,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _goshipService = goshipService;
            _notificationService = notificationService;
        }

        public async Task<DeliveryRequestDto> CreateAsync(Guid userId, string userRole, CreateDeliveryRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu yêu cầu giao hàng không được để trống.");

            EnsureRole(userRole, CreateRoles, "Chỉ Sale được lập yêu cầu giao hàng.");

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(request.OrderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {request.OrderId}");

            await EnsureFinalPaymentSettledAsync(order);

            var orderParcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == order.Id)
                .ToList();

            var alreadyRequested = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                .Select(i => i.ParcelId)
                .ToHashSet();

            List<Parcel> targets;
            if (request.ParcelIds != null && request.ParcelIds.Count > 0)
            {
                targets = new List<Parcel>();
                foreach (var pid in request.ParcelIds.Distinct())
                {
                    var parcel = orderParcels.FirstOrDefault(p => p.Id == pid)
                        ?? throw new ArgumentException($"Kiện {pid} không thuộc đơn hàng này.");

                    EnsureParcelReadyToDeliver(parcel, alreadyRequested);
                    targets.Add(parcel);
                }
            }
            else
            {
                targets = orderParcels
                    .Where(p => IsReadyToDeliver(p) && !alreadyRequested.Contains(p.Id))
                    .ToList();

                if (targets.Count == 0)
                {
                    throw new ArgumentException(
                        "Đơn này chưa có kiện nào sẵn sàng giao. Kiện phải được kho mở lô xác nhận " +
                        "(hàng giao thẳng) hoặc đã nhập kho VN xếp lên kệ thì mới lập phiếu giao được.");
                }
            }

            var now = DateTime.UtcNow;
            var delivery = new DeliveryRequest
            {
                Id = Guid.NewGuid(),
                DeliveryCode = $"DLV-{now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                OrderId = order.Id,
                Status = StatusPending,
                ReceiverName = request.ReceiverName.Trim(),
                ReceiverPhone = request.ReceiverPhone.Trim(),
                AddressDetail = request.AddressDetail.Trim(),
                Province = request.Province.Trim(),
                District = request.District.Trim(),
                Ward = request.Ward.Trim(),
                ScheduledDate = request.ScheduledDate,
                Note = request.Note,
                CreatedBy = userId,
                CreatedAt = now
            };
            await _unitOfWork.Repository<DeliveryRequest>().AddAsync(delivery);

            var itemRepo = _unitOfWork.Repository<DeliveryRequestItem>();
            foreach (var parcel in targets)
            {
                await itemRepo.AddAsync(new DeliveryRequestItem
                {
                    Id = Guid.NewGuid(),
                    DeliveryRequestId = delivery.Id,
                    ParcelId = parcel.Id,
                    // Kiện đang nằm ô kệ thì kho phải đi nhặt trên kệ; kiện giao thẳng nằm ở
                    // khu tạm, không có mã ô nào để đi tới.
                    PickSource = parcel.BinId.HasValue ? PickFromShelf : PickFromStaging
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // Thông báo cho Operations & Kho có yêu cầu giao hàng mới cần duyệt
            await _notificationService.NotifyRolesAsync(
                new[] { "OperationsManager", "Operations", "Admin", "WarehouseStaff", "Warehouse" },
                "Có yêu cầu giao hàng mới cần duyệt",
                $"Yêu cầu giao hàng (Mã: {delivery.DeliveryCode}) vừa được tạo cho đơn {order.ConsignmentCode ?? order.Id.ToString()}. Vui lòng xem xét duyệt.",
                excludeUserId: userId);

            return await BuildDtoAsync(delivery);
        }

        public async Task<DeliveryRequestDto> UpdateStatusAsync(
            Guid deliveryRequestId, Guid userId, string userRole, UpdateDeliveryRequestStatusDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            EnsureRole(userRole, ApproveRoles, "Chỉ Operations Manager được duyệt yêu cầu giao hàng.");

            var repo = _unitOfWork.Repository<DeliveryRequest>();
            var delivery = await repo.GetByIdAsync(deliveryRequestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu giao hàng với ID {deliveryRequestId}");

            var target = request.Status?.Trim().ToUpperInvariant();
            if (target != StatusApproved && target != StatusRejected)
            {
                throw new ArgumentException($"Trạng thái không hợp lệ. Chỉ nhận {StatusApproved} hoặc {StatusRejected}.");
            }

            if (!string.Equals(delivery.Status, StatusPending, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Phiếu đã được xử lý trước đó (hiện tại: {delivery.Status}), không duyệt lại được.");
            }

            if (target == StatusRejected && string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                throw new ArgumentException("Từ chối yêu cầu giao hàng thì bắt buộc ghi lý do.");
            }

            delivery.Status = target;
            delivery.ApprovedBy = userId;
            delivery.ApprovedAt = DateTime.UtcNow;
            delivery.RejectionReason = target == StatusRejected ? request.RejectionReason!.Trim() : null;
            repo.Update(delivery);

            await _unitOfWork.SaveChangesAsync();

            // Bắn thông báo kết quả duyệt yêu cầu giao hàng
            if (target == StatusApproved)
            {
                await _notificationService.NotifyRolesAsync(
                    new[] { "WarehouseStaff", "Warehouse", "Delivery", "Staff", "Sale", "Sales" },
                    "Yêu cầu giao hàng đã được duyệt",
                    $"Yêu cầu giao hàng (Mã: {delivery.DeliveryCode}) đã được duyệt. Sẵn sàng tạo vận đơn và xuất kho.",
                    excludeUserId: userId);
            }
            else if (target == StatusRejected)
            {
                await _notificationService.NotifyRolesAsync(
                    new[] { "Sale", "Sales", "Admin" },
                    "Yêu cầu giao hàng bị từ chối",
                    $"Yêu cầu giao hàng (Mã: {delivery.DeliveryCode}) đã bị từ chối. Lý do: {delivery.RejectionReason}",
                    excludeUserId: userId);
            }

            return await BuildDtoAsync(delivery);
        }

        public async Task<DeliveryRequestDto> DispatchAsync(
            Guid deliveryRequestId, Guid userId, string userRole, DispatchDeliveryRequestDto request)
        {
            EnsureRole(userRole, DispatchRoles, "Chỉ nhân viên kho hoặc Operations được đặt đơn vị giao hàng.");

            var repo = _unitOfWork.Repository<DeliveryRequest>();
            var delivery = await repo.GetByIdAsync(deliveryRequestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu giao hàng với ID {deliveryRequestId}");

            if (string.Equals(delivery.Status, StatusDispatched, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Phiếu này đã đặt giao rồi (mã vận đơn {delivery.CarrierTrackingCode}).");
            }

            if (!string.Equals(delivery.Status, StatusApproved, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Phiếu phải được Operations Manager duyệt mới đặt giao được (hiện tại: {delivery.Status}).");
            }

            var items = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                .Where(i => i.DeliveryRequestId == delivery.Id)
                .ToList();
            var parcelIds = items.Select(i => i.ParcelId).ToList();

            // Gọi sang bên giao hàng TRƯỚC khi đụng vào dữ liệu của mình. Họ từ chối thì phiếu
            // giữ nguyên trạng thái đã duyệt, kho bấm lại được — không để cảnh ô kệ đã nhả mà
            // đơn giao thì chưa đặt được.
            GoshipOrderResponseData? goshipOrder;
            try
            {
                goshipOrder = await _goshipService.CreateOrderAsync(new GoshipOrderRequest
                {
                    OrderId = delivery.OrderId,
                    ParcelIds = parcelIds
                }, request?.Token ?? string.Empty);
            }
            catch (Exception ex)
            {
                // Lỗi hay gặp nhất là chưa cấu hình token nên bên kia trả về trang HTML, đọc
                // JSON là vỡ. Nói thẳng ra để kho khỏi tưởng hệ thống mình hỏng.
                throw new InvalidOperationException(
                    "Không gọi được đơn vị giao hàng. Kiểm tra token GoShip đã cấu hình chưa, " +
                    $"hoặc thử lại sau. Chi tiết: {ex.Message}");
            }

            var trackingCode = !string.IsNullOrWhiteSpace(goshipOrder?.Code) ? goshipOrder!.Code : goshipOrder?.Id;

            // Đánh dấu kiện đã rời kho đi giao. Kiện nhặt từ kệ còn được nhả ô và ghi nhật ký
            // xuất kho — mốc DỪNG đồng hồ phí lưu kho tại kho VN. Thiếu bước này thì kiện đã đi
            // giao rồi mà vẫn bị tính tiền nằm kho, và ô kệ mãi không trống ra.
            await MarkParcelsDispatchedAsync(items, userId);

            var now = DateTime.UtcNow;
            delivery.Status = StatusDispatched;
            delivery.CarrierTrackingCode = trackingCode;
            delivery.DispatchedAt = now;
            repo.Update(delivery);

            await _unitOfWork.SaveChangesAsync();
            return await BuildDtoAsync(delivery);
        }

        public async Task<DeliveryRequestDto> GetByIdAsync(Guid deliveryRequestId)
        {
            var delivery = await _unitOfWork.Repository<DeliveryRequest>().GetByIdAsync(deliveryRequestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu giao hàng với ID {deliveryRequestId}");

            return await BuildDtoAsync(delivery);
        }

        public async Task<List<DeliveryRequestDto>> GetListAsync(string? status, Guid? orderId)
        {
            var all = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync()).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var s = status.Trim();
                all = all.Where(d => string.Equals(d.Status, s, StringComparison.OrdinalIgnoreCase));
            }

            if (orderId.HasValue && orderId.Value != Guid.Empty)
            {
                all = all.Where(d => d.OrderId == orderId.Value);
            }

            var result = new List<DeliveryRequestDto>();
            foreach (var delivery in all.OrderByDescending(d => d.CreatedAt ?? DateTime.MinValue))
            {
                result.Add(await BuildDtoAsync(delivery));
            }
            return result;
        }

        // ----------------------------------------------------------------------------------

        private static void EnsureRole(string userRole, string[] allowed, string message)
        {
            var ok = !string.IsNullOrWhiteSpace(userRole)
                && allowed.Contains(userRole.Trim().ToUpperInvariant());
            if (!ok) throw new UnauthorizedAccessException(message);
        }

        /// <summary>
        /// Kiện sẵn sàng giao khi ở một trong hai chỗ: vừa được kho mở lô kiểm đếm và phân loại
        /// giao thẳng, hoặc đã nhập kho VN nằm trên kệ chờ khách nhận.
        /// </summary>
        private static bool IsReadyToDeliver(Parcel parcel)
        {
            var status = parcel.PackageStatus?.Trim().ToUpperInvariant();

            if (status == Stored) return true;

            return status == ReceivedAtDestination
                && string.Equals(parcel.DestinationHandling, DirectDelivery, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureParcelReadyToDeliver(Parcel parcel, HashSet<Guid> alreadyRequested)
        {
            var code = parcel.PackageCode ?? parcel.Id.ToString();

            if (alreadyRequested.Contains(parcel.Id))
            {
                throw new ArgumentException($"Kiện {code} đã nằm trong một yêu cầu giao hàng khác.");
            }

            if (!IsReadyToDeliver(parcel))
            {
                throw new ArgumentException(
                    $"Kiện {code} chưa sẵn sàng giao (trạng thái {parcel.PackageStatus}, " +
                    $"hướng xử lý {parcel.DestinationHandling ?? "chưa phân loại"}). " +
                    "Kiện giao thẳng phải được kho mở lô xác nhận, kiện gửi kho phải xếp lên kệ xong.");
            }
        }

        /// <summary>
        /// Chưa thu đủ tiền thì không giao. Chốt này trùng ý với chốt bên GoShip nhưng phải có ở
        /// đây, vì sale cần biết ngay lúc lập phiếu chứ không phải đợi tới lúc kho bấm đặt giao
        /// mới báo thiếu tiền — lúc đó cả dây đã kẹt.
        /// </summary>
        private async Task EnsureFinalPaymentSettledAsync(Order order)
        {
            var quotations = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == order.Id)
                .Select(q => q.Id)
                .ToHashSet();

            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotations.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();

            var settled = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Where(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value))
                .Any(p => string.Equals(p.InstallmentType, "FINAL_PAYMENT", StringComparison.OrdinalIgnoreCase)
                          && p.PaymentStatus?.Trim().ToUpperInvariant() is "PAID" or "SUCCESS" or "COMPLETED");

            if (!settled)
            {
                throw new ArgumentException(
                    $"Đơn {order.ConsignmentCode} chưa tất toán xong. Khách trả nốt đợt cuối rồi mới lập được yêu cầu giao hàng.");
            }
        }

        /// <summary>
        /// Đánh dấu kiện đã rời kho đi giao: mọi kiện trong phiếu chuyển sang chờ shipper lấy,
        /// riêng kiện nhặt trên kệ thì nhả ô và ghi nhật ký xuất kho.
        /// <para>
        /// Phải đặt trạng thái ngay tại đây chứ không đợi webhook bên giao hàng bắn về. Sandbox
        /// không bắn webhook, mà thật thì cũng có độ trễ — trong khoảng đó kiện đã rời kho rồi
        /// mà màn hình vẫn báo "đang lưu kho", nhìn vào là thấy vô lý. Webhook về sau ghi đè
        /// bằng trạng thái thật (đang lấy hàng, đã giao...) nên không đá nhau.
        /// </para>
        /// </summary>
        private async Task MarkParcelsDispatchedAsync(List<DeliveryRequestItem> items, Guid userId)
        {
            if (items.Count == 0) return;

            var allParcelIds = items.Select(i => i.ParcelId).ToHashSet();
            var shelfParcelIds = items
                .Where(i => string.Equals(i.PickSource, PickFromShelf, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.ParcelId)
                .ToHashSet();

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var binRepo = _unitOfWork.Repository<Bin>();
            var movementRepo = _unitOfWork.Repository<InventoryMovement>();

            var parcels = (await parcelRepo.GetAllAsync()).Where(p => allParcelIds.Contains(p.Id)).ToList();
            var bins = (await binRepo.GetAllAsync()).ToDictionary(b => b.Id);
            var now = DateTime.UtcNow;

            foreach (var parcel in parcels)
            {
                // Kiện nhặt trên kệ: nhả ô kệ và ghi nhật ký xuất kho. Đây là mốc DỪNG đồng hồ
                // phí lưu kho tại kho VN.
                if (shelfParcelIds.Contains(parcel.Id) && parcel.BinId.HasValue)
                {
                    var binId = parcel.BinId.Value;

                    if (bins.TryGetValue(binId, out var bin))
                    {
                        var released = (parcel.Length ?? 0m) * (parcel.Width ?? 0m) * (parcel.Height ?? 0m);
                        var remaining = (bin.CurrentVolume ?? 0m) - released;
                        bin.CurrentVolume = remaining < 0m ? 0m : remaining;
                        binRepo.Update(bin);
                    }

                    await movementRepo.AddAsync(new InventoryMovement
                    {
                        Id = Guid.NewGuid(),
                        ParcelId = parcel.Id,
                        // Cột to_bin_id là NOT NULL nên ghi lại chính ô vừa rời đi; MovementType
                        // OUTBOUND đã đủ để phân biệt với điều chuyển nội bộ.
                        ToBinId = binId,
                        MovementType = "OUTBOUND",
                        Reason = "Xuất kho giao khách theo yêu cầu giao hàng",
                        MovedById = userId,
                        MovedAt = now
                    });

                    parcel.BinId = null;
                }

                parcel.PackageStatus = AwaitingPickup;
                parcelRepo.Update(parcel);
            }
        }

        private static string DescribeStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            StatusPending => "Chờ duyệt",
            StatusApproved => "Đã duyệt",
            StatusRejected => "Bị từ chối",
            StatusDispatched => "Đã đặt giao",
            _ => status ?? string.Empty
        };

        private async Task<DeliveryRequestDto> BuildDtoAsync(DeliveryRequest delivery)
        {
            var items = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                .Where(i => i.DeliveryRequestId == delivery.Id)
                .ToList();
            var parcelIds = items.Select(i => i.ParcelId).ToHashSet();

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToDictionary(p => p.Id);

            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id);
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);

            var order = delivery.OrderId.HasValue
                ? await _unitOfWork.Repository<Order>().GetByIdAsync(delivery.OrderId.Value)
                : null;

            Customer? customer = null;
            if (order?.CustomerId is Guid cid)
            {
                customer = (await _unitOfWork.Repository<Customer>().GetAllAsync()).FirstOrDefault(c => c.Id == cid);
            }

            var lines = new List<DeliveryRequestParcelDto>();
            foreach (var item in items)
            {
                parcels.TryGetValue(item.ParcelId, out var parcel);

                Bin? bin = null;
                if (parcel?.BinId is Guid bid) bins.TryGetValue(bid, out bin);

                lines.Add(new DeliveryRequestParcelDto
                {
                    ParcelId = item.ParcelId,
                    PackageCode = parcel?.PackageCode ?? string.Empty,
                    PackageStatus = parcel?.PackageStatus ?? string.Empty,
                    PickSource = item.PickSource,
                    BinId = parcel?.BinId,
                    BinCode = bin?.BinCode
                });
            }

            string? NameOf(Guid? id) => id.HasValue && users.TryGetValue(id.Value, out var u) ? u.FullName : null;

            var addressParts = new[] { delivery.AddressDetail, delivery.Ward, delivery.District, delivery.Province }
                .Where(s => !string.IsNullOrWhiteSpace(s));

            return new DeliveryRequestDto
            {
                DeliveryRequestId = delivery.Id,
                DeliveryCode = delivery.DeliveryCode ?? string.Empty,
                OrderId = delivery.OrderId,
                OrderCode = order?.ConsignmentCode,
                CustomerName = customer?.FullName,
                Status = delivery.Status ?? string.Empty,
                StatusText = DescribeStatus(delivery.Status),
                ReceiverName = delivery.ReceiverName ?? string.Empty,
                ReceiverPhone = delivery.ReceiverPhone ?? string.Empty,
                AddressDetail = delivery.AddressDetail ?? string.Empty,
                Province = delivery.Province ?? string.Empty,
                District = delivery.District ?? string.Empty,
                Ward = delivery.Ward ?? string.Empty,
                FullAddress = string.Join(", ", addressParts),
                ScheduledDate = delivery.ScheduledDate,
                Note = delivery.Note,
                RejectionReason = delivery.RejectionReason,
                CarrierTrackingCode = delivery.CarrierTrackingCode,
                DispatchedAt = delivery.DispatchedAt,
                CreatedByName = NameOf(delivery.CreatedBy),
                CreatedAt = delivery.CreatedAt,
                ApprovedByName = NameOf(delivery.ApprovedBy),
                ApprovedAt = delivery.ApprovedAt,
                TotalParcels = lines.Count,
                HasShelfPick = lines.Any(l => string.Equals(l.PickSource, PickFromShelf, StringComparison.OrdinalIgnoreCase)),
                Parcels = lines
            };
        }
    }
}
