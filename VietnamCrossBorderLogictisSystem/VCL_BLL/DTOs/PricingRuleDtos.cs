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

    public class CreatePricingRuleRequest
    {
        public Guid? ServicePricingId { get; set; }

        [Required(ErrorMessage = "Tên rule phụ phí không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên rule phụ phí không được quá 255 ký tự.")]
        public string RuleName { get; set; } = null!;

        [Required(ErrorMessage = "Mã rule phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã rule phụ phí không được quá 100 ký tự.")]
        public string RuleCode { get; set; } = null!;

        [Required(ErrorMessage = "Loại phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại phụ phí không được quá 100 ký tự.")]
        public string RuleType { get; set; } = null!;

        [MaxLength(100, ErrorMessage = "Loại điều kiện không được quá 100 ký tự.")]
        public string? ConditionType { get; set; }

        [MaxLength(255, ErrorMessage = "Giá trị điều kiện không được quá 255 ký tự.")]
        public string? ConditionValue { get; set; }

        [Required(ErrorMessage = "Cách tính phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Cách tính phí không được quá 100 ký tự.")]
        public string CalculationType { get; set; } = null!;

        [Range(0, double.MaxValue, ErrorMessage = "Giá trị tính phí phải lớn hơn hoặc bằng 0.")]
        public decimal Value { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối thiểu phải lớn hơn hoặc bằng 0.")]
        public decimal? MinAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối đa phải lớn hơn hoặc bằng 0.")]
        public decimal? MaxAmount { get; set; }

        public bool IsRequired { get; set; } = false;

        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        [MaxLength(50, ErrorMessage = "Trạng thái không được quá 50 ký tự.")]
        public string Status { get; set; } = "ACTIVE";

        [MaxLength(500, ErrorMessage = "Mô tả không được quá 500 ký tự.")]
        public string? Description { get; set; }
    }

    public class UpdatePricingRuleRequest
    {
        public Guid? ServicePricingId { get; set; }

        [Required(ErrorMessage = "Tên rule phụ phí không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên rule phụ phí không được quá 255 ký tự.")]
        public string RuleName { get; set; } = null!;

        [Required(ErrorMessage = "Mã rule phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã rule phụ phí không được quá 100 ký tự.")]
        public string RuleCode { get; set; } = null!;

        [Required(ErrorMessage = "Loại phụ phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại phụ phí không được quá 100 ký tự.")]
        public string RuleType { get; set; } = null!;

        [MaxLength(100, ErrorMessage = "Loại điều kiện không được quá 100 ký tự.")]
        public string? ConditionType { get; set; }

        [MaxLength(255, ErrorMessage = "Giá trị điều kiện không được quá 255 ký tự.")]
        public string? ConditionValue { get; set; }

        [Required(ErrorMessage = "Cách tính phí không được để trống.")]
        [MaxLength(100, ErrorMessage = "Cách tính phí không được quá 100 ký tự.")]
        public string CalculationType { get; set; } = null!;

        [Range(0, double.MaxValue, ErrorMessage = "Giá trị tính phí phải lớn hơn hoặc bằng 0.")]
        public decimal Value { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối thiểu phải lớn hơn hoặc bằng 0.")]
        public decimal? MinAmount { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Mức phí tối đa phải lớn hơn hoặc bằng 0.")]
        public decimal? MaxAmount { get; set; }

        public bool IsRequired { get; set; }

        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        [MaxLength(50, ErrorMessage = "Trạng thái không được quá 50 ký tự.")]
        public string Status { get; set; } = "ACTIVE";

        [MaxLength(500, ErrorMessage = "Mô tả không được quá 500 ký tự.")]
        public string? Description { get; set; }
    }
}
