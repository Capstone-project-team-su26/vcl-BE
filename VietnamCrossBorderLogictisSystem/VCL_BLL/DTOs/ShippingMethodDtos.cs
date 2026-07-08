using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class ShippingMethodResponseDto
    {
        public Guid Id { get; set; }
        public string MethodName { get; set; } = string.Empty;
        public string MethodCode { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? EstimatedTransitTime { get; set; }
        public string? ApplicableCondition { get; set; }
        public bool IsActive { get; set; }
        public string? InternalNote { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class CreateShippingMethodRequestDto
    {
        [Required(ErrorMessage = "Tên phương thức vận chuyển không được để trống.")]
        public string MethodName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã phương thức vận chuyển không được để trống.")]
        public string MethodCode { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string? EstimatedTransitTime { get; set; }
        public string? ApplicableCondition { get; set; }
        public bool IsActive { get; set; } = true;
        public string? InternalNote { get; set; }
    }

    public class UpdateShippingMethodRequestDto
    {
        [Required(ErrorMessage = "Tên phương thức vận chuyển không được để trống.")]
        public string MethodName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã phương thức vận chuyển không được để trống.")]
        public string MethodCode { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string? EstimatedTransitTime { get; set; }
        public string? ApplicableCondition { get; set; }
        public bool IsActive { get; set; }
        public string? InternalNote { get; set; }
    }
}
