using System;

namespace VCL_DLL.Models;

/// <summary>
/// Nhật ký di chuyển của kiện hàng trong kho: cất kiện lần đầu (PUTAWAY)
/// và điều chuyển vị trí nội bộ (RELOCATION).
/// Mỗi lần ghi giữ lại vị trí cũ, vị trí mới, người thực hiện và thời điểm (BR-47).
/// </summary>
public partial class InventoryMovement
{
    public Guid Id { get; set; }

    public Guid InventoryId { get; set; }

    public Guid ParcelId { get; set; }

    /// <summary>Ô kệ trước khi di chuyển. Null khi đây là lần cất kiện đầu tiên.</summary>
    public Guid? FromBinId { get; set; }

    public Guid ToBinId { get; set; }

    /// <summary>PUTAWAY hoặc RELOCATION</summary>
    public string MovementType { get; set; } = string.Empty;

    /// <summary>Lý do điều chuyển do nhân viên kho nhập</summary>
    public string Reason { get; set; } = string.Empty;

    public int Quantity { get; set; }

    /// <summary>Nhân viên thực hiện thao tác</summary>
    public Guid? MovedById { get; set; }

    public DateTime MovedAt { get; set; }

    public virtual Inventory Inventory { get; set; }

    public virtual Parcel Parcel { get; set; }

    public virtual Bin FromBin { get; set; }

    public virtual Bin ToBin { get; set; }

    public virtual User MovedBy { get; set; }
}
