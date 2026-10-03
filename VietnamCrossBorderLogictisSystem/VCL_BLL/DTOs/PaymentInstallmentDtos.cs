using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>Một dòng chi phí phát sinh do Sale nhập khi tạo thanh toán cuối.</summary>
    public class ExtraFeeLineDto
    {
        [Required(ErrorMessage = "Tên khoản phí phát sinh không được để trống.")]
        public string Name { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Số tiền phí phát sinh không được âm.")]
        public decimal Amount { get; set; }

        public string? Note { get; set; }
    }

    public class CreateFinalPaymentRequest
    {
        /// <summary>Danh sách chi phí phát sinh thêm (nếu có).</summary>
        public List<ExtraFeeLineDto> ExtraFees { get; set; } = new();

        public string? ReturnUrl { get; set; }
        public string? CancelUrl { get; set; }

        /// <summary>Phương thức thanh toán: PAYOS hoặc SEPAY. Nếu trống, mặc định dùng PAYOS hoặc theo cấu hình hệ thống.</summary>
        public string? PaymentMethod { get; set; }

        /// <summary>
        /// Chủ động phát hành LẠI đợt cuối đang chờ khách trả (huỷ mã cũ, tính lại phí lưu kho theo
        /// ngày hiện tại). Mặc định false: đơn đã có đợt cuối chờ trả thì API trả 409, để bấm đúp /
        /// bấm lại trên hàng chờ không âm thầm huỷ mã QR khách đang cầm.
        /// </summary>
        public bool Reissue { get; set; }
    }

    public class CreateFinalPaymentResponse
    {
        public Guid OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;

        public Guid InvoiceId { get; set; }
        public decimal TotalBillAmount { get; set; }
        public decimal DepositPaid { get; set; }
        public decimal ExtraFeesTotal { get; set; }

        /// <summary>
        /// Phần điều chỉnh cước (kèm VAT) theo cân đo tại kho VN ghi thêm ở lần phát hành này —
        /// đã nằm trong <see cref="ExtraFeesTotal"/>. Chi tiết xem GET /api/orders/{orderId}/settlement-preview.
        /// </summary>
        public decimal FreightAdjustmentDelta { get; set; }
        public decimal FinalAmount { get; set; }

        public Guid? PaymentId { get; set; }
        public long? OrderCode { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary>Link thanh toán payOS cho đợt cuối (null nếu final = 0, không cần thanh toán).</summary>
        public string? CheckoutUrl { get; set; }
        public string? PaymentLinkId { get; set; }
    }

    // ----- VCL-209: Sale xác nhận thanh toán đơn ký gửi -----

    /// <summary>Một đợt thanh toán trong lịch sử của đơn.</summary>
    public class OrderPaymentItemDto
    {
        public Guid PaymentId { get; set; }
        public Guid? InvoiceId { get; set; }
        public string? InstallmentType { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public long? OrderCode { get; set; }
        public string? TransactionCode { get; set; }
        public string? CheckoutUrl { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public class OrderPaymentsResponse
    {
        public Guid OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public decimal TotalBillAmount { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Remaining { get; set; }
        public List<OrderPaymentItemDto> Payments { get; set; } = new();
    }

    // ----- VCL-207: Lịch sử thanh toán chi tiết theo đơn -----

    public class PaymentHistoryCustomerDto
    {
        public Guid? CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    public class PaymentHistoryQuotationDto
    {
        public Guid QuotationId { get; set; }
        public string? QuoteType { get; set; }
        public string? Status { get; set; }
        public decimal TotalAmount { get; set; }
    }

    public class PaymentHistoryItemDto
    {
        public Guid PaymentId { get; set; }
        public Guid? InvoiceId { get; set; }

        /// <summary>DEPOSIT | FINAL_PAYMENT (null với thanh toán 1 lần cũ).</summary>
        public string? InstallmentType { get; set; }

        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;

        /// <summary>Trạng thái chuẩn hóa cho FE: PENDING | SUCCESS | FAILED | CANCELED.</summary>
        public string Status { get; set; } = string.Empty;

        public long? OrderCode { get; set; }

        /// <summary>Mã giao dịch từ payOS (nếu thành công).</summary>
        public string? TransactionCode { get; set; }
        public string? CheckoutUrl { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public string? FailureReason { get; set; }
    }

    public class PaymentHistoryResponse
    {
        public Guid OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;

        public PaymentHistoryCustomerDto Customer { get; set; } = new();
        public PaymentHistoryQuotationDto? Quotation { get; set; }

        public decimal TotalBillAmount { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Remaining { get; set; }

        public List<PaymentHistoryItemDto> Payments { get; set; } = new();
    }

    /// <summary>
    /// Yêu cầu chốt phí lưu kho để thu một lần lúc xuất hàng (Pay-at-Pickup).
    /// </summary>
    public class CreateStorageFeePaymentRequest
    {
        /// <summary>SEPAY hoặc PAYOS. Bỏ trống thì dùng SEPAY.</summary>
        public string? PaymentMethod { get; set; }

        public string? ReturnUrl { get; set; }
        public string? CancelUrl { get; set; }
    }

    /// <summary>
    /// Khoản phí lưu kho đã chốt, kèm mã thanh toán cho khách quét tại quầy.
    /// </summary>
    public class CreateStorageFeePaymentResponse
    {
        public Guid OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;

        /// <summary>Số ngày miễn phí đang áp dụng.</summary>
        public int FreeDays { get; set; }

        /// <summary>Đơn giá một kiện một ngày.</summary>
        public decimal UnitPrice { get; set; }

        public string Currency { get; set; } = "VND";

        /// <summary>Tổng phí lưu kho chốt tại thời điểm gọi.</summary>
        public decimal StorageFeeAmount { get; set; }

        /// <summary>Diễn giải cách tính, hiện thẳng cho khách xem.</summary>
        public string? Note { get; set; }

        /// <summary>Chi tiết từng kiện: số ngày lưu, số ngày tính tiền, thành tiền.</summary>
        public List<StorageFeeParcelDto> Parcels { get; set; } = new();

        public Guid? PaymentId { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary>Null khi phí bằng 0 — không cần thu thì không phát hành mã.</summary>
        public string? CheckoutUrl { get; set; }

        public long? OrderCode { get; set; }

        /// <summary>True khi không phát sinh phí, kho cho xuất hàng ngay.</summary>
        public bool IsFreeOfCharge { get; set; }
    }
}
