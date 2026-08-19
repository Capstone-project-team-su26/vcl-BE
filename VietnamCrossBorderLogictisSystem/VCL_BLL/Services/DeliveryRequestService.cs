using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Enums;
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
        private readonly IConfiguration _configuration;
        private readonly IStorageFeeService _storageFeeService;

        /// <summary>Một chỗ duy nhất quyết định "đơn này đã tất toán chưa".</summary>
        private readonly ISettlementQueryService _settlementQueryService;
        private readonly ILogger<DeliveryRequestService> _logger;

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
            INotificationService notificationService,
            IConfiguration configuration,
            IStorageFeeService storageFeeService,
            ISettlementQueryService settlementQueryService,
            ILogger<DeliveryRequestService> logger)
        {
            _unitOfWork = unitOfWork;
            _goshipService = goshipService;
            _notificationService = notificationService;
            _configuration = configuration;
            _storageFeeService = storageFeeService;
            _settlementQueryService = settlementQueryService;
            _logger = logger;
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

            // Chỉ phiếu CÒN HIỆU LỰC mới giữ chỗ kiện. Phiếu bị OM từ chối phải nhả kiện ra —
            // không thì hàng đợi của Sale trả đơn về (nó đã loại phiếu bị từ chối) nhưng bấm nút
            // lại báo "kiện đã nằm trong một yêu cầu giao hàng khác", Sale không có đường đi tiếp.
            var rejectedDeliveryIds = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync())
                .Where(d => string.Equals(d.Status, StatusRejected, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Id)
                .ToHashSet();

            var alreadyRequested = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                .Where(i => !rejectedDeliveryIds.Contains(i.DeliveryRequestId))
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

            // Token là bí mật của server, app kho không cầm. Cho phép truyền tay khi chạy thử,
            // còn lại lấy từ cấu hình — nếu không thì môi trường đã nạp token vẫn không đặt được đơn.
            var token = !string.IsNullOrWhiteSpace(request?.Token)
                ? request!.Token!.Trim()
                : _configuration["GoshipSettings:Token"];

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình GoShip Access Token trong hệ thống. Nạp GoshipSettings:Token rồi thử lại.");
            }

            await EnsureStorageFeeSettledAsync(delivery);

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToList();

            var shipment = await BuildGoshipShipmentAsync(delivery, parcels);

            // GoShip chỉ hiểu mã số, phiếu thì lưu tên — đổi trước khi gửi, cả hai đầu.
            try
            {
                // Hai đầu địa chỉ không phụ thuộc nhau mà mỗi đầu tốn ba lượt gọi sang GoShip
                // (tỉnh → quận → phường). Chạy nối tiếp là gấp đôi thời gian chờ cho đúng một
                // việc; danh mục có cache 12 giờ nên chỉ lần đầu sau khi khởi động mới đắt.
                var toTask = _goshipService.ResolveAddressCodesAsync(
                    delivery.Province ?? string.Empty,
                    delivery.District ?? string.Empty,
                    delivery.Ward ?? string.Empty,
                    token);

                var fromTask = _goshipService.ResolveAddressCodesAsync(
                    shipment.AddressFrom.City,
                    shipment.AddressFrom.District,
                    shipment.AddressFrom.Ward,
                    token);

                await Task.WhenAll(toTask, fromTask);

                var toCodes = await toTask;
                shipment.AddressTo.City = toCodes.CityId;
                shipment.AddressTo.District = toCodes.DistrictId;
                shipment.AddressTo.Ward = toCodes.WardId;

                var fromCodes = await fromTask;
                shipment.AddressFrom.City = fromCodes.CityId;
                shipment.AddressFrom.District = fromCodes.DistrictId;
                shipment.AddressFrom.Ward = fromCodes.WardId;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Địa chỉ giao hàng không khớp danh mục của đơn vị vận chuyển. {ex.Message}");
            }

            // Gọi sang bên giao hàng TRƯỚC khi đụng vào dữ liệu của mình. Họ từ chối thì phiếu
            // giữ nguyên trạng thái đã duyệt, kho bấm lại được — không để cảnh ô kệ đã nhả mà
            // đơn giao thì chưa đặt được.
            GoshipOrderResponseData? goshipOrder;
            try
            {
                // GoShip bắt chọn sẵn gói cước: phải hỏi bảng giá của đúng tuyến này rồi mới
                // tạo được vận đơn. Lấy gói rẻ nhất vì phiếu không cho người dùng chọn hãng.
                shipment.Rate = await ResolveCheapestRateAsync(shipment, token);

                goshipOrder = await _goshipService.CreateOrderAsync(new GoshipOrderRequest
                {
                    Shipment = shipment,
                    OrderId = delivery.OrderId,
                    ParcelIds = parcelIds
                }, token);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Không gọi được đơn vị giao hàng. Chi tiết: {ex.Message}");
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

        /// <summary>Nhãn tiếng Việt cho trạng thái kiện, viết theo cách khách hiểu được.</summary>
        private static string DescribeParcelStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            "ARRIVED_DESTINATION" => "Đã về kho Việt Nam, chờ kiểm đếm",
            "RECEIVED_AT_DESTINATION" => "Kho đã kiểm đếm xong",
            "STORED" => "Đang lưu tại kho Việt Nam",
            "AWAITING_PICKUP" => "Chờ đơn vị giao tới lấy",
            "PICKING_UP" => "Đơn vị giao đang lấy hàng",
            "DELIVERED" => "Đã giao tới bạn",
            "PARTIALLY_DELIVERED" => "Đã giao một phần",
            _ => "Đang xử lý",
        };

        private static string? DescribeHandling(string? handling) => handling?.Trim().ToUpperInvariant() switch
        {
            "DIRECT_DELIVERY" => "Giao thẳng tới bạn",
            "STORE_AT_VN" => "Gửi lại kho Việt Nam",
            _ => null,
        };

        /// <summary>
        /// Nguyện vọng khách, diễn đạt cho nhân viên đọc.
        /// Tách khỏi <see cref="DescribeHandling"/> vì bên kia viết cho khách ("giao thẳng tới
        /// bạn") — đọc trong màn duyệt của OM sẽ sai vai.
        /// </summary>
        private static string? DescribeCustomerIntent(string? handling) =>
            handling?.Trim().ToUpperInvariant() switch
            {
                "DIRECT_DELIVERY" => "Khách muốn giao ngay khi về VN",
                "STORE_AT_VN" => "Khách muốn gửi lại kho VN",
                _ => null,
            };

        public async Task<OrderDeliveryTrackingDto> GetOrderTrackingAsync(
            Guid orderId, Guid userId, string userRole)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {orderId}");

            // Khách chỉ xem đơn của mình. Không có chốt này thì chỉ cần đổi orderId trên URL là
            // xem được đơn người khác, kèm tên và địa chỉ nhà họ.
            var isStaff = !string.IsNullOrWhiteSpace(userRole)
                && !string.Equals(userRole.Trim(), "Customer", StringComparison.OrdinalIgnoreCase);

            if (!isStaff)
            {
                var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .FirstOrDefault(c => c.UserId == userId);

                if (customer == null || order.CustomerId != customer.Id)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền xem tiến trình giao của đơn này.");
                }
            }

            var deliveries = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync())
                .Where(d => d.OrderId == orderId)
                .OrderByDescending(d => d.CreatedAt ?? DateTime.MinValue)
                .ToList();

            var deliveryIds = deliveries.Select(d => d.Id).ToHashSet();
            var itemsByDelivery = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                .Where(i => deliveryIds.Contains(i.DeliveryRequestId))
                .GroupBy(i => i.DeliveryRequestId)
                .ToDictionary(g => g.Key, g => g.Count());

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .OrderBy(p => p.PackageCode)
                .ToList();

            var storage = await _storageFeeService.CalculateForOrderAsync(orderId);

            // Nút xác nhận chỉ bật khi hàng thật sự đã tới tay: mọi kiện DELIVERED và khách chưa bấm.
            var allDelivered = parcels.Count > 0
                && parcels.All(p => string.Equals(p.PackageStatus, "DELIVERED", StringComparison.OrdinalIgnoreCase));

            return new OrderDeliveryTrackingDto
            {
                OrderId = order.Id,
                OrderCode = order.ConsignmentCode ?? string.Empty,
                OrderStatus = order.Status ?? string.Empty,
                OrderStatusText = DescribeOrderStatus(order.Status),
                IsCustomerConfirmed = order.CustomerConfirmedAt.HasValue,
                CustomerConfirmedAt = order.CustomerConfirmedAt,
                CanConfirmReceipt = allDelivered && !order.CustomerConfirmedAt.HasValue,
                StorageFeeAmount = storage.TotalAmount,
                StorageFeeNote = storage.Note,
                Deliveries = deliveries.Select(d => new CustomerDeliveryDto
                {
                    DeliveryRequestId = d.Id,
                    DeliveryCode = d.DeliveryCode ?? string.Empty,
                    Status = d.Status ?? string.Empty,
                    StatusText = DescribeStatus(d.Status),
                    ReceiverName = d.ReceiverName ?? string.Empty,
                    FullAddress = string.Join(", ", new[] { d.AddressDetail, d.Ward, d.District, d.Province }
                        .Where(part => !string.IsNullOrWhiteSpace(part))),
                    CarrierTrackingCode = d.CarrierTrackingCode,
                    ScheduledDate = d.ScheduledDate,
                    DispatchedAt = d.DispatchedAt,
                    TotalParcels = itemsByDelivery.TryGetValue(d.Id, out var count) ? count : 0,
                }).ToList(),
                Parcels = parcels.Select(p => new CustomerParcelStatusDto
                {
                    PackageCode = p.PackageCode ?? string.Empty,
                    PackageStatus = p.PackageStatus ?? string.Empty,
                    StatusText = DescribeParcelStatus(p.PackageStatus),
                    DestinationHandling = p.DestinationHandling,
                    HandlingText = DescribeHandling(p.DestinationHandling),
                    StoredAt = p.PutAwayTime,
                }).ToList(),
            };
        }

        private static string DescribeOrderStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            "AT_DESTINATION_WAREHOUSE" => "Hàng đã về kho Việt Nam",
            "WAITING_FINAL_PAYMENT" => "Chờ thanh toán đợt cuối",
            "PAID" => "Đã thanh toán",
            "DELIVERED" => "Đã giao hàng",
            "CUSTOMER_CONFIRMED" => "Bạn đã xác nhận nhận đủ hàng",
            "COMPLETED" => "Hoàn tất",
            _ => status ?? string.Empty,
        };

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

            // Nạp bảng tra MỘT lần cho cả danh sách. Trước đây mỗi phiếu tự nạp lại toàn bộ
            // Parcel, Bin, User, Customer và DeliveryRequestItem — 23 phiếu thành hơn trăm lượt
            // gọi DB, mất bảy giây cho một danh sách 26KB.
            var lookups = await LoadLookupsAsync();

            var result = new List<DeliveryRequestDto>();
            foreach (var delivery in all.OrderByDescending(d => d.CreatedAt ?? DateTime.MinValue))
            {
                result.Add(BuildDto(delivery, lookups));
            }
            return result;
        }

        // ----------------------------------------------------------------------------------

        /// <summary>
        /// Cổng chặn thứ hai: phí lưu kho phát sinh sau tất toán phải trả xong mới cho hàng ra.
        /// <para>
        /// Tất toán chốt số tiền tại thời điểm khách trả, nhưng hàng còn nằm kệ thêm ngày nào thì
        /// phát sinh ngày ấy. Không chặn ở đây thì phần chênh không bao giờ thu được — hàng đi rồi
        /// thì mất luôn đòn bẩy. Kiện giao thẳng không qua kệ nên phí bằng 0, tự động lọt qua.
        /// </para>
        /// </summary>
        private async Task EnsureStorageFeeSettledAsync(DeliveryRequest delivery)
        {
            if (!delivery.OrderId.HasValue) return;

            var storage = await _storageFeeService.CalculateForOrderAsync(delivery.OrderId.Value);
            if (storage.TotalAmount <= 0m) return;

            // Đơn mua hộ chưa có đường ghi phí lưu kho vào hoá đơn (nó dùng bộ bảng tiền riêng),
            // nên chặn ở đây sẽ khoá cứng luồng giao mà kho không có nút nào gỡ. Cảnh báo trong
            // log để còn lần ra, và thu tay cho tới khi dựng xong Pay-at-Pickup cho mua hộ.
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(delivery.OrderId.Value);
            if (order?.PurchaseRequestId is not null)
            {
                _logger.LogWarning(
                    "Đơn mua hộ {ConsignmentCode} phát sinh {Amount} phí lưu kho nhưng chưa có "
                    + "luồng thu cho mua hộ — cần thu tay trước khi giao.",
                    order.ConsignmentCode, storage.TotalAmount);
                return;
            }

            var quotations = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == delivery.OrderId.Value)
                .Select(q => q.Id)
                .ToHashSet();
            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotations.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();

            var storagePayments = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Where(p => invoiceIds.Contains(p.InvoiceId ?? Guid.Empty)
                            && string.Equals(p.InstallmentType, "STORAGE_FEE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Mã đã phát hành mà khách chưa quét: chặn ngay, khỏi cần so số. Không kiểm tra chỗ
            // này thì bước chốt phí ghi hoá đơn lên số mới sẽ tự làm mọi phép so bên dưới hoà,
            // và hàng ra khỏi kho trong khi tiền chưa về.
            var pending = storagePayments.FirstOrDefault(p =>
                !string.Equals(p.PaymentStatus?.Trim(), "PAID", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p.PaymentStatus?.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase));

            if (pending != null)
            {
                throw new ArgumentException(
                    $"Khách chưa thanh toán {pending.Amount:#,##0}đ phí lưu kho. "
                    + "Mời khách quét mã rồi mới xuất hàng được.");
            }

            var billedStorage = (await _unitOfWork.Repository<InvoiceItem>().GetAllAsync())
                .Where(i => invoiceIds.Contains(i.InvoiceId ?? Guid.Empty)
                            && string.Equals(i.FeeType, "STORAGE", StringComparison.OrdinalIgnoreCase))
                .Sum(i => i.Amount ?? 0m);

            // Phí hôm nay vượt phần đã ghi hoá đơn nghĩa là có ngày lưu mới chưa ai chốt.
            var outstanding = storage.TotalAmount - billedStorage;
            if (outstanding <= 0m) return;

            throw new ArgumentException(
                $"Đơn phát sinh thêm {outstanding:#,##0}đ phí lưu kho chưa chốt. "
                + "Bấm chốt phí lưu kho rồi cho khách thanh toán trước khi xuất hàng.");
        }

        /// <summary>
        /// Dựng payload gửi GoShip từ phiếu giao và các kiện đem đi.
        /// <para>
        /// Ba cấp hành chính của người nhận nằm sẵn trên phiếu (sale nhập lúc lập) — trước đây
        /// không ai lấy ra dùng nên GoShip luôn từ chối vì thiếu quận/phường.
        /// </para>
        /// </summary>
        private async Task<GoshipOrderShipment> BuildGoshipShipmentAsync(
            DeliveryRequest delivery, List<Parcel> parcels)
        {
            var order = delivery.OrderId.HasValue
                ? await _unitOfWork.Repository<Order>().GetByIdAsync(delivery.OrderId.Value)
                : null;

            // GoShip tính theo gram và chỉ nhận một khối kiện: cộng dồn cân nặng, lấy kích thước
            // lớn nhất theo từng chiều cho sát thực tế nhất có thể.
            var totalWeightKg = parcels.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m);
            var weightGrams = (int)Math.Max(200m, Math.Round(totalWeightKg * 1000m));

            var destinationWarehouse = await ResolveDestinationWarehouseAsync(parcels);

            var shipment = new GoshipOrderShipment
            {
                // Gửi mã PHIẾU GIAO, không phải mã đơn ký gửi.
                //
                // GoShip coi order_id là khoá chống trùng: gọi lại với cùng giá trị thì họ trả
                // nguyên vận đơn cũ chứ không tạo đơn mới. Một đơn ký gửi lại thường có nhiều
                // phiếu giao — kiện giao thẳng đi trước, kiện gửi kho lấy sau — nên lấy mã đơn
                // làm khoá thì phiếu thứ hai trở đi không được đặt giao thật, trong khi hệ thống
                // vẫn báo thành công và lưu đè đúng mã vận đơn của phiếu đầu.
                OrderId = delivery.DeliveryCode,
                AddressFrom = BuildPickupAddress(destinationWarehouse),
                AddressTo = new GoshipAddressFull
                {
                    Name = delivery.ReceiverName ?? string.Empty,
                    Phone = delivery.ReceiverPhone ?? string.Empty,
                    Street = delivery.AddressDetail ?? string.Empty,
                    Ward = delivery.Ward ?? string.Empty,
                    District = delivery.District ?? string.Empty,
                    City = delivery.Province ?? string.Empty,
                },
                Parcel = new GoshipParcelFull
                {
                    Name = order?.ConsignmentCode ?? delivery.DeliveryCode,
                    Metadata = $"{parcels.Count} kiện · phiếu {delivery.DeliveryCode}",
                    Cod = 0,
                    Weight = weightGrams,
                    Width = (int)Math.Max(1m, Math.Round(parcels.Max(p => p.Width ?? 10m))),
                    Height = (int)Math.Max(1m, Math.Round(parcels.Max(p => p.Height ?? 10m))),
                    Length = (int)Math.Max(1m, Math.Round(parcels.Max(p => p.Length ?? 10m))),
                }
            };

            return shipment;
        }

        /// <summary>
        /// Kho mà shipper tới lấy hàng: chính là kho đích của lô đã chở kiện về.
        /// <para>
        /// Lấy động theo từng kiện thay vì khai cứng một kho trong cấu hình — hệ thống có thể
        /// mở thêm kho Việt Nam, và mỗi phiếu giao phải trỏ đúng nơi hàng đang nằm.
        /// </para>
        /// </summary>
        private async Task<Warehouse?> ResolveDestinationWarehouseAsync(List<Parcel> parcels)
        {
            var parcelIds = parcels.Select(p => p.Id).ToHashSet();

            var shipmentIds = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .Where(sp => parcelIds.Contains(sp.ParcelId))
                .Select(sp => sp.ShipmentId)
                .ToHashSet();

            var destinationWarehouseId = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .Where(s => shipmentIds.Contains(s.Id) && s.DestinationWarehouseId.HasValue)
                .Select(s => s.DestinationWarehouseId!.Value)
                .FirstOrDefault();

            if (destinationWarehouseId != Guid.Empty)
            {
                return await _unitOfWork.Repository<Warehouse>().GetByIdAsync(destinationWarehouseId);
            }

            // Kiện không gắn lô (dữ liệu cũ, hoặc hàng nhập tay): lấy kho Việt Nam đang hoạt động.
            return (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .FirstOrDefault(w => w.IsActive && w.Region == RegionCode.VN);
        }

        /// <summary>
        /// Tách quận và phường ra khỏi chuỗi địa chỉ kho.
        /// <para>
        /// Bảng WAREHOUSES gộp cả địa chỉ vào một cột ("Khu CN Tân Bình, P.Tây Thạnh, Q.Tân Phú")
        /// trong khi GoShip đòi ba cấp riêng. Nhận diện theo tiền tố quen thuộc; phần còn lại coi
        /// là tên đường. Sai sót thì bước đổi tên sang mã sẽ báo lỗi rõ chứ không gửi bừa.
        /// </para>
        /// </summary>
        private static (string Street, string Ward, string District) SplitWarehouseAddress(string? address)
        {
            var parts = (address ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            string ward = string.Empty, district = string.Empty;
            var streetParts = new List<string>();

            foreach (var part in parts)
            {
                var lowered = part.ToLowerInvariant();
                if (lowered.StartsWith("p.") || lowered.StartsWith("phường") || lowered.StartsWith("phuong")
                    || lowered.StartsWith("xã") || lowered.StartsWith("xa "))
                {
                    ward = part;
                }
                else if (lowered.StartsWith("q.") || lowered.StartsWith("quận") || lowered.StartsWith("quan")
                         || lowered.StartsWith("huyện") || lowered.StartsWith("huyen"))
                {
                    district = part;
                }
                else
                {
                    streetParts.Add(part);
                }
            }

            return (string.Join(", ", streetParts), ward, district);
        }

        /// <summary>
        /// Địa chỉ kho gửi cho GoShip: lấy từ kho đích của lô, cấu hình chỉ bù phần DB không có
        /// (tên người trực, số điện thoại shipper gọi khi tới nơi).
        /// </summary>
        private GoshipAddressFull BuildPickupAddress(Warehouse? warehouse)
        {
            var section = _configuration.GetSection("GoshipSettings:Pickup");
            var (street, ward, district) = SplitWarehouseAddress(warehouse?.Address);

            return new GoshipAddressFull
            {
                Name = FirstNonEmpty(warehouse?.WarehouseName, section["Name"], "Kho VCL Việt Nam"),
                // Bảng kho chưa có cột điện thoại nên phần này vẫn phải lấy từ cấu hình.
                Phone = section["Phone"] ?? string.Empty,
                Street = FirstNonEmpty(street, section["Street"], string.Empty),
                Ward = FirstNonEmpty(ward, section["Ward"], string.Empty),
                District = FirstNonEmpty(district, section["District"], string.Empty),
                City = FirstNonEmpty(warehouse?.City, section["City"], string.Empty),
            };
        }

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        /// <summary>
        /// Hỏi bảng giá của đúng tuyến rồi chọn gói rẻ nhất. GoShip bắt buộc trường
        /// <c>rate</c> khi tạo vận đơn, không có thì từ chối ngay.
        /// </summary>
        private async Task<string> ResolveCheapestRateAsync(GoshipOrderShipment shipment, string token)
        {
            var rates = await _goshipService.GetRatesAsync(new GoshipRateRequest
            {
                Shipment = new GoshipRateShipment
                {
                    AddressFrom = new GoshipAddressBasic
                    {
                        City = shipment.AddressFrom.City,
                        District = shipment.AddressFrom.District,
                    },
                    AddressTo = new GoshipAddressBasic
                    {
                        City = shipment.AddressTo.City,
                        District = shipment.AddressTo.District,
                    },
                    Parcel = new GoshipParcelBasic
                    {
                        Cod = shipment.Parcel.Cod,
                        Weight = shipment.Parcel.Weight,
                        Width = shipment.Parcel.Width,
                        Height = shipment.Parcel.Height,
                        Length = shipment.Parcel.Length,
                    }
                }
            }, token);

            var cheapest = rates
                .Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .OrderBy(r => r.TotalAmount > 0 ? r.TotalAmount : r.TotalFee)
                .FirstOrDefault();

            if (cheapest == null)
            {
                throw new InvalidOperationException(
                    "GoShip không có gói cước nào cho tuyến này. Kiểm tra lại tỉnh/quận của người nhận và kho gửi.");
            }

            return cheapest.Id;
        }

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
        /// <summary>Khoản được coi là đã thu: trả đủ một lần, hoặc đã trả nốt đợt cuối.</summary>
        /// <summary>
        /// Chưa thu đủ tiền thì không giao. Luật "đã tất toán" nằm ở
        /// <see cref="ISettlementQueryService"/> để hàng đợi của Sale, cổng nhập kho và chốt này
        /// không bao giờ nói khác nhau.
        /// </summary>
        private async Task EnsureFinalPaymentSettledAsync(Order order)
        {
            if (await _settlementQueryService.IsSettledAsync(order)) return;

            var label = order.PurchaseRequestId.HasValue ? "Đơn mua hộ" : "Đơn";
            throw new ArgumentException(
                $"{label} {order.ConsignmentCode} chưa tất toán xong. "
                + "Khách trả nốt đợt cuối rồi mới lập được yêu cầu giao hàng.");
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

            // Bản ghi tồn kho còn hiệu lực của từng kiện. Nhật ký di chuyển bắt buộc trỏ về đúng
            // bản ghi này: thiếu thì InventoryId rơi về Guid.Empty và câu INSERT vỡ khoá ngoại,
            // kéo cả lượt xuất kho hỏng theo.
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var inventoryByParcel = (await inventoryRepo.GetAllAsync())
                .Where(i => allParcelIds.Contains(i.ParcelId))
                .GroupBy(i => i.ParcelId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(i => i.CreatedAt).First());

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

                    if (inventoryByParcel.TryGetValue(parcel.Id, out var inventory))
                    {
                        await movementRepo.AddAsync(new InventoryMovement
                        {
                            Id = Guid.NewGuid(),
                            InventoryId = inventory.Id,
                            ParcelId = parcel.Id,
                            FromBinId = binId,
                            // Cột to_bin_id là NOT NULL nên ghi lại chính ô vừa rời đi; MovementType
                            // OUTBOUND đã đủ để phân biệt với điều chuyển nội bộ.
                            ToBinId = binId,
                            MovementType = "OUTBOUND",
                            Reason = "Xuất kho giao khách theo yêu cầu giao hàng",
                            Quantity = inventory.Quantity > 0 ? inventory.Quantity : 1,
                            MovedById = userId,
                            MovedAt = now
                        });

                        // Hàng đã rời kho thì bản ghi tồn kho không còn khả dụng, nếu không ô kệ
                        // vừa nhả vẫn bị coi là đang chứa hàng ở màn tồn kho.
                        inventory.Status = "RELEASED";
                        inventoryRepo.Update(inventory);
                    }

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

        /// <summary>
        /// Bảng tra dùng chung khi dựng nhiều phiếu giao một lượt.
        /// </summary>
        /// <remarks>
        /// Repository ở đây chỉ có <c>GetAllAsync</c>, nên dựng từng phiếu một là mỗi phiếu kéo
        /// lại nguyên bảng. Nạp sẵn vào đây rồi tra trong bộ nhớ.
        /// </remarks>
        private sealed class DeliveryLookups
        {
            public ILookup<Guid, DeliveryRequestItem> ItemsByDelivery { get; init; } =
                Array.Empty<DeliveryRequestItem>().ToLookup(i => Guid.Empty);
            public Dictionary<Guid, Parcel> Parcels { get; init; } = new();
            public Dictionary<Guid, Bin> Bins { get; init; } = new();
            public Dictionary<Guid, User> Users { get; init; } = new();
            public Dictionary<Guid, Order> Orders { get; init; } = new();
            public Dictionary<Guid, Customer> Customers { get; init; } = new();
        }

        private async Task<DeliveryLookups> LoadLookupsAsync()
        {
            return new DeliveryLookups
            {
                ItemsByDelivery = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                    .ToLookup(i => i.DeliveryRequestId),
                Parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                    .ToDictionary(p => p.Id),
                Bins = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                    .ToDictionary(b => b.Id),
                Users = (await _unitOfWork.Repository<User>().GetAllAsync())
                    .ToDictionary(u => u.Id),
                Orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                    .ToDictionary(o => o.Id),
                Customers = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .ToDictionary(c => c.Id),
            };
        }

        private async Task<DeliveryRequestDto> BuildDtoAsync(DeliveryRequest delivery) =>
            BuildDto(delivery, await LoadLookupsAsync());

        private static DeliveryRequestDto BuildDto(DeliveryRequest delivery, DeliveryLookups lookups)
        {
            var items = lookups.ItemsByDelivery[delivery.Id].ToList();
            var parcels = lookups.Parcels;
            var bins = lookups.Bins;
            var users = lookups.Users;

            Order? order = delivery.OrderId.HasValue
                && lookups.Orders.TryGetValue(delivery.OrderId.Value, out var foundOrder)
                    ? foundOrder
                    : null;

            Customer? customer = order?.CustomerId is Guid cid
                && lookups.Customers.TryGetValue(cid, out var foundCustomer)
                    ? foundCustomer
                    : null;

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
                    CustomerIntent = order?.DefaultDestinationHandling,
                    CustomerIntentText = DescribeCustomerIntent(order?.DefaultDestinationHandling),
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
