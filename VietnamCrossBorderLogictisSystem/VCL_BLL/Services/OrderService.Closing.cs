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
    /// <summary>
    /// Ba bước cuối luồng, đi tuần tự và không được nhảy cóc:
    /// hãng giao xong đặt đơn về <c>DELIVERED</c> → khách xác nhận (<c>CUSTOMER_CONFIRMED</c>)
    /// → OM chốt (<c>COMPLETED</c>).
    /// <para>
    /// Thực tế phần lớn khách nhận hàng xong không vào web bấm gì, nên quá
    /// <see cref="CustomerConfirmGraceDays"/> ngày thì OM được chốt thay.
    /// </para>
    /// </summary>
    public partial class OrderService
    {
        /// <summary>Số ngày chờ khách tự xác nhận trước khi OM được chốt thay.</summary>
        private const int CustomerConfirmGraceDays = 1;

        private const string StatusDelivered = "DELIVERED";
        private const string StatusCustomerConfirmed = "CUSTOMER_CONFIRMED";
        private const string StatusCompleted = "COMPLETED";
        private const string StatusCancelledForfeited = "CANCELLED_FORFEITED";

        public async Task<OrderClosingResponseDto> ConfirmReceivedByCustomerAsync(Guid orderId, Guid userId)
        {
            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .FirstOrDefault(c => c.UserId == userId)
                ?? throw new UnauthorizedAccessException("Người dùng không phải là khách hàng hợp lệ.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {orderId}");

            if (order.CustomerId != customer.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận đơn hàng này.");

            var current = order.Status?.Trim().ToUpperInvariant() ?? string.Empty;

            if (current == StatusCustomerConfirmed || current == StatusCompleted)
                throw new ArgumentException("Đơn hàng này đã được xác nhận trước đó.");

            if (current != StatusDelivered)
                throw new ArgumentException(
                    $"Chỉ xác nhận được khi hãng vận chuyển đã báo giao xong. Trạng thái hiện tại: {order.Status}.");

            var now = DateTime.UtcNow;
            order.Status = StatusCustomerConfirmed;
            order.CustomerConfirmedAt = now;
            orderRepo.Update(order);

            await NotifyOrderStaffAsync(order,
                $"Khách đã xác nhận nhận hàng — đơn {order.ConsignmentCode}",
                $"Khách hàng đã xác nhận nhận đủ hàng của đơn {order.ConsignmentCode}. Đơn chờ OM chốt hoàn thành.");

            await _unitOfWork.SaveChangesAsync();

            return BuildClosingResponse(order, customer.FullName ?? string.Empty, now, null);
        }

        public async Task<OrderClosingResponseDto> CompleteConsignmentAsync(
            Guid orderId, Guid userId, string userRole, CompleteConsignmentRequestDto? request)
        {
            if (!StaffRoleHelper.CanApproveWarehouseStore(userRole))
                throw new UnauthorizedAccessException("Chỉ Operations Manager hoặc Admin được chốt đơn hoàn thành.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {orderId}");

            var current = order.Status?.Trim().ToUpperInvariant() ?? string.Empty;

            if (current == StatusCompleted)
                throw new ArgumentException("Đơn hàng này đã hoàn thành trước đó.");

            var now = DateTime.UtcNow;

            if (current != StatusCustomerConfirmed)
            {
                // Khách chưa bấm xác nhận: chỉ chốt thay được sau thời hạn chờ.
                if (current != StatusDelivered)
                    throw new ArgumentException(
                        $"Chỉ chốt hoàn thành sau khi khách xác nhận, hoặc sau {CustomerConfirmGraceDays} ngày kể từ khi giao xong. "
                        + $"Trạng thái hiện tại: {order.Status}.");

                if (!order.DeliveredAt.HasValue)
                    throw new ArgumentException(
                        "Đơn chưa ghi nhận thời điểm giao xong nên chưa tính được thời hạn chờ khách xác nhận.");

                var waited = now - order.DeliveredAt.Value;
                if (waited < TimeSpan.FromDays(CustomerConfirmGraceDays))
                {
                    var remaining = TimeSpan.FromDays(CustomerConfirmGraceDays) - waited;
                    throw new ArgumentException(
                        $"Khách chưa xác nhận và chưa hết thời hạn chờ. Còn {remaining.TotalHours:F1} giờ nữa mới chốt thay được.");
                }
            }

            order.Status = StatusCompleted;
            order.CompletedAt = now;
            order.CompletedById = userId;
            if (!string.IsNullOrWhiteSpace(request?.Note))
                order.Note = request.Note;
            orderRepo.Update(order);

            await NotifyCustomerOfOrderAsync(order,
                $"Đơn {order.ConsignmentCode} đã hoàn thành",
                $"Đơn ký gửi {order.ConsignmentCode} đã được xác nhận hoàn thành. Cảm ơn quý khách.");

            await _unitOfWork.SaveChangesAsync();

            var actor = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            return BuildClosingResponse(order, actor?.FullName ?? string.Empty, now, request?.Note);
        }

        /// <summary>
        /// Khách quá hạn không trả đợt cuối: Sale bấm từ chối, đơn đóng lại và tiền cọc không
        /// hoàn. Giao dịch cọc giữ nguyên PAID vì tiền đã thực sự về tài khoản — xoá đi thì
        /// sổ thu chi không khớp ngân hàng.
        /// </summary>
        public async Task<OrderClosingResponseDto> RejectUnpaidConsignmentAsync(
            Guid orderId, Guid userId, string userRole, RejectUnpaidConsignmentRequestDto? request)
        {
            if (!StaffRoleHelper.CanHandlePayments(userRole))
                throw new UnauthorizedAccessException("Chỉ Sale, Admin hoặc Operations Manager được từ chối đơn quá hạn thanh toán.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {orderId}");

            var current = order.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (current is StatusCompleted or StatusCancelledForfeited or "CANCELLED")
                throw new ArgumentException($"Đơn hàng đang ở trạng thái {order.Status}, không từ chối được nữa.");

            var quotationIds = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId)
                .Select(q => q.Id)
                .ToHashSet();
            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();

            var paymentRepo = _unitOfWork.Repository<Payment>();
            var payments = (await paymentRepo.GetAllAsync())
                .Where(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value))
                .ToList();

            var finalPaid = payments.Any(p =>
                string.Equals(p.InstallmentType, "FINAL_PAYMENT", StringComparison.OrdinalIgnoreCase)
                && IsSettledPayment(p.PaymentStatus));
            if (finalPaid)
                throw new ArgumentException("Khách đã thanh toán đợt cuối, không từ chối đơn được.");

            var depositPaid = payments
                .Where(p => string.Equals(p.InstallmentType, "DEPOSIT", StringComparison.OrdinalIgnoreCase)
                            && IsSettledPayment(p.PaymentStatus))
                .Sum(p => p.Amount ?? 0m);

            // Đợt cuối đang treo thì đóng lại, khỏi để khách quét mã trả sau khi đơn đã bị từ chối.
            foreach (var pending in payments.Where(p =>
                         string.Equals(p.InstallmentType, "FINAL_PAYMENT", StringComparison.OrdinalIgnoreCase)
                         && string.Equals(p.PaymentStatus, "PENDING", StringComparison.OrdinalIgnoreCase)))
            {
                pending.PaymentStatus = "CANCELLED";
                paymentRepo.Update(pending);
            }

            var now = DateTime.UtcNow;
            var reason = string.IsNullOrWhiteSpace(request?.Reason)
                ? "Khách quá hạn không thanh toán đợt cuối."
                : request!.Reason!.Trim();

            order.Status = StatusCancelledForfeited;
            order.Note = $"{reason} Tiền cọc {depositPaid:#,##0}đ không hoàn lại.";
            orderRepo.Update(order);

            await NotifyCustomerOfOrderAsync(order,
                $"Đơn {order.ConsignmentCode} đã bị huỷ do quá hạn thanh toán",
                $"Đơn ký gửi {order.ConsignmentCode} đã bị huỷ. Lý do: {reason} "
                + $"Theo điều khoản đã thoả thuận, tiền cọc {depositPaid:#,##0}đ không được hoàn lại. "
                + "Vui lòng liên hệ nhân viên kinh doanh nếu cần hỗ trợ.");

            await _unitOfWork.SaveChangesAsync();

            var actor = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            return BuildClosingResponse(order, actor?.FullName ?? string.Empty, now, order.Note);
        }

        private static bool IsSettledPayment(string? status) =>
            status?.Trim().ToUpperInvariant() is "PAID" or "SUCCESS" or "COMPLETED";

        private OrderClosingResponseDto BuildClosingResponse(
            Order order, string actedByName, DateTime actedAt, string? note) => new()
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                Status = order.Status ?? string.Empty,
                StatusLabel = GetConsignmentStatuses()
                    .FirstOrDefault(s => string.Equals(s.Code, order.Status, StringComparison.OrdinalIgnoreCase))
                    ?.Label ?? order.Status ?? string.Empty,
                ActedByName = actedByName,
                ActedAt = actedAt,
                Note = note
            };

        private async Task NotifyCustomerOfOrderAsync(Order order, string title, string content)
        {
            if (!order.CustomerId.HasValue) return;

            var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value);
            if (customer?.UserId is not Guid userId || userId == Guid.Empty) return;

            await _unitOfWork.Repository<Notification>().AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                Content = content,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        private async Task NotifyOrderStaffAsync(Order order, string title, string content)
        {
            if (!order.CreatedById.HasValue) return;

            await _unitOfWork.Repository<Notification>().AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = order.CreatedById.Value,
                Title = title,
                Content = content,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
