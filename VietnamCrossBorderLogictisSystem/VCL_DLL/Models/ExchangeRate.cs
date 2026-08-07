using System;

namespace VCL_DLL.Models;

/// <summary>
/// Tỷ giá quy đổi ngoại tệ sang VND do công ty tự quy định (không lấy tỷ giá thị trường),
/// dùng cho luồng mua hộ khi Sale nhập giá sản phẩm bằng ngoại tệ.
/// Tỷ giá là thuộc tính của đồng tiền, không phải của tuyến vận chuyển.
/// </summary>
public partial class ExchangeRate
{
    public Guid Id { get; set; }

    /// <summary>Mã ISO 4217 của ngoại tệ, ví dụ CNY, USD, JPY, KRW.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Tên hiển thị, ví dụ "Nhân dân tệ".</summary>
    public string? CurrencyName { get; set; }

    /// <summary>Số VND đổi được 1 đơn vị ngoại tệ.</summary>
    public decimal RateToVnd { get; set; }

    /// <summary>Chỉ tỷ giá đang bật mới được dùng để báo giá.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Ghi chú lý do đặt mức tỷ giá này (biên rủi ro, phí chuyển tiền...).</summary>
    public string? Note { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
