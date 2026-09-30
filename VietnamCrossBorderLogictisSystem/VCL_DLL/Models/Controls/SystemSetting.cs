#nullable disable
using System;

namespace VCL_DLL.Models;

/// <summary>
/// Một tham số hệ thống Admin chỉnh được trên web (hiện dùng cho giới hạn tạo đơn ký gửi / mua hộ).
/// <para>
/// <see cref="DecimalValue"/> = <c>null</c> nghĩa là "không giới hạn". Chưa có dòng nào cho một khoá
/// thì service dùng giá trị mặc định trong code — nên chạy code mới trước khi chạy SQL cũng không gãy.
/// </para>
/// </summary>
public partial class SystemSetting
{
    public Guid Id { get; set; }

    /// <summary>Mã tham số, VD <c>ORDER_MAX_TOTAL_WEIGHT_KG</c>. Duy nhất.</summary>
    public string SettingKey { get; set; }

    /// <summary>Nhóm tham số, VD <c>ORDER_LIMITS</c>.</summary>
    public string SettingGroup { get; set; }

    /// <summary>Giá trị số; <c>null</c> = không giới hạn.</summary>
    public decimal? DecimalValue { get; set; }

    public string Description { get; set; }

    /// <summary>Admin sửa gần nhất (null = giá trị khởi tạo).</summary>
    public Guid? UpdatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Nhật ký thay đổi tham số hệ thống: ai đổi, lúc nào, từ giá trị nào sang giá trị nào.</summary>
public partial class SystemSettingHistory
{
    public Guid Id { get; set; }

    public string SettingKey { get; set; }

    public decimal? OldValue { get; set; }

    public decimal? NewValue { get; set; }

    public Guid? ChangedById { get; set; }

    public DateTime ChangedAt { get; set; }

    public string Note { get; set; }
}
