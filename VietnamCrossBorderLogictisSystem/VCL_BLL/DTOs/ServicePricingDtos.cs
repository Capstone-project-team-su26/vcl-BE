using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class ServicePricingDto
    {
        public Guid Id { get; set; }
        public Guid? CarrierId { get; set; }
        public string ServiceType { get; set; } = null!;
        public string OriginCountry { get; set; } = null!;
        public string DestinationCountry { get; set; } = null!;
        public string UnitType { get; set; } = null!;
        public decimal? Price { get; set; }
        public string Currency { get; set; } = null!;
        public DateTime? EffectiveDate { get; set; }

        /// <summary>Danh sách các quy tắc bảng giá/phụ phí liên quan đến đóng thùng/đóng gói.</summary>
        public List<PricingRuleDto> BoxPricingRules { get; set; } = new();
    }

    public class CreateServicePricingRequest
    {
        public Guid? CarrierId { get; set; }

        [Required(ErrorMessage = "Loại dịch vụ không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại dịch vụ không được quá 100 ký tự.")]
        public string ServiceType { get; set; } = null!;

        [Required(ErrorMessage = "Quốc gia đi không được để trống.")]
        [MaxLength(100, ErrorMessage = "Quốc gia đi không được quá 100 ký tự.")]
        public string OriginCountry { get; set; } = null!;

        [Required(ErrorMessage = "Quốc gia đến không được để trống.")]
        [MaxLength(100, ErrorMessage = "Quốc gia đến không được quá 100 ký tự.")]
        public string DestinationCountry { get; set; } = null!;

        [Required(ErrorMessage = "Đơn vị tính không được để trống.")]
        [MaxLength(50, ErrorMessage = "Đơn vị tính không được quá 50 ký tự.")]
        public string UnitType { get; set; } = null!;

        [Range(0, double.MaxValue, ErrorMessage = "Giá dịch vụ phải lớn hơn hoặc bằng 0.")]
        public decimal? Price { get; set; }

        [Required(ErrorMessage = "Đơn vị tiền tệ không được để trống.")]
        [MaxLength(10, ErrorMessage = "Đơn vị tiền tệ không được quá 10 ký tự.")]
        public string Currency { get; set; } = "VND";

        public DateTime? EffectiveDate { get; set; }
    }

    public class UpdateServicePricingRequest
    {
        public Guid? CarrierId { get; set; }

        [Required(ErrorMessage = "Loại dịch vụ không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại dịch vụ không được quá 100 ký tự.")]
        public string ServiceType { get; set; } = null!;

        [Required(ErrorMessage = "Quốc gia đi không được để trống.")]
        [MaxLength(100, ErrorMessage = "Quốc gia đi không được quá 100 ký tự.")]
        public string OriginCountry { get; set; } = null!;

        [Required(ErrorMessage = "Quốc gia đến không được để trống.")]
        [MaxLength(100, ErrorMessage = "Quốc gia đến không được quá 100 ký tự.")]
        public string DestinationCountry { get; set; } = null!;

        [Required(ErrorMessage = "Đơn vị tính không được để trống.")]
        [MaxLength(50, ErrorMessage = "Đơn vị tính không được quá 50 ký tự.")]
        public string UnitType { get; set; } = null!;

        [Range(0, double.MaxValue, ErrorMessage = "Giá dịch vụ phải lớn hơn hoặc bằng 0.")]
        public decimal? Price { get; set; }

        [Required(ErrorMessage = "Đơn vị tiền tệ không được để trống.")]
        [MaxLength(10, ErrorMessage = "Đơn vị tiền tệ không được quá 10 ký tự.")]
        public string Currency { get; set; } = null!;

        public DateTime? EffectiveDate { get; set; }
    }
}
