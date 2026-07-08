#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class PurchaseRequestItem
{
    public Guid Id { get; set; }

    public Guid PurchaseRequestId { get; set; }

    public string ProductLink { get; set; }

    public string SourceWebsite { get; set; }

    public string ProductType { get; set; }

    public string ProductName { get; set; }

    public int Quantity { get; set; }

    public string Attributes { get; set; }

    public string Note { get; set; }

    public string ImageUrl { get; set; }

    public virtual PurchaseRequest PurchaseRequest { get; set; }
}
