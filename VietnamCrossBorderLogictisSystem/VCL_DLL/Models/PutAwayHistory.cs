using System;

namespace VCL_DLL.Models;

public partial class PutAwayHistory
{
    public Guid Id { get; set; }

    public decimal ParcelWeight { get; set; }

    public decimal Volume { get; set; }

    public string Category { get; set; } = string.Empty;

    public Guid WarehouseId { get; set; }

    public Guid ZoneId { get; set; }

    public Guid ShelfId { get; set; }

    public Guid BinId { get; set; }

    public DateTime ChosenAt { get; set; }
}
