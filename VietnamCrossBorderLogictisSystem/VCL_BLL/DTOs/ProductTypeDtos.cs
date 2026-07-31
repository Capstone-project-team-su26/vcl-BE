using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class ProductTypeResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Chi tiết loại hàng dùng cho màn quản trị danh mục</summary>
    public class ProductTypeDetailResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>Thuế suất nhập khẩu áp dụng cho loại hàng, đơn vị phần trăm</summary>
        public decimal? ImportTaxRate { get; set; }

        public bool IsActive { get; set; }

        public DateTime? CreatedAt { get; set; }
    }

    public class CreateProductTypeRequest
    {
        [Required(ErrorMessage = "Tên loại hàng không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên loại hàng không quá 255 ký tự.")]
        public string Name { get; set; } = null!;

        [Range(0, 100, ErrorMessage = "Thuế suất nhập khẩu phải nằm trong khoảng 0 đến 100.")]
        public decimal? ImportTaxRate { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class UpdateProductTypeRequest
    {
        [Required(ErrorMessage = "Tên loại hàng không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên loại hàng không quá 255 ký tự.")]
        public string Name { get; set; } = null!;

        [Range(0, 100, ErrorMessage = "Thuế suất nhập khẩu phải nằm trong khoảng 0 đến 100.")]
        public decimal? ImportTaxRate { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
