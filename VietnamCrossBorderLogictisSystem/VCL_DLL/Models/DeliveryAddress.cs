#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class DeliveryAddress
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string ReceiverName { get; set; }

    public string ReceiverPhone { get; set; }

    public string Address { get; set; }

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Customer Customer { get; set; }
}
