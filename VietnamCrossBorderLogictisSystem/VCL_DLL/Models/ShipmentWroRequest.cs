#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class ShipmentWroRequest
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }

    public Guid WroRequestId { get; set; }

    public virtual Shipment Shipment { get; set; }

    public virtual WroRequest WroRequest { get; set; }
}
