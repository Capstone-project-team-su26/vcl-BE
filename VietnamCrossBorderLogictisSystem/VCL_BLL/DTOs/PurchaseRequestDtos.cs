using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CreatePurchaseRequestItemDto
    {
        [Required(ErrorMessage = "Link sản phẩm không được để trống.")]
        public string ProductLink { get; set; } = string.Empty;

        public string? ProductName { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        /// <summary>Thuộc tính sản phẩm như size, màu, mẫu mã (nếu có).</summary>
        public string? Attributes { get; set; }

        public string? Note { get; set; }

        /// <summary>Hình ảnh tham khảo (nếu có).</summary>
        public string? ImageUrl { get; set; }
    }

    public class CreatePurchaseRequestDto
    {
        /// <summary>Ghi chú mua hộ (nếu có).</summary>
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách sản phẩm cần mua hộ phải có ít nhất một sản phẩm.")]
        public List<CreatePurchaseRequestItemDto> Items { get; set; } = new();
    }

    public class PurchaseRequestItemResponseDto
    {
        public Guid ItemId { get; set; }
        public string ProductLink { get; set; } = string.Empty;
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
        public string? Attributes { get; set; }
        public string? Note { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class PurchaseRequestResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<PurchaseRequestItemResponseDto> Items { get; set; } = new();
    }

    public class PurchaseRequestListItemDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
