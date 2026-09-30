using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <inheritdoc cref="IPaymentConfirmationService"/>
    public class PaymentConfirmationService : IPaymentConfirmationService
    {
        public const string ReceivedUnallocatedStatus = "RECEIVED_UNALLOCATED";

        private const string StorageFeeInstallment = "STORAGE_FEE";
        private const string RedeliveryFeeInstallment = "REDELIVERY_FEE";

        // Đơn đã ra khỏi vòng thu tiền: tiền về lúc này không được làm đơn "sống lại".
        private static readonly string[] ClosedOrderStatuses =
            { "CANCELLED", "CANCELLED_FORFEITED", "REJECTED", "QUOTATION_REJECTED", "COMPLETED" };

        // Cọc chỉ đẩy đơn sang DEPOSIT_PAID khi đơn đang đúng mốc chờ cọc; không kéo lùi đơn đã đi xa hơn.
        private static readonly string[] AwaitingDepositStatuses =
            { "WAITING_DEPOSIT", "QUOTATION_CONFIRMED" };

        private static readonly string[] AwaitingPaymentStatuses =
            { "PENDING", "PENDING_RECONCILIATION" };

        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;
        private readonly ILogger<PaymentConfirmationService> _logger;

        public PaymentConfirmationService(
            IUnitOfWork unitOfWork,
            INotificationService notificationService,
            ILogger<PaymentConfirmationService> logger)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _logger = logger;
        }

        public async Task<PaymentConfirmationResult> ConfirmAsync(Payment payment, PaymentConfirmationInput input)
        {
            if (payment == null) throw new ArgumentNullException(nameof(payment));
            input ??= new PaymentConfirmationInput();

            var (order, invoice) = await ResolveOrderAsync(payment);
            var result = new PaymentConfirmationResult
            {
                Order = order,
                Invoice = invoice,
                OrderStatusBefore = order?.Status ?? string.Empty,
                OrderStatusAfter = order?.Status ?? string.Empty
            };

            if (IsStatus(payment.PaymentStatus, "PAID"))
            {
                result.Outcome = PaymentConfirmationOutcome.AlreadyConfirmed;
                result.Message = "Khoản thu đã được xác nhận trước đó.";
                return result;
            }

            var expected = payment.Amount ?? 0m;
            var received = input.ReceivedAmount ?? expected;
            if (received < expected)
            {
                result.Outcome = PaymentConfirmationOutcome.Underpaid;
                result.Message = $"Số tiền thực nhận {received:#,##0}đ nhỏ hơn số phải thu {expected:#,##0}đ.";
                return result;
            }

            var now = DateTime.UtcNow;
            payment.ReceivedAmount = received;
            payment.TransactionCode = string.IsNullOrWhiteSpace(input.Reference) ? payment.TransactionCode : input.Reference.Trim();
            payment.PaidAt = now;
            payment.ConfirmedAt = now;
            payment.ConfirmedById = input.ActorId;
            payment.ConfirmationSource = input.Source;

            var paymentOpen = AwaitingPaymentStatuses.Contains(payment.PaymentStatus?.Trim().ToUpperInvariant());
            var orderClosed = order != null && ClosedOrderStatuses.Contains(order.Status?.Trim().ToUpperInvariant());

            if (!paymentOpen || orderClosed)
            {
                // Tiền đã về tài khoản thật nên phải ghi lại, nhưng không được tính vào đơn.
                var reason = orderClosed
                    ? $"Tiền về khi đơn đã ở trạng thái {StatusNames.Quote(order!.Status)}."
                    : $"Tiền về cho khoản thu đã ở trạng thái {StatusNames.Quote(payment.PaymentStatus)}.";

                payment.PaymentStatus = ReceivedUnallocatedStatus;
                payment.FailureReason = $"{reason} Cần Admin xử lý hoàn tiền hoặc phân bổ lại.";
                _unitOfWork.Repository<Payment>().Update(payment);

                if (order != null)
                {
                    await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, order.Status, order.Status,
                        "PAYMENT_RECEIVED_UNALLOCATED",
                        $"{received:#,##0}đ ({StatusNames.Quote(payment.InstallmentType)}, mã {payment.TransactionCode}). {reason}",
                        input.ActorId);
                }

                await _unitOfWork.SaveChangesAsync();

                _logger.LogWarning(
                    "Khoản thu {PaymentId} nhận {Amount} nhưng không phân bổ được: {Reason}", payment.Id, received, reason);

                await _notificationService.NotifyRolesAsync(
                    new[] { "Admin" },
                    "Có tiền về cần xử lý",
                    $"Đơn {order?.ConsignmentCode ?? "(không rõ)"}: nhận {received:#,##0}đ. {reason} Kiểm tra để hoàn tiền hoặc phân bổ lại.");

                result.Outcome = PaymentConfirmationOutcome.ReceivedUnallocated;
                result.Message = reason;
                return result;
            }

            payment.PaymentStatus = "PAID";
            _unitOfWork.Repository<Payment>().Update(payment);

            var installment = payment.InstallmentType?.Trim().ToUpperInvariant();
            var depositConfirmed = false;

            if (order != null)
            {
                var before = order.Status;

                if (installment == "DEPOSIT")
                {
                    order.PaymentConfirmedAt = now;
                    if (AwaitingDepositStatuses.Contains(order.Status?.Trim().ToUpperInvariant()))
                    {
                        order.Status = "DEPOSIT_PAID";
                    }

                    depositConfirmed = true;
                }
                else if (installment == StorageFeeInstallment || installment == RedeliveryFeeInstallment)
                {
                    // Phí lưu kho / phí giao lại thu riêng, không phải một đợt của hoá đơn gốc:
                    // không đụng trạng thái đơn.
                }
                else
                {
                    order.PaymentConfirmedAt = now;
                    if (invoice != null)
                    {
                        invoice.InvoiceStatus = "PAID";
                        _unitOfWork.Repository<Invoice>().Update(invoice);
                    }

                    if (IsStatus(order.Status, "WAITING_FINAL_PAYMENT"))
                    {
                        order.Status = "PAID";
                    }
                }

                _unitOfWork.Repository<Order>().Update(order);

                await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, before, order.Status,
                    $"PAYMENT_CONFIRMED_{installment ?? "UNKNOWN"}",
                    $"Xác nhận thu {received:#,##0}đ qua {input.Source}"
                    + (string.IsNullOrWhiteSpace(payment.TransactionCode) ? "." : $", mã {payment.TransactionCode}.")
                    + (string.IsNullOrWhiteSpace(input.Note) ? string.Empty : $" {input.Note.Trim()}"),
                    input.ActorId);

                result.OrderStatusAfter = order.Status ?? string.Empty;
            }

            await _unitOfWork.SaveChangesAsync();

            // Thanh toán xong: Sale lập phiếu nhập kho cho khách (đơn mua hộ vào kho theo luồng mua hộ).
            if (depositConfirmed && order != null
                && !string.Equals(order.OrderType, "PURCHASE", StringComparison.OrdinalIgnoreCase))
            {
                await _notificationService.NotifyRolesAsync(
                    new[] { "Sale", "Sales" },
                    "Đơn đã thanh toán, cần lập phiếu nhập kho",
                    $"Đơn {order.ConsignmentCode} đã nhận cọc {received:#,##0}đ. Lập phiếu nhập kho để quản lý kho duyệt.",
                    excludeUserId: input.ActorId);
            }

            result.Outcome = PaymentConfirmationOutcome.Confirmed;
            result.Message = "Đã xác nhận tiền thực nhận.";
            return result;
        }

        private async Task<(Order? Order, Invoice? Invoice)> ResolveOrderAsync(Payment payment)
        {
            if (!payment.InvoiceId.HasValue) return (null, null);

            var invoice = await _unitOfWork.Repository<Invoice>().GetByIdAsync(payment.InvoiceId.Value);
            if (invoice?.QuotationId == null) return (null, invoice);

            var quotation = await _unitOfWork.Repository<Quotation>().GetByIdAsync(invoice.QuotationId.Value);
            if (quotation?.OrderId == null) return (null, invoice);

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(quotation.OrderId.Value);
            return (order, invoice);
        }

        private static bool IsStatus(string? value, string expected) =>
            string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
