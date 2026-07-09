#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class PurchasePayment
{
    public Guid Id { get; set; }

    public Guid PurchaseRequestId { get; set; }

    public Guid PurchaseQuotationId { get; set; }

    /// <summary>Mã đơn hàng gửi sang payOS (số nguyên, duy nhất).</summary>
    public long OrderCode { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Phương thức thanh toán (VD: PAYOS).</summary>
    public string PaymentMethod { get; set; }

    /// <summary>PENDING | PAID | CANCELLED | EXPIRED.</summary>
    public string Status { get; set; }

    /// <summary>Id link thanh toán do payOS trả về.</summary>
    public string PaymentLinkId { get; set; }

    /// <summary>URL trang thanh toán payOS để FE điều hướng khách hàng.</summary>
    public string CheckoutUrl { get; set; }

    /// <summary>Mã giao dịch/tham chiếu khi thanh toán thành công.</summary>
    public string TransactionCode { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public virtual PurchaseQuotation PurchaseQuotation { get; set; }
}
