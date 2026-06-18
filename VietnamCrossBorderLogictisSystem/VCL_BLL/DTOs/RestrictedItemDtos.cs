using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class RestrictedItemDto
    {
        public Guid Id { get; set; }
        public string ItemName { get; set; } = null!;
        public string? Country { get; set; }
        public string RestrictionType { get; set; } = null!;
        public string? Note { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateRestrictedItemRequest
    {
        [Required(ErrorMessage = "Tên mặt hàng không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên mặt hàng không quá 255 ký tự.")]
        public string ItemName { get; set; } = null!;

        [MaxLength(100, ErrorMessage = "Tên quốc gia không quá 100 ký tự.")]
        public string? Country { get; set; }

        [Required(ErrorMessage = "Loại hạn chế không được để trống.")]
        [RegularExpression("^(BANNED|RESTRICTED|WARNING)$", ErrorMessage = "Loại hạn chế chỉ nhận: BANNED, RESTRICTED, WARNING.")]
        public string RestrictionType { get; set; } = null!;

        public string? Note { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class UpdateRestrictedItemRequest
    {
        [Required(ErrorMessage = "Tên mặt hàng không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên mặt hàng không quá 255 ký tự.")]
        public string ItemName { get; set; } = null!;

        [MaxLength(100, ErrorMessage = "Tên quốc gia không quá 100 ký tự.")]
        public string? Country { get; set; }

        [Required(ErrorMessage = "Loại hạn chế không được để trống.")]
        [RegularExpression("^(BANNED|RESTRICTED|WARNING)$", ErrorMessage = "Loại hạn chế chỉ nhận: BANNED, RESTRICTED, WARNING.")]
        public string RestrictionType { get; set; } = null!;

        public string? Note { get; set; }

        public bool IsActive { get; set; }
    }
}
