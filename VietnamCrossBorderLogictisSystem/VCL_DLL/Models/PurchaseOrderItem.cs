#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class PurchaseOrderItem
{
    public Guid Id { get; set; }

    public Guid PurchaseOrderId { get; set; }

    public Guid PurchaseRequestItemId { get; set; }

    public string ProductName { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }

    public virtual PurchaseOrder PurchaseOrder { get; set; }
}
