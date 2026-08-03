using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>Đơn vị tính rút gọn, dùng để đổ danh sách chọn cho khách hàng</summary>
    public class UnitOfMeasureResponse
    {
        public Guid Id { get; set; }
        public string UnitCode { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
    }

    /// <summary>Đơn vị tính đầy đủ, dùng cho màn quản trị danh mục</summary>
    public class UnitOfMeasureDetailResponse : UnitOfMeasureResponse
    {
        public string Description { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class CreateUnitOfMeasureRequest
    {
        [Required(ErrorMessage = "Mã đơn vị tính không được để trống.")]
        [MaxLength(30, ErrorMessage = "Mã đơn vị tính không quá 30 ký tự.")]
        public string UnitCode { get; set; } = null!;

        [Required(ErrorMessage = "Tên đơn vị tính không được để trống.")]
        [MaxLength(100, ErrorMessage = "Tên đơn vị tính không quá 100 ký tự.")]
        public string UnitName { get; set; } = null!;

        [MaxLength(255, ErrorMessage = "Mô tả không quá 255 ký tự.")]
        public string Description { get; set; } = string.Empty;

        [Range(0, 999, ErrorMessage = "Thứ tự hiển thị phải nằm trong khoảng 0 đến 999.")]
        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class UpdateUnitOfMeasureRequest : CreateUnitOfMeasureRequest
    {
    }
}
