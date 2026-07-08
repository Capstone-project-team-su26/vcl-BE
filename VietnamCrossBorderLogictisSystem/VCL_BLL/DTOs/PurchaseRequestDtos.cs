using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CreatePurchaseRequestItemDto
    {
        [Required(ErrorMessage = "Link sản phẩm không được để trống.")]
        public string ProductLink { get; set; } = string.Empty;

        /// <summary>Website/sàn nguồn sản phẩm (VD: Taobao, 1688, Tmall).</summary>
        public string? SourceWebsite { get; set; }

        /// <summary>Loại hàng (lấy từ dropdown /api/product-types).</summary>
        public string? ProductType { get; set; }

        public string? ProductName { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        /// <summary>Thuộc tính sản phẩm như size, màu, mẫu mã (nếu có).</summary>
        public string? Attributes { get; set; }

        /// <summary>Ghi chú riêng cho từng sản phẩm (nếu có).</summary>
        public string? Note { get; set; }

        /// <summary>Hình ảnh tham khảo (nếu có).</summary>
        public string? ImageUrl { get; set; }
    }

    public class CreatePurchaseRequestDto
    {
        /// <summary>Tuyến hàng (lấy từ dropdown /api/orders/consignments/routes).</summary>
        public string? Route { get; set; }

        /// <summary>Tên người nhận.</summary>
        public string? ReceiverName { get; set; }

        /// <summary>Số điện thoại người nhận.</summary>
        public string? ReceiverPhone { get; set; }

        /// <summary>Địa chỉ nhận hàng.</summary>
        public string? ReceiverAddress { get; set; }

        /// <summary>Yêu cầu kiểm tra hàng.</summary>
        public bool RequiresInspection { get; set; }

        /// <summary>Yêu cầu kiểm tra số lượng.</summary>
        public bool RequiresQuantityCheck { get; set; }

        /// <summary>Ghi chú mua hộ chung cho cả yêu cầu (nếu có).</summary>
        public string? GeneralNote { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Danh sách sản phẩm cần mua hộ phải có ít nhất một sản phẩm.")]
        public List<CreatePurchaseRequestItemDto> Items { get; set; } = new();
    }

    public class PurchaseRequestItemResponseDto
    {
        public Guid ItemId { get; set; }
        public string ProductLink { get; set; } = string.Empty;
        public string? SourceWebsite { get; set; }
        public string? ProductType { get; set; }
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
        public string? Route { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresInspection { get; set; }
        public bool RequiresQuantityCheck { get; set; }
        public string? GeneralNote { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<PurchaseRequestItemResponseDto> Items { get; set; } = new();
    }

    public class PurchaseRequestDetailResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;
        public string? Route { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresInspection { get; set; }
        public bool RequiresQuantityCheck { get; set; }
        public string? GeneralNote { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalQuantity { get; set; }
        public List<PurchaseRequestItemResponseDto> Items { get; set; } = new();
    }

    public class PurchaseRequestListProductDto
    {
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
    }

    public class PurchaseRequestListItemDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string ReceiverName { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? GeneralNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<PurchaseRequestListProductDto> Items { get; set; } = new();
    }
}
