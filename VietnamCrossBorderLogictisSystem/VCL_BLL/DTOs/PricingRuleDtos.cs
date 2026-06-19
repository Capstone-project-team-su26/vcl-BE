using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class PricingRuleDto
    {
        public Guid Id { get; set; }
        public string ShippingServiceType { get; set; } = null!;
        public string ConsignmentType { get; set; } = null!;
        public string? Route { get; set; }
        public string? UnitType { get; set; }
        public decimal PricePerWeight { get; set; }
        public decimal? PricePerVolume { get; set; }
        public decimal ServiceFee { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreatePricingRuleRequest
    {
        [Required(ErrorMessage = "Loại dịch vụ vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại dịch vụ vận chuyển không được quá 100 ký tự.")]
        public string ShippingServiceType { get; set; } = null!;

        [Required(ErrorMessage = "Loại ký gửi không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại ký gửi không được quá 100 ký tự.")]
        public string ConsignmentType { get; set; } = null!;

        [MaxLength(255, ErrorMessage = "Tuyến vận chuyển không được quá 255 ký tự.")]
        public string? Route { get; set; }

        [MaxLength(50, ErrorMessage = "Đơn vị tính phí không được quá 50 ký tự.")]
        public string? UnitType { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Giá theo cân nặng phải lớn hơn hoặc bằng 0.")]
        public decimal PricePerWeight { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Giá theo thể tích phải lớn hơn hoặc bằng 0.")]
        public decimal? PricePerVolume { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Phí dịch vụ phải lớn hơn hoặc bằng 0.")]
        public decimal ServiceFee { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class UpdatePricingRuleRequest
    {
        [Required(ErrorMessage = "Loại dịch vụ vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại dịch vụ vận chuyển không được quá 100 ký tự.")]
        public string ShippingServiceType { get; set; } = null!;

        [Required(ErrorMessage = "Loại ký gửi không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại ký gửi không được quá 100 ký tự.")]
        public string ConsignmentType { get; set; } = null!;

        [MaxLength(255, ErrorMessage = "Tuyến vận chuyển không được quá 255 ký tự.")]
        public string? Route { get; set; }

        [MaxLength(50, ErrorMessage = "Đơn vị tính phí không được quá 50 ký tự.")]
        public string? UnitType { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Giá theo cân nặng phải lớn hơn hoặc bằng 0.")]
        public decimal PricePerWeight { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Giá theo thể tích phải lớn hơn hoặc bằng 0.")]
        public decimal? PricePerVolume { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Phí dịch vụ phải lớn hơn hoặc bằng 0.")]
        public decimal ServiceFee { get; set; }

        public bool IsActive { get; set; }
    }
}
