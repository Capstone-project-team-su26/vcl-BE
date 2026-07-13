using System;

namespace VCL_DLL.Models;

public partial class WarehouseLocationLayout
{
    public Guid Id { get; set; }

    public Guid WarehouseId { get; set; }

    public Guid? ZoneId { get; set; }

    public Guid? ShelfId { get; set; }

    public Guid? BinId { get; set; }

    public int RowIndex { get; set; }

    public int ColumnIndex { get; set; }

    public string DisplayLabel { get; set; }

    public string LayoutType { get; set; }

    public string Status { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? ColorCode { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Warehouse Warehouse { get; set; }

    public virtual WarehouseZone? Zone { get; set; }

    public virtual Shelf? Shelf { get; set; }

    public virtual Bin? Bin { get; set; }
}
