using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class PricingRuleDto
    {
        public Guid Id { get; set; }
        public Guid? ServicePricingId { get; set; }
        public string RuleName { get; set; } = null!;
        public string RuleCode { get; set; } = null!;
        public string RuleType { get; set; } = null!;
        public string? ConditionType { get; set; }
        public string? ConditionValue { get; set; }
        public string CalculationType { get; set; } = null!;
        public decimal Value { get; set; }
        public decimal? MinAmount { get; set; }
        public decimal? MaxAmount { get; set; }
        public bool IsRequired { get; set; }
        public string Status { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Yêu cầu tạo cấu hình phụ phí mới.
    /// </summary>
    public class CreatePricingRuleRequest
    {
        /// <summary>
        /// ID bảng giá dịch vụ chính liên kết (Không bắt buộc).
        /// </summary>
        public Guid? ServicePricingId { get; set; }

        /// <summary>
        /// Tên hiển thị của quy tắc phụ phí (Bắt buộc, ví dụ: Phí đóng thùng gỗ).
        /// </summary>
        [Required(ErrorMessage = "Tên rule phụ phí không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên rule phụ phí không được quá 255 ký tự.")]
        public string RuleName { get; set; } = null!;

        /// <summary>
        /// Mã định danh quy tắc phụ phí (Bắt buộc, ví dụ: WOOD_CRATE).
        /// </summary>
        [Required(ErrorMessage = "Mã rule phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã rule phụ phí không được quá 100 ký tự.")]
        public string RuleCode { get; set; } = null!;

        /// <summary>
        /// Nhóm loại phụ phí (Bắt buộc, ví dụ: WOODEN_BOX, INSPECTION, OTHER).
        /// </summary>
        [Required(ErrorMessage = "Loại phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại phụ phí không được quá 100 ký tự.")]
        public string RuleType { get; set; } = null!;

        /// <summary>
        /// Loại điều kiện áp dụng (Không bắt buộc, ví dụ: MIN_WEIGHT, MIN_VOLUME, REQUIRES_INSPECTION, ALWAYS).
        /// </summary>
        [MaxLength(100, ErrorMessage = "Loại điều kiện không được quá 100 ký tự.")]
        public string? ConditionType { get; set; }

        /// <summary>
        /// Giá trị đi kèm của điều kiện (Không bắt buộc, ví dụ: 5.5 cho MIN_WEIGHT hoặc true cho REQUIRES_INSPECTION).
        /// </summary>
        [MaxLength(255, ErrorMessage = "Giá trị điều kiện không được quá 255 ký tự.")]
        public string? ConditionValue { get; set; }

        /// <summary>
        /// Cách thức tính phí (Bắt buộc, gồm: FIXED, PERCENTAGE, PER_KG, PER_CBM, PER_PRODUCT).
        /// </summary>
        [Required(ErrorMessage = "Cách tính phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Cách tính phí không được quá 100 ký tự.")]
        public string CalculationType { get; set; } = null!;

        /// <summary>
        /// Giá trị tính phí (Bắt buộc, số tiền cố định hoặc tỷ lệ phần trăm, >= 0).
        /// </summary>
        [Required(ErrorMessage = "Giá trị tính phí là bắt buộc.")]
        [Range(0, double.MaxValue, ErrorMessage = "Giá trị tính phí phải lớn hơn hoặc bằng 0.")]
        public decimal Value { get; set; }

        /// <summary>
        /// Mức phí tối thiểu có thể áp dụng (Không bắt buộc, >= 0).
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối thiểu phải lớn hơn hoặc bằng 0.")]
        public decimal? MinAmount { get; set; }

        /// <summary>
        /// Mức phí tối đa giới hạn (Không bắt buộc, >= 0).
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối đa phải lớn hơn hoặc bằng 0.")]
        public decimal? MaxAmount { get; set; }

        /// <summary>
        /// Xác định phụ phí này có bắt buộc áp dụng tự động hay không (Mặc định là false).
        /// </summary>
        public bool IsRequired { get; set; } = false;

        /// <summary>
        /// Trạng thái hoạt động (Bắt buộc, mặc định: ACTIVE, tối đa 50 ký tự).
        /// </summary>
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        [MaxLength(50, ErrorMessage = "Trạng thái không được quá 50 ký tự.")]
        public string Status { get; set; } = "ACTIVE";

        /// <summary>
        /// Ghi chú, mô tả chi tiết của quy tắc (Không bắt buộc, tối đa 500 ký tự).
        /// </summary>
        [MaxLength(500, ErrorMessage = "Mô tả không được quá 500 ký tự.")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Yêu cầu cập nhật cấu hình phụ phí.
    /// </summary>
    public class UpdatePricingRuleRequest
    {
        /// <summary>
        /// ID bảng giá dịch vụ chính liên kết (Không bắt buộc).
        /// </summary>
        public Guid? ServicePricingId { get; set; }

        /// <summary>
        /// Tên hiển thị của quy tắc phụ phí (Bắt buộc, ví dụ: Phí đóng thùng gỗ).
        /// </summary>
        [Required(ErrorMessage = "Tên rule phụ phí không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên rule phụ phí không được quá 255 ký tự.")]
        public string RuleName { get; set; } = null!;

        /// <summary>
        /// Mã định danh quy tắc phụ phí (Bắt buộc, ví dụ: WOOD_CRATE).
        /// </summary>
        [Required(ErrorMessage = "Mã rule phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã rule phụ phí không được quá 100 ký tự.")]
        public string RuleCode { get; set; } = null!;

        /// <summary>
        /// Nhóm loại phụ phí (Bắt buộc, ví dụ: WOODEN_BOX, INSPECTION, OTHER).
        /// </summary>
        [Required(ErrorMessage = "Loại phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại phụ phí không được quá 100 ký tự.")]
        public string RuleType { get; set; } = null!;

        /// <summary>
        /// Loại điều kiện áp dụng (Không bắt buộc, ví dụ: MIN_WEIGHT, MIN_VOLUME, REQUIRES_INSPECTION, ALWAYS).
        /// </summary>
        [MaxLength(100, ErrorMessage = "Loại điều kiện không được quá 100 ký tự.")]
        public string? ConditionType { get; set; }

        /// <summary>
        /// Giá trị đi kèm của điều kiện (Không bắt buộc, ví dụ: 5.5 cho MIN_WEIGHT hoặc true cho REQUIRES_INSPECTION).
        /// </summary>
        [MaxLength(255, ErrorMessage = "Giá trị điều kiện không được quá 255 ký tự.")]
        public string? ConditionValue { get; set; }

        /// <summary>
        /// Cách thức tính phí (Bắt buộc, gồm: FIXED, PERCENTAGE, PER_KG, PER_CBM, PER_PRODUCT).
        /// </summary>
        [Required(ErrorMessage = "Cách tính phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Cách tính phí không được quá 100 ký tự.")]
        public string CalculationType { get; set; } = null!;

        /// <summary>
        /// Giá trị tính phí (Bắt buộc, số tiền cố định hoặc tỷ lệ phần trăm, >= 0).
        /// </summary>
        [Required(ErrorMessage = "Giá trị tính phí là bắt buộc.")]
        [Range(0, double.MaxValue, ErrorMessage = "Giá trị tính phí phải lớn hơn hoặc bằng 0.")]
        public decimal Value { get; set; }

        /// <summary>
        /// Mức phí tối thiểu có thể áp dụng (Không bắt buộc, >= 0).
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối thiểu phải lớn hơn hoặc bằng 0.")]
        public decimal? MinAmount { get; set; }

        /// <summary>
        /// Mức phí tối đa giới hạn (Không bắt buộc, >= 0).
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối đa phải lớn hơn hoặc bằng 0.")]
        public decimal? MaxAmount { get; set; }

        /// <summary>
        /// Xác định phụ phí này có bắt buộc áp dụng tự động hay không.
        /// </summary>
        public bool IsRequired { get; set; }

        /// <summary>
        /// Trạng thái hoạt động (Bắt buộc, ví dụ: ACTIVE, INACTIVE, tối đa 50 ký tự).
        /// </summary>
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        [MaxLength(50, ErrorMessage = "Trạng thái không được quá 50 ký tự.")]
        public string Status { get; set; } = "ACTIVE";

        /// <summary>
        /// Ghi chú, mô tả chi tiết của quy tắc (Không bắt buộc, tối đa 500 ký tự).
        /// </summary>
        [MaxLength(500, ErrorMessage = "Mô tả không được quá 500 ký tự.")]
        public string? Description { get; set; }
    }
}
