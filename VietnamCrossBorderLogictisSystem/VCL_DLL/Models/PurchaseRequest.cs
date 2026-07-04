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

    public string Note { get; set; }

    public string Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Customer Customer { get; set; }

    public virtual User User { get; set; }

    public virtual ICollection<PurchaseRequestItem> PurchaseRequestItems { get; set; } = new List<PurchaseRequestItem>();
}
