using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CreateWroRequestItemDto
    {
        [Required]
        public Guid InventoryId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng xuất kho phải lớn hơn 0.")]
        public int Quantity { get; set; }
    }

    public class CreateWroRequestDto
    {
        [Required(ErrorMessage = "Tên người nhận không được để trống.")]
        public string ReceiverName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số điện thoại người nhận không được để trống.")]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Địa chỉ giao hàng không được để trống.")]
        public string DeliveryAddress { get; set; } = string.Empty;

        public string? ShelfCode { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách xuất kho phải có ít nhất một mặt hàng.")]
        public List<CreateWroRequestItemDto> Items { get; set; } = new();
    }

    public class WroItemResponseDto
    {
        public Guid ItemId { get; set; }
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
        public string PackageCode { get; set; } = string.Empty;
    }

    public class WroResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ShelfCode { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<WroItemResponseDto> Items { get; set; } = new();
    }

    public class WroDetailItemDto
    {
        public Guid ItemId { get; set; }
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
    }

    public class WroDetailResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;
        public string CreatedByUserRole { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ShelfCode { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalQuantity { get; set; }
        public List<WroDetailItemDto> Items { get; set; } = new();
    }

    public class UpdateWroStatusRequestDto
    {
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        public string? RejectionReason { get; set; }
    }

    public class UpdateWroStatusResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
