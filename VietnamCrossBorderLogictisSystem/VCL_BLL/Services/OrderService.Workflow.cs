using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
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
                throw new ArgumentException($"Không thể hủy yêu cầu khi đang ở trạng thái {StatusNames.Quote(order.Status)}. Chỉ được hủy khi hàng chưa vào kho VN.");

            // Đã có tiền thực nhận thì không tự huỷ được: phải qua Sales đề nghị và Admin xử lý hoàn tiền.
            var paidQuotationIds = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId).Select(q => q.Id).ToHashSet();
            var paidInvoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && paidQuotationIds.Contains(i.QuotationId.Value))
                .Select(i => i.Id).ToHashSet();
            var hasMoney = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Any(p => p.InvoiceId.HasValue && paidInvoiceIds.Contains(p.InvoiceId.Value)
                          && (p.Amount ?? 0m) > 0m
                          && string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase));
            if (hasMoney)
                throw new ArgumentException("Đơn đã có khoản thanh toán được xác nhận nên không tự huỷ được. Vui lòng liên hệ Sales.");

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

            // Link thanh toán còn treo phải huỷ theo đơn: trước đây khách vẫn trả được link cũ và
            // webhook làm đơn đã huỷ "sống lại" thành DEPOSIT_PAID.
            var quotationIds = allQuotations.Where(q => q.OrderId == orderId).Select(q => q.Id).ToHashSet();
            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();
            var paymentRepo = _unitOfWork.Repository<Payment>();
            foreach (var pending in (await paymentRepo.GetAllAsync()).Where(p =>
                         p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value)
                         && (string.Equals(p.PaymentStatus, "PENDING", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(p.PaymentStatus, "PENDING_RECONCILIATION", StringComparison.OrdinalIgnoreCase))))
            {
                pending.PaymentStatus = "CANCELLED";
                pending.FailureReason = "Khách huỷ đơn.";
                paymentRepo.Update(pending);
            }

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, currentStatus, "CANCELLED",
                "ORDER_CANCELLED_BY_CUSTOMER", reason, userId);

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

        /// <summary>
        /// Sale không phải bấm tiếp nhận trước khi báo giá (gửi báo giá là đã tiếp nhận). Endpoint
        /// trạng thái chỉ còn để từ chối đơn hoặc yêu cầu khách bổ sung thông tin.
        /// </summary>
        private static readonly string[] ReviewDecisions = { "REJECTED", "NEED_MORE_INFO" };

        /// <summary>Đơn chưa có báo giá được khách chấp nhận, chưa phát sinh tiền.</summary>
        private static readonly string[] ReviewableStatuses =
            { "PENDING_REVIEW", "NEED_MORE_INFO", "APPROVED", "QUOTATION_SENT", "QUOTATION_REJECTED" };

        public async Task<UpdateConsignmentStatusResponse> UpdateConsignmentStatusAsync(Guid orderId, UpdateConsignmentStatusRequest request, Guid? actorUserId = null)
        {
            var normalizedStatus = request?.Status?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedStatus) || !ReviewDecisions.Contains(normalizedStatus))
            {
                throw new ArgumentException("Trạng thái không hợp lệ. Chỉ được từ chối đơn hoặc yêu cầu bổ sung thông tin; Sale gửi báo giá trực tiếp, không cần duyệt đơn trước.");
            }

            // Từ chối hoặc xin bổ sung thông tin mà không nói lý do thì khách không biết phải làm gì.
            if (string.IsNullOrWhiteSpace(request!.RejectionReason))
            {
                throw new ArgumentException("Vui lòng ghi lý do (RejectionReason) khi từ chối hoặc yêu cầu bổ sung thông tin.");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);

            if (order == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");
            }

            // Chỉ từ chối / xin bổ sung khi đơn chưa có báo giá được chấp nhận. Đơn đã thanh toán
            // hoặc đã có hàng thì xử lý bằng luồng huỷ đơn.
            var currentStatus = order.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!ReviewableStatuses.Contains(currentStatus))
            {
                throw new ArgumentException(
                    $"Không thể đổi kết quả tiếp nhận khi đơn đang ở trạng thái {StatusNames.Quote(order.Status)}. "
                    + "Đơn đã thanh toán thì xử lý bằng luồng huỷ đơn.");
            }

            var orderQuotations = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId)
                .ToList();

            if (orderQuotations.Any(q => string.Equals(q.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Đơn đã có báo giá được khách chấp nhận nên không đổi kết quả tiếp nhận được.");
            }

            var oldStatus = order.Status;
            order.Status = normalizedStatus;
            order.Note = request.RejectionReason;

            // Báo giá đang chờ khách không còn giá trị khi đơn bị từ chối / trả lại bổ sung.
            foreach (var quotation in orderQuotations.Where(q =>
                         q.Status is not null
                         && (q.Status.Equals("PENDING", StringComparison.OrdinalIgnoreCase)
                             || q.Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase)
                             || q.Status.Equals("PENDING_PRICE_APPROVAL", StringComparison.OrdinalIgnoreCase))))
            {
                quotation.Status = "SUPERSEDED";
                _unitOfWork.Repository<Quotation>().Update(quotation);
            }

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, oldStatus, normalizedStatus,
                $"ORDER_{normalizedStatus}", request.RejectionReason, actorUserId);

            var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId);
            if (customer != null)
            {
                var code = order.ConsignmentCode ?? order.Id.ToString();
                await _unitOfWork.Repository<Notification>().AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = customer.UserId,
                    Title = normalizedStatus == "NEED_MORE_INFO"
                        ? "Yêu cầu ký gửi cần bổ sung thông tin"
                        : "Yêu cầu ký gửi bị từ chối",
                    Content = normalizedStatus == "NEED_MORE_INFO"
                        ? $"Yêu cầu ký gửi (Mã: {code}) cần bổ sung: {request.RejectionReason}"
                        : $"Yêu cầu ký gửi (Mã: {code}) của bạn đã bị từ chối. Lý do: {request.RejectionReason}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            orderRepo.Update(order);
            await _unitOfWork.SaveChangesAsync();

            return new UpdateConsignmentStatusResponse
            {
                Status = order.Status,
                ConsignmentCode = order.ConsignmentCode,
                ReceiptPdfUrl = order.ReceiptPdfUrl
            };
        }
    }
}
