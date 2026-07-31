using System;

namespace VCL_DLL.Models;

/// <summary>
/// Danh mục đơn vị tính dùng khi khách hàng khai báo hàng hoá
/// trong yêu cầu ký gửi và yêu cầu mua hộ.
/// </summary>
public partial class UnitOfMeasure
{
    public Guid Id { get; set; }

    /// <summary>Mã đơn vị tính, ví dụ PCS, BOX, CTN</summary>
    public string UnitCode { get; set; } = string.Empty;

    /// <summary>Tên hiển thị, ví dụ Cái, Hộp, Thùng</summary>
    public string UnitName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Thứ tự hiển thị trong danh sách chọn</summary>
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
