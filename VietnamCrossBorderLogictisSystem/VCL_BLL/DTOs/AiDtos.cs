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

public class GeminiResponseDto
{
    public List<GeminiBinRecommendation>? Recommendations { get; set; }
}

public class GeminiBinRecommendation
{
    public Guid BinId { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CandidateBinDto
{
    public Guid LayoutId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? ZoneId { get; set; }
    public Guid? ShelfId { get; set; }
    public Guid BinId { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public decimal OccupiedVolume { get; set; }
    public decimal AvailableVolume { get; set; }
    public decimal OccupancyRatio { get; set; }
    public decimal CurrentWeight { get; set; }
    public decimal AvailableWeight { get; set; }
    public bool IsEntirelyEmpty { get; set; }
    public int SameCustomerParcelCount { get; set; }
    public int ShelfRowIndex { get; set; }
    public string BinLevelDescription { get; set; } = string.Empty;
}

public class SalesOrderStatusQueryRequest
{
    [Required(ErrorMessage = "Message không được để trống.")]
    public string Message { get; set; } = string.Empty;

    public string? OrderCode { get; set; }
    public Guid? CustomerId { get; set; }
    public string? RelatedType { get; set; }
    public Guid? RelatedId { get; set; }
}

public class SalesOrderStatusQueryResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<SalesQueryOrderSummaryDto> RelatedOrders { get; set; } = new();
    public List<SalesQueryParcelSummaryDto> RelatedParcels { get; set; } = new();
    public string CurrentStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string WarehouseStatus { get; set; } = string.Empty;
    public string ShipmentStatus { get; set; } = string.Empty;
    public string NextActionSuggestion { get; set; } = string.Empty;
    public List<string> DataSources { get; set; } = new();
    public string? Warning { get; set; }
}

public class SalesQueryOrderSummaryDto
{
    public Guid OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class SalesQueryParcelSummaryDto
{
    public Guid ParcelId { get; set; }
    public string PackageCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string LocalTrackingCode { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
}

public class CustomerOrderIntentRequest
{
    [Required(ErrorMessage = "Nội dung câu hỏi/yêu cầu (message) không được để trống.")]
    public string Message { get; set; } = string.Empty;

    public Guid? CustomerId { get; set; }
    public string? PreferredShippingRoute { get; set; }
}

public class ExtractOrderIntentResponseDto
{
    /// <summary>
    /// CONSIGNMENT (Ký gửi) hoặc PURCHASE (Mua hộ)
    /// </summary>
    public string OrderType { get; set; } = "CONSIGNMENT";

    public decimal ConfidenceScore { get; set; } = 0.85m;
    public string Explanation { get; set; } = string.Empty;

    // --- Thông tin Ký gửi (CONSIGNMENT) ---
    public string? ChinaTrackingCode { get; set; }
    public decimal? DeclaredValueCny { get; set; }
    public string? ShippingRoute { get; set; }
    public string? DestinationWarehouse { get; set; }

    // --- Thông tin Mua hộ (PURCHASE) ---
    public string? ProductLink { get; set; }
    public string? Variant { get; set; }
    public int? Quantity { get; set; }
    public decimal? UnitPriceCny { get; set; }

    // --- Thông tin hàng hoá & người nhận chung ---
    public string ProductName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? Note { get; set; }

    public List<ExtractedOrderItemDto> ExtractedItems { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ExtractedOrderItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public string? ProductLink { get; set; }
    public string? Variant { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal? UnitPriceCny { get; set; }
    public string? ChinaTrackingCode { get; set; }
}

