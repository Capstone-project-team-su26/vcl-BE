using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>Giới hạn ký gửi đang áp dụng (null = không giới hạn) — form tạo đơn đọc để hiện gợi ý và kiểm.</summary>
    public class ConsignmentOrderLimitsDto
    {
        public decimal? MaxParcelWeightKg { get; set; }
        public decimal? MaxParcelLengthCm { get; set; }
        public decimal? MaxParcelWidthCm { get; set; }
        public decimal? MaxParcelHeightCm { get; set; }
        public int? MaxParcelQuantity { get; set; }
        public decimal? MaxItemDeclaredValue { get; set; }
        public decimal? MaxTotalWeightKg { get; set; }
        public decimal? MaxTotalDeclaredValue { get; set; }
        public int MaxPackages { get; set; }
    }

    /// <summary>Giới hạn mua hộ đang áp dụng (null = không giới hạn).</summary>
    public class PurchaseOrderLimitsDto
    {
        public int MaxItems { get; set; }
        public int? MaxItemQuantity { get; set; }
    }

    /// <summary>Một dòng cấu hình giới hạn kèm nhãn, đơn vị, khoảng hợp lệ để trang Admin dựng form.</summary>
    public class OrderLimitSettingItemDto
    {
        public string Key { get; set; } = string.Empty;
        /// <summary>CONSIGNMENT | PURCHASE.</summary>
        public string Scope { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        /// <summary>Giá trị đang áp dụng; null = không giới hạn.</summary>
        public decimal? Value { get; set; }
        public decimal? DefaultValue { get; set; }
        public bool AllowUnlimited { get; set; }
        public bool IsInteger { get; set; }
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        /// <summary>Chỉ trả cho Admin.</summary>
        public DateTime? UpdatedAt { get; set; }
        public Guid? UpdatedById { get; set; }
        public string? UpdatedByName { get; set; }
    }

    public class OrderLimitSettingsResponseDto
    {
        public ConsignmentOrderLimitsDto Consignment { get; set; } = new();
        public PurchaseOrderLimitsDto Purchase { get; set; } = new();
        public List<OrderLimitSettingItemDto> Items { get; set; } = new();
        /// <summary>Lần sửa gần nhất (chỉ trả cho Admin).</summary>
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByName { get; set; }
    }

    public class OrderLimitValueDto
    {
        [Required(ErrorMessage = "Thiếu mã giới hạn.")]
        public string Key { get; set; } = string.Empty;

        /// <summary>Giá trị mới; null = không giới hạn (chỉ với giới hạn cho phép).</summary>
        public decimal? Value { get; set; }
    }

    /// <summary>Admin sửa một hoặc nhiều giới hạn. Khoá không gửi lên thì giữ nguyên.</summary>
    public class UpdateOrderLimitSettingsRequestDto
    {
        [Required(ErrorMessage = "Danh sách giới hạn không được để trống.")]
        [MinLength(1, ErrorMessage = "Danh sách giới hạn không được để trống.")]
        [MaxLength(50, ErrorMessage = "Danh sách giới hạn quá dài.")]
        public List<OrderLimitValueDto> Items { get; set; } = new();

        /// <summary>Lý do thay đổi (tuỳ chọn) — ghi vào nhật ký.</summary>
        [StringLength(500, ErrorMessage = "Lý do thay đổi tối đa 500 ký tự.")]
        public string? Note { get; set; }
    }

    public class OrderLimitHistoryItemDto
    {
        public Guid Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal? OldValue { get; set; }
        public decimal? NewValue { get; set; }
        public DateTime ChangedAt { get; set; }
        public Guid? ChangedById { get; set; }
        public string? ChangedByName { get; set; }
        public string? Note { get; set; }
    }
}
