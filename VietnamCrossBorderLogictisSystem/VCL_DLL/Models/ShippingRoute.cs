using System;

namespace VCL_DLL.Models;

/// <summary>
/// Tuyến vận chuyển quốc tế được cấu hình sẵn, dùng khi gán tuyến cho lô hàng
/// và yêu cầu xuất kho (WRO).
/// </summary>
public partial class ShippingRoute
{
    public Guid Id { get; set; }

    public string RouteCode { get; set; } = string.Empty;

    public string RouteName { get; set; } = string.Empty;

    public string OriginCountry { get; set; } = string.Empty;

    /// <summary>Kho đi, để trống nếu tuyến áp dụng cho mọi kho tại quốc gia đi</summary>
    public Guid? OriginWarehouseId { get; set; }

    public string DestinationCountry { get; set; } = string.Empty;

    /// <summary>Kho đến, để trống nếu tuyến áp dụng cho mọi kho tại quốc gia đến</summary>
    public Guid? DestinationWarehouseId { get; set; }

    /// <summary>AIR, SEA, ROAD hoặc RAIL</summary>
    public string TransportMode { get; set; } = string.Empty;

    /// <summary>Đơn vị vận chuyển đảm nhận tuyến, có thể để trống</summary>
    public Guid? CarrierId { get; set; }

    /// <summary>Nhà cung cấp dịch vụ trung chuyển trên tuyến, có thể để trống</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>Thời gian vận chuyển dự kiến, tính bằng ngày</summary>
    public int? EstimatedTransitDays { get; set; }

    public string Note { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Warehouse OriginWarehouse { get; set; }

    public virtual Warehouse DestinationWarehouse { get; set; }

    public virtual Carrier Carrier { get; set; }

    public virtual Supplier Supplier { get; set; }
}
