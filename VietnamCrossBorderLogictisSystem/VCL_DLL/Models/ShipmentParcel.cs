#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class ShipmentParcel
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }

    public Guid ParcelId { get; set; }

    public virtual Shipment Shipment { get; set; }

    public virtual Parcel Parcel { get; set; }
}
