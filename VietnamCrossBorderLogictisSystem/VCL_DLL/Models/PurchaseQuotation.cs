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

    public virtual PurchaseRequest PurchaseRequest { get; set; }

    public virtual ICollection<PurchaseQuotationItem> PurchaseQuotationItems { get; set; } = new List<PurchaseQuotationItem>();

    public virtual ICollection<QuotationFee> QuotationFees { get; set; } = new List<QuotationFee>();
}
