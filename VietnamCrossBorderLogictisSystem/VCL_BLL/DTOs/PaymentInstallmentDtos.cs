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
        public decimal FinalAmount { get; set; }

        public Guid? PaymentId { get; set; }
        public long? OrderCode { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary>Link thanh toán payOS cho đợt cuối (null nếu final = 0, không cần thanh toán).</summary>
        public string? CheckoutUrl { get; set; }
        public string? PaymentLinkId { get; set; }
    }

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
}
