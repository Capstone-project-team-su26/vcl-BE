#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class PurchaseQuotationItem
{
    public Guid Id { get; set; }

    public Guid PurchaseQuotationId { get; set; }

    public Guid PurchaseRequestItemId { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }

    public virtual PurchaseQuotation PurchaseQuotation { get; set; }
}
