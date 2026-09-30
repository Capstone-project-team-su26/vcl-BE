#nullable disable
using System;

namespace VCL_DLL.Models;

// Cột bổ sung cho luồng mua hộ chuẩn (docs/migrate-source-cu/08-luong-mua-ho.md mục 6).
// Tách khỏi file model sinh tự động để lần chạy EF Core Power Tools sau không ghi đè mất.

public partial class PurchaseQuotation
{
    /// <summary>Ship nội địa từ NCC tới kho nguồn — thu trước cùng tiền hàng.</summary>
    public decimal DomesticShippingFee { get; set; }

    /// <summary>Đơn giá cước quốc tế /kg đã báo khách. Tất toán ở VN tính lại theo đúng đơn giá này.</summary>
    public decimal? FreightRatePerKg { get; set; }

    /// <summary>Cân nặng ước tính để ra cước tạm tính (<see cref="ShippingFee"/>).</summary>
    public decimal? EstimatedWeight { get; set; }

    public Guid? ServicePricingId { get; set; }

    /// <summary>% VAT áp cho phí dịch vụ và cước.</summary>
    public decimal? VatRate { get; set; }

    /// <summary>
    /// Phần trả trước = tiền hàng + phí mua + ship nội địa + phụ phí + VAT phần phí.
    /// Null là báo giá đời cũ (thu cọc theo tỷ lệ).
    /// </summary>
    public decimal? PrepayAmount { get; set; }

    /// <summary>Phần tạm tính thu ở VN theo cân đo thật = cước quốc tế + VAT cước + thuế NK.</summary>
    public decimal? EstimatedLaterAmount { get; set; }
}

public partial class PurchasePayment
{
    /// <summary>Khoản chênh giá của một đơn mua NCC (PRICE_DIFFERENCE). Null với khoản của cả yêu cầu.</summary>
    public Guid? PurchaseOrderId { get; set; }
}

public partial class PurchaseOrder
{
    public Guid? SupplierId { get; set; }

    /// <summary>Kho nguồn nhận hàng từ NCC.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>Đồng tiền giá mua NCC; đơn giá VND nằm ở dòng hàng.</summary>
    public string Currency { get; set; }

    public decimal? ExchangeRate { get; set; }

    /// <summary>Số thứ tự đơn mua trong yêu cầu — đơn kho sinh ra mang mã PUR-xxx-{số này}.</summary>
    public int? SequenceNo { get; set; }

    /// <summary>Tiền hàng theo giá đã báo khách cho đúng các dòng/số lượng của đơn mua này.</summary>
    public decimal? QuotedGoodsAmount { get; set; }

    /// <summary>Phần khách phải trả thêm vì giá thực vượt giá báo quá ngưỡng. 0 / null là không có.</summary>
    public decimal? PriceDifferenceAmount { get; set; }

    public DateTime? CustomerDecisionAt { get; set; }
    public string CustomerRejectReason { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public string DecisionNote { get; set; }

    public string SupplierOrderCode { get; set; }
    public DateTime? OrderedAt { get; set; }
    public Guid? OrderedBy { get; set; }
    public DateTime? SupplierConfirmedAt { get; set; }
    public DateTime? SupplierShippedAt { get; set; }
    public string DomesticCarrier { get; set; }
    public string DomesticTrackingCode { get; set; }

    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string CancelReason { get; set; }
}

public partial class PurchaseOrderItem
{
    /// <summary>Đơn giá NCC theo nguyên tệ. Null khi nhập thẳng VND.</summary>
    public decimal? UnitPriceOriginal { get; set; }

    /// <summary>Đơn giá VND đã báo khách cho sản phẩm này.</summary>
    public decimal? QuotedUnitPrice { get; set; }
}

public partial class Order
{
    /// <summary>Đơn mua NCC sinh ra đơn kho này (luồng mua hộ mới). Null với ký gửi và đơn mua hộ đời cũ.</summary>
    public Guid? PurchaseOrderId { get; set; }
}
