using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs;

public class WarehouseLocationLayoutDto
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? ZoneId { get; set; }
    public Guid? ShelfId { get; set; }
    public Guid? BinId { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public string DisplayLabel { get; set; } = string.Empty;
    public string LayoutType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? ColorCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Display helpers
    public string? ZoneName { get; set; }
    public string? ShelfCode { get; set; }
    public string? BinCode { get; set; }
}

public class CreateWarehouseLocationLayoutDto
{
    public Guid? ZoneId { get; set; }

    public Guid? ShelfId { get; set; }

    public Guid? BinId { get; set; }

    [Required(ErrorMessage = "Hàng (RowIndex) là bắt buộc.")]
    [Range(0, int.MaxValue, ErrorMessage = "Tọa độ hàng không được âm.")]
    public int RowIndex { get; set; }

    [Required(ErrorMessage = "Cột (ColumnIndex) là bắt buộc.")]
    [Range(0, int.MaxValue, ErrorMessage = "Tọa độ cột không được âm.")]
    public int ColumnIndex { get; set; }

    [Required(ErrorMessage = "Nhãn hiển thị (DisplayLabel) là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Nhãn hiển thị tối đa 100 ký tự.")]
    public string DisplayLabel { get; set; } = string.Empty;

    [Required(ErrorMessage = "Loại layout (LayoutType) là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Loại layout tối đa 50 ký tự.")]
    public string LayoutType { get; set; } = "BIN";

    [Required(ErrorMessage = "Trạng thái (Status) là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Trạng thái tối đa 50 ký tự.")]
    public string Status { get; set; } = "ACTIVE";

    public int? Width { get; set; }

    public int? Height { get; set; }

    [StringLength(50, ErrorMessage = "Mã màu tối đa 50 ký tự.")]
    public string? ColorCode { get; set; }
}

public class UpdateWarehouseLocationLayoutDto
{
    public Guid? ZoneId { get; set; }

    public Guid? ShelfId { get; set; }

    public Guid? BinId { get; set; }

    [Required(ErrorMessage = "Hàng (RowIndex) là bắt buộc.")]
    [Range(0, int.MaxValue, ErrorMessage = "Tọa độ hàng không được âm.")]
    public int RowIndex { get; set; }

    [Required(ErrorMessage = "Cột (ColumnIndex) là bắt buộc.")]
    [Range(0, int.MaxValue, ErrorMessage = "Tọa độ cột không được âm.")]
    public int ColumnIndex { get; set; }

    [Required(ErrorMessage = "Nhãn hiển thị (DisplayLabel) là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Nhãn hiển thị tối đa 100 ký tự.")]
    public string DisplayLabel { get; set; } = string.Empty;

    [Required(ErrorMessage = "Loại layout (LayoutType) là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Loại layout tối đa 50 ký tự.")]
    public string LayoutType { get; set; } = "BIN";

    [Required(ErrorMessage = "Trạng thái (Status) là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Trạng thái tối đa 50 ký tự.")]
    public string Status { get; set; } = "ACTIVE";

    public int? Width { get; set; }

    public int? Height { get; set; }

    [StringLength(50, ErrorMessage = "Mã màu tối đa 50 ký tự.")]
    public string? ColorCode { get; set; }
}
