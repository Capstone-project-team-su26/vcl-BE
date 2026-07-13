using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs;

public class WarehouseZoneLayoutDto
{
    public Guid ZoneId { get; set; }
    public string ZoneCode { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public string ZoneType { get; set; } = string.Empty;
    public int? RowIndex { get; set; }
    public int? ColumnIndex { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string Status { get; set; } = "Active";
    public List<ShelfLayoutDto> Shelves { get; set; } = new();
}

public class ShelfLayoutDto
{
    public Guid ShelfId { get; set; }
    public string ShelfCode { get; set; } = string.Empty;
    public int? RowIndex { get; set; }
    public int? ColumnIndex { get; set; }
    public List<BinLayoutDto> Bins { get; set; } = new();
}

public class BinLayoutDto
{
    public Guid BinId { get; set; }
    public string BinCode { get; set; } = string.Empty;
    public int? RowIndex { get; set; }
    public int? ColumnIndex { get; set; }
    public string Status { get; set; } = "Active";
    public decimal? CurrentVolume { get; set; }
    public decimal? MaxVolume { get; set; }
    public decimal? CurrentWeight { get; set; }
    public decimal? MaxWeight { get; set; }
}
