using System;

namespace VCL_DLL.Models;

/// <summary>Một kiện hàng nằm trong phiếu nhập kho VN.</summary>
public partial class WarehouseInboundItem
{
    public Guid Id { get; set; }

    public Guid InboundRequestId { get; set; }

    public Guid ParcelId { get; set; }

    public virtual WarehouseInboundRequest InboundRequest { get; set; }

    public virtual Parcel Parcel { get; set; }
}
