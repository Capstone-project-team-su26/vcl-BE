using System;
using System.Threading.Tasks;
using VCL_DLL.Models;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Nơi duy nhất ghi nhận "tiền đã thực nhận" cho khoản thu của đơn ký gửi, dùng chung cho
    /// webhook cổng thanh toán và Admin duyệt chuyển khoản tay.
    /// </summary>
    public interface IPaymentConfirmationService
    {
        Task<PaymentConfirmationResult> ConfirmAsync(Payment payment, PaymentConfirmationInput input);
    }

    public static class PaymentConfirmationSources
    {
        public const string PayOs = "GATEWAY_PAYOS";
        public const string SePay = "GATEWAY_SEPAY";
        public const string AdminManual = "ADMIN_MANUAL";
        public const string SystemZero = "SYSTEM_ZERO";
    }

    public class PaymentConfirmationInput
    {
        public string Source { get; set; } = string.Empty;

        /// <summary>Số tiền thực nhận. Null thì coi bằng số phải thu (cổng không báo số tiền).</summary>
        public decimal? ReceivedAmount { get; set; }

        public string? Reference { get; set; }
        public Guid? ActorId { get; set; }
        public string? Note { get; set; }
    }

    public enum PaymentConfirmationOutcome
    {
        Confirmed,
        AlreadyConfirmed,
        Underpaid,

        /// <summary>
        /// Tiền đã về nhưng đơn không còn nhận tiền (đã huỷ, từ chối, hoàn tất) hoặc khoản thu đã
        /// bị huỷ. Tiền được ghi lại để Admin xử lý, không làm đổi trạng thái đơn.
        /// </summary>
        ReceivedUnallocated
    }

    public class PaymentConfirmationResult
    {
        public PaymentConfirmationOutcome Outcome { get; set; }
        public Order? Order { get; set; }
        public Invoice? Invoice { get; set; }
        public string OrderStatusBefore { get; set; } = string.Empty;
        public string OrderStatusAfter { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
