using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class OrderService
    {
        /// <summary>
        /// Sale báo cho kho rằng đơn này có hàng khách xin gửi lại, kho vào lập phiếu nhập kho.
        /// </summary>
        /// <remarks>
        /// Đứng SAU bước khách tất toán: thu đủ tiền rồi mới cho hàng vào sổ kho. Chỉ áp cho ký
        /// gửi — hàng mua hộ vào kho qua bước OM duyệt riêng nên không cần Sale báo thêm.
        /// </remarks>
        public async Task<NotifyWarehouseResponse> NotifyWarehouseAsync(
            Guid orderId, Guid userId, string userRole, NotifyWarehouseRequest request)
        {
            var normalizedRole = userRole?.Trim().ToUpperInvariant();
            if (normalizedRole is not ("SALE" or "SALES" or "ADMIN"))
            {
                throw new UnauthorizedAccessException("Chỉ Sale được thông báo cho kho.");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");

            if (string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Đơn mua hộ không cần bước này — hàng mua hộ vào kho qua bước OM duyệt nhập kho.");
            }

            // Kiện phải vừa được kho kiểm đếm xong, vừa được phân loại là khách gửi lại. Báo kho
            // khi hàng chưa về hoặc chưa phân loại thì kho mở màn ra chẳng có gì để lập phiếu.
            var storeAtVn = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Count(p => p.ConsignmentId == orderId
                            && string.Equals(p.DestinationHandling, StoreAtVnHandling, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(p.PackageStatus, ReceivedAtDestinationStatus, StringComparison.OrdinalIgnoreCase));

            if (storeAtVn == 0)
            {
                throw new ArgumentException(
                    "Đơn này chưa có kiện nào kho đã kiểm đếm và phân loại là khách gửi lại kho VN. "
                    + "Chờ kho mở lô kiểm đếm xong rồi mới báo được.");
            }

            // Tiền trước, hàng vào sổ sau. Đây là chốt chặn thu tiền của cả chặng cuối.
            if (!await _settlementQueryService.IsSettledAsync(order))
            {
                throw new ArgumentException(
                    $"Đơn {order.ConsignmentCode} chưa tất toán. Khách trả nốt đợt cuối rồi mới báo kho nhập hàng được.");
            }

            var actor = await _unitOfWork.Repository<User>().GetByIdAsync(userId);

            // Bấm lại lần nữa không được dời mốc: kho đo thời gian chờ việc từ mốc này, dời là
            // mọi đơn trễ đều tự động trẻ lại.
            if (order.WarehouseNotifiedAt.HasValue)
            {
                return new NotifyWarehouseResponse
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode,
                    WarehouseNotifiedAt = order.WarehouseNotifiedAt.Value,
                    WarehouseNotifiedByName = actor?.FullName,
                    Note = order.WarehouseNotifiedNote,
                    StoreAtVnParcelCount = storeAtVn,
                    AlreadyNotified = true,
                };
            }

            var now = DateTime.UtcNow;
            order.WarehouseNotifiedAt = now;
            order.WarehouseNotifiedById = userId;
            order.WarehouseNotifiedNote = string.IsNullOrWhiteSpace(request?.Note)
                ? null
                : request!.Note!.Trim();
            orderRepo.Update(order);
            await _unitOfWork.SaveChangesAsync();

            await _notificationService.NotifyRolesAsync(
                new[] { "WarehouseStaff", "Warehouse", "Staff", "OperationsManager", "Operations", "Admin" },
                "Có đơn chờ nhập kho",
                $"Đơn {order.ConsignmentCode} đã tất toán, {storeAtVn} kiện khách gửi lại kho VN. "
                + "Vào mục Đơn hàng cần xử lý để lập phiếu nhập kho.",
                excludeUserId: userId);

            return new NotifyWarehouseResponse
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode,
                WarehouseNotifiedAt = now,
                WarehouseNotifiedByName = actor?.FullName,
                Note = order.WarehouseNotifiedNote,
                StoreAtVnParcelCount = storeAtVn,
            };
        }

        public async Task<CancelConsignmentResponse> CancelConsignmentAsync(Guid orderId, Guid userId, CancelConsignmentRequest request)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new UnauthorizedAccessException("Người dùng không phải là khách hàng hợp lệ.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");

            if (order.CustomerId != customer.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền hủy yêu cầu ký gửi này.");

            var currentStatus = order.Status?.ToUpperInvariant() ?? string.Empty;

            if (currentStatus == "CANCELLED")
                throw new ArgumentException("Yêu cầu ký gửi này đã được hủy trước đó.");

            if (!CancellableStatuses.Contains(currentStatus))
                throw new ArgumentException($"Không thể hủy yêu cầu khi đang ở trạng thái {order.Status}. Chỉ được hủy khi hàng chưa vào kho VN.");

            // Hủy Order
            order.Status = "CANCELLED";
            
            var reason = request.CancelReason;
            if (!string.IsNullOrWhiteSpace(reason))
                order.Note = reason;

            orderRepo.Update(order);

            // Hủy các Quotation liên quan đang DRAFT/ACCEPTED để tránh accept lại sau khi đơn đã hủy
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var relatedQuotations = allQuotations
                .Where(q => q.OrderId == orderId)
                .Where(q => q.Status == null
                    || q.Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase)
                    || q.Status.Equals("ACCEPTED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var quotation in relatedQuotations)
            {
                quotation.Status = "CANCELLED";
                quotationRepo.Update(quotation);
            }

            // Tạo thông báo cho khách hàng
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = customer.UserId,
                Title = "Yêu cầu ký gửi đã bị hủy",
                Content = $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã bị hủy.{(string.IsNullOrWhiteSpace(reason) ? "" : $" Lý do: {reason}")}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            var notificationRepo = _unitOfWork.Repository<Notification>();
            await notificationRepo.AddAsync(notification);

            // Bắn thông báo cho Sale & Admin
            await _notificationService.NotifyRolesAsync(
                new[] { "Sale", "Sales", "Admin" },
                "Đơn ký gửi đã bị hủy",
                $"Khách hàng đã hủy đơn ký gửi {order.ConsignmentCode ?? order.Id.ToString()}.{(string.IsNullOrWhiteSpace(reason) ? "" : $" Lý do: {reason}")}",
                excludeUserId: userId);

            await _unitOfWork.SaveChangesAsync();

            return new CancelConsignmentResponse
            {
                OrderId = order.Id,
                Status = order.Status,
                CancelReason = reason,
                CancelledQuotations = relatedQuotations.Count,
                ConsignmentCode = order.ConsignmentCode
            };
        }

        public async Task<UpdateConsignmentStatusResponse> UpdateConsignmentStatusAsync(Guid orderId, UpdateConsignmentStatusRequest request, Guid? actorUserId = null)
        {
            var validStatuses = new[] { "PENDING_REVIEW", "APPROVED", "REJECTED" };
            if (string.IsNullOrWhiteSpace(request.Status) || !validStatuses.Contains(request.Status.ToUpper()))
            {
                throw new ArgumentException("Trạng thái không hợp lệ. Các trạng thái hợp lệ: PENDING_REVIEW, APPROVED, REJECTED.");
            }

            var normalizedStatus = request.Status.ToUpper();

            if (normalizedStatus == "REJECTED" && string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                throw new ArgumentException("Vui lòng cung cấp lý do từ chối (RejectionReason).");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);

            if (order == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");
            }

            // Đạt xác nhận (PR#48): staff VẪN được đổi duyệt/từ chối khi đơn ở QUOTATION_SENT/CONFIRMED/REJECTED.
            // Chỉ chặn khi đơn đã hủy hoặc đã nhập kho.
            var invalidCurrentStatuses = new[] { "CANCELLED", "WAREHOUSE_RECEIVED", "CHECKED_IN" };
            if (order.Status != null && invalidCurrentStatuses.Contains(order.Status.ToUpper()))
            {
                throw new ArgumentException($"Không thể cập nhật trạng thái yêu cầu khi yêu cầu đang ở trạng thái {order.Status}");
            }

            if (normalizedStatus == "APPROVED" && string.IsNullOrEmpty(order.ConsignmentCode))
            {
                var allOrders = await orderRepo.GetAllAsync();
                var random = new Random();
                string generatedCode;
                do
                {
                    var now = DateTime.UtcNow;
                    var randomSuffix = random.Next(100000, 999999).ToString();
                    generatedCode = $"VCL-{now:yyyyMMddHHmmss}-{randomSuffix}";
                } while (allOrders.Any(o => o.ConsignmentCode == generatedCode));

                order.ConsignmentCode = generatedCode;
            }

            var oldStatus = order.Status;
            order.Status = normalizedStatus;
            
            if (normalizedStatus == "REJECTED")
            {
                order.Note = request.RejectionReason;
            }

            if (oldStatus != normalizedStatus)
            {
                if (normalizedStatus == "APPROVED" || normalizedStatus == "REJECTED")
                {
                    var customerRepo = _unitOfWork.Repository<Customer>();
                    var customer = await customerRepo.GetByIdAsync(order.CustomerId);
                    if (customer != null)
                    {
                        var notif = new Notification
                        {
                            Id = Guid.NewGuid(),
                            UserId = customer.UserId,
                            Title = normalizedStatus == "APPROVED" ? "Yêu cầu ký gửi đã được duyệt" : "Yêu cầu ký gửi bị từ chối",
                            Content = normalizedStatus == "APPROVED"
                                ? $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã được duyệt."
                                : $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã bị từ chối.{(string.IsNullOrWhiteSpace(request.RejectionReason) ? "" : $" Lý do: {request.RejectionReason}")}",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        var notificationRepo = _unitOfWork.Repository<Notification>();
                        await notificationRepo.AddAsync(notif);
                    }

                    // Khi đơn ký gửi được duyệt, thông báo cho bộ phận Kho & Operations sẵn sàng tiếp nhận
                    if (normalizedStatus == "APPROVED")
                    {
                        await _notificationService.NotifyRolesAsync(
                            new[] { "WarehouseStaff", "OperationsManager", "Operations", "Warehouse" },
                            "Đơn ký gửi đã được duyệt",
                            $"Đơn ký gửi {order.ConsignmentCode ?? order.Id.ToString()} đã được duyệt, sẵn sàng tiếp nhận kiện hàng vào kho.",
                            excludeUserId: actorUserId);
                    }
                }
            }

            orderRepo.Update(order);
            await _unitOfWork.SaveChangesAsync();

            string? receiptPdfUrl = order.ReceiptPdfUrl;

            // Sale xác nhận yêu cầu là mốc phát hành phiếu cho kho. Lỗi sinh phiếu
            // không được làm hỏng việc duyệt đơn, nên nuốt lỗi và để Sale sinh lại sau.
            if (normalizedStatus == "APPROVED" && oldStatus != normalizedStatus)
            {
                try
                {
                    receiptPdfUrl = await _consignmentReceiptService.GenerateAndStoreReceiptAsync(orderId, actorUserId)
                                    ?? receiptPdfUrl;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"GENERATE CONSIGNMENT RECEIPT ERROR ({orderId}): {ex.Message}");
                }
            }

            return new UpdateConsignmentStatusResponse
            {
                Status = order.Status,
                ConsignmentCode = order.ConsignmentCode,
                ReceiptPdfUrl = receiptPdfUrl
            };
        }
    }
}
