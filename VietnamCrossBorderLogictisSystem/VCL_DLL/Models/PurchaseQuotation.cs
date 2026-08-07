#nullable disable
using System;
using System.Collections.Generic;

namespace VCL_DLL.Models;

public partial class PurchaseQuotation
{
    public Guid Id { get; set; }

    public Guid PurchaseRequestId { get; set; }

    public decimal ProductsSubtotal { get; set; }

    public decimal PurchaseFee { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal? Vat { get; set; }

    public decimal? ImportTax { get; set; }

    public decimal TotalAmount { get; set; }

    public string Note { get; set; }

    public string Status { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    /// <summary>Đồng tiền Sale nhập giá sản phẩm (CNY, USD, JPY...). "VND" nghĩa là không quy đổi.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Tỷ giá áp dụng, ĐÓNG BĂNG tại thời điểm lập báo giá. Bắt buộc phải lưu lại:
    /// nếu tính lại theo tỷ giá hiện hành thì báo giá cũ sẽ âm thầm đổi số tiền khi Admin sửa tỷ giá.
    /// </summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>Thời điểm chốt tỷ giá, để đối chiếu khi khách thắc mắc.</summary>
    public DateTime? ExchangeRateAt { get; set; }

    public virtual PurchaseRequest PurchaseRequest { get; set; }

    public virtual ICollection<PurchaseQuotationItem> PurchaseQuotationItems { get; set; } = new List<PurchaseQuotationItem>();

    public virtual ICollection<QuotationFee> QuotationFees { get; set; } = new List<QuotationFee>();
}
