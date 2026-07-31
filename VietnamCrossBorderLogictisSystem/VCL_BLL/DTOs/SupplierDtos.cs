using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class SupplierDto
    {
        public Guid Id { get; set; }
        public string SupplierCode { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public string SupplierType { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class CreateSupplierRequest
    {
        [Required(ErrorMessage = "Mã nhà cung cấp không được để trống.")]
        [MaxLength(50, ErrorMessage = "Mã nhà cung cấp không quá 50 ký tự.")]
        public string SupplierCode { get; set; } = null!;

        [Required(ErrorMessage = "Tên nhà cung cấp không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên nhà cung cấp không quá 255 ký tự.")]
        public string SupplierName { get; set; } = null!;

        [Required(ErrorMessage = "Loại nhà cung cấp không được để trống.")]
        [RegularExpression("^(TRANSIT|PICKUP|GOODS|SERVICE)$", ErrorMessage = "Loại nhà cung cấp chỉ nhận: TRANSIT, PICKUP, GOODS, SERVICE.")]
        public string SupplierType { get; set; } = null!;

        [MaxLength(100, ErrorMessage = "Tên quốc gia không quá 100 ký tự.")]
        public string Country { get; set; } = string.Empty;

        [MaxLength(255, ErrorMessage = "Tên người liên hệ không quá 255 ký tự.")]
        public string ContactPerson { get; set; } = string.Empty;

        [MaxLength(30, ErrorMessage = "Số điện thoại không quá 30 ký tự.")]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [MaxLength(255, ErrorMessage = "Email không quá 255 ký tự.")]
        public string Email { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Địa chỉ không quá 500 ký tự.")]
        public string Address { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Ghi chú không quá 500 ký tự.")]
        public string Note { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    public class UpdateSupplierRequest : CreateSupplierRequest
    {
    }
}
