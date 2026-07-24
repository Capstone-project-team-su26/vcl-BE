#nullable disable
using System;
using System.Collections.Generic;

namespace VCL_DLL.Models;

public partial class PurchaseRequest
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public Guid? CustomerId { get; set; }

    public string PurchaseCode { get; set; }

    public string Route { get; set; }

    public string ShippingOption { get; set; }

    public string ReceiverName { get; set; }

    public string ReceiverPhone { get; set; }

    public string ReceiverAddress { get; set; }

    public bool RequiresInspection { get; set; }

    public bool RequiresQuantityCheck { get; set; }

    public bool RequiresPacking { get; set; }

    public bool RequiresWoodenCrate { get; set; }

    public bool RequiresInsurance { get; set; }

    /// <summary>ID các PricingRule khách chọn, nối bằng "|".</summary>
    public string PricingRuleIds { get; set; }

    public string GeneralNote { get; set; }

    public string Status { get; set; }

    public string Reason { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Customer Customer { get; set; }

    public virtual User User { get; set; }

    public virtual ICollection<PurchaseRequestItem> PurchaseRequestItems { get; set; } = new List<PurchaseRequestItem>();
}
