using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs;

public class PutawaySuggestionRequest
{
    [Required(ErrorMessage = "Mã kiện hàng (parcelId) là bắt buộc.")]
    public Guid ParcelId { get; set; }

    [Required(ErrorMessage = "Mã kho hàng (warehouseId) là bắt buộc.")]
    public Guid WarehouseId { get; set; }

    public Guid? PreferredZoneId { get; set; }

    public string? Note { get; set; }
}

public class PutawaySuggestionResponseDto
{
    public List<SuggestedLocationDto> SuggestedLocations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class SuggestedLocationDto
{
    public Guid LocationId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? ZoneId { get; set; }
    public Guid? ShelfId { get; set; }
    public Guid? BinId { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string Reason { get; set; } = string.Empty;
}
