using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CarrierDto
    {
        public Guid Id { get; set; }
        public string CarrierName { get; set; } = null!;
        public string CarrierCode { get; set; } = null!;
        public string CarrierType { get; set; } = null!;
        public string? ApiUrl { get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactPhone { get; set; }
        public string? SupportedShippingMethods { get; set; }
        public string? SupportedRegions { get; set; }
        public bool IsActive { get; set; }
        public string? InternalNotes { get; set; }
    }

    public class CreateCarrierRequestDto
    {
        [Required(ErrorMessage = "Tên đơn vị vận chuyển không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên đơn vị vận chuyển không quá 255 ký tự.")]
        public string CarrierName { get; set; } = null!;

        [Required(ErrorMessage = "Mã đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã đơn vị vận chuyển không quá 100 ký tự.")]
        [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "Mã đơn vị vận chuyển chỉ được chứa ký tự chữ, số, gạch dưới và gạch ngang.")]
        public string CarrierCode { get; set; } = null!;

        [Required(ErrorMessage = "Loại đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại đơn vị vận chuyển không quá 100 ký tự.")]
        public string CarrierType { get; set; } = null!;

        [Url(ErrorMessage = "Đường dẫn API không hợp lệ.")]
        public string? ApiUrl { get; set; }

        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
        [MaxLength(255, ErrorMessage = "Email liên hệ không quá 255 ký tự.")]
        public string? ContactEmail { get; set; }

        [MaxLength(50, ErrorMessage = "Số điện thoại liên hệ không quá 50 ký tự.")]
        public string? ContactPhone { get; set; }

        [MaxLength(500, ErrorMessage = "Phương thức vận chuyển hỗ trợ không quá 500 ký tự.")]
        public string? SupportedShippingMethods { get; set; }

        [MaxLength(500, ErrorMessage = "Quốc gia/khu vực hỗ trợ không quá 500 ký tự.")]
        public string? SupportedRegions { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(1000, ErrorMessage = "Ghi chú nội bộ không quá 1000 ký tự.")]
        public string? InternalNotes { get; set; }
    }

    public class UpdateCarrierRequestDto
    {
        [Required(ErrorMessage = "Tên đơn vị vận chuyển không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên đơn vị vận chuyển không quá 255 ký tự.")]
        public string CarrierName { get; set; } = null!;

        [Required(ErrorMessage = "Mã đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã đơn vị vận chuyển không quá 100 ký tự.")]
        [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "Mã đơn vị vận chuyển chỉ được chứa ký tự chữ, số, gạch dưới và gạch ngang.")]
        public string CarrierCode { get; set; } = null!;

        [Required(ErrorMessage = "Loại đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại đơn vị vận chuyển không quá 100 ký tự.")]
        public string CarrierType { get; set; } = null!;

        [Url(ErrorMessage = "Đường dẫn API không hợp lệ.")]
        public string? ApiUrl { get; set; }

        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
        [MaxLength(255, ErrorMessage = "Email liên hệ không quá 255 ký tự.")]
        public string? ContactEmail { get; set; }

        [MaxLength(50, ErrorMessage = "Số điện thoại liên hệ không quá 50 ký tự.")]
        public string? ContactPhone { get; set; }

        [MaxLength(500, ErrorMessage = "Phương thức vận chuyển hỗ trợ không quá 500 ký tự.")]
        public string? SupportedShippingMethods { get; set; }

        [MaxLength(500, ErrorMessage = "Quốc gia/khu vực hỗ trợ không quá 500 ký tự.")]
        public string? SupportedRegions { get; set; }

        public bool IsActive { get; set; }

        [MaxLength(1000, ErrorMessage = "Ghi chú nội bộ không quá 1000 ký tự.")]
        public string? InternalNotes { get; set; }
    }
}
