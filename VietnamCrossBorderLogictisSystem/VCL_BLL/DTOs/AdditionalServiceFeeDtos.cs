using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class AdditionalServiceFeeResponseDto
    {
        public Guid Id { get; set; }
        public string FeeName { get; set; } = string.Empty;
        public string FeeCode { get; set; } = string.Empty;
        public string CalculationType { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string? Unit { get; set; }
        public bool IsActive { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CreateAdditionalServiceFeeRequestDto
    {
        [Required(ErrorMessage = "Tên loại phí không được để trống.")]
        [MaxLength(150, ErrorMessage = "Tên loại phí tối đa 150 ký tự.")]
        public string FeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã loại phí không được để trống.")]
        [MaxLength(50, ErrorMessage = "Mã loại phí tối đa 50 ký tự.")]
        public string FeeCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Cách tính phí không được để trống.")]
        [MaxLength(50, ErrorMessage = "Cách tính phí tối đa 50 ký tự.")]
        public string CalculationType { get; set; } = string.Empty; // e.g. "FIXED", "PERCENTAGE"

        [Range(0, double.MaxValue, ErrorMessage = "Giá trị phí phải lớn hơn hoặc bằng 0.")]
        public decimal Value { get; set; }

        [MaxLength(50, ErrorMessage = "Đơn vị tính tối đa 50 ký tự.")]
        public string? Unit { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(1000, ErrorMessage = "Ghi chú mô tả tối đa 1000 ký tự.")]
        public string? Description { get; set; }
    }

    public class UpdateAdditionalServiceFeeRequestDto
    {
        [Required(ErrorMessage = "Tên loại phí không được để trống.")]
        [MaxLength(150, ErrorMessage = "Tên loại phí tối đa 150 ký tự.")]
        public string FeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã loại phí không được để trống.")]
        [MaxLength(50, ErrorMessage = "Mã loại phí tối đa 50 ký tự.")]
        public string FeeCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Cách tính phí không được để trống.")]
        [MaxLength(50, ErrorMessage = "Cách tính phí tối đa 50 ký tự.")]
        public string CalculationType { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Giá trị phí phải lớn hơn hoặc bằng 0.")]
        public decimal Value { get; set; }

        [MaxLength(50, ErrorMessage = "Đơn vị tính tối đa 50 ký tự.")]
        public string? Unit { get; set; }

        public bool IsActive { get; set; }

        [MaxLength(1000, ErrorMessage = "Ghi chú mô tả tối đa 1000 ký tự.")]
        public string? Description { get; set; }
    }
}
