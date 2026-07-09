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
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalQuantity { get; set; }
        public List<PurchaseRequestItemResponseDto> Items { get; set; } = new();
    }

    public class UpdatePurchaseRequestStatusRequestDto
    {
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        /// <summary>Lý do (bắt buộc khi trạng thái là NEED_MORE_INFO hoặc REJECTED).</summary>
        public string? Reason { get; set; }
    }

    public class UpdatePurchaseRequestStatusResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public class CreatePurchaseQuotationItemDto
    {
        [Required]
        public Guid PurchaseRequestItemId { get; set; }

        /// <summary>Giá sản phẩm (đơn giá).</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Đơn giá sản phẩm không được âm.")]
        public decimal UnitPrice { get; set; }
    }

    public class CreatePurchaseQuotationDto
    {
        /// <summary>Phí mua hộ.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Phí mua hộ không được âm.")]
        public decimal PurchaseFee { get; set; }

        /// <summary>Phí vận chuyển dự kiến (nếu có).</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Phí vận chuyển không được âm.")]
        public decimal ShippingFee { get; set; }

        /// <summary>Ghi chú báo giá (nếu có).</summary>
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Báo giá phải có ít nhất một sản phẩm.")]
        public List<CreatePurchaseQuotationItemDto> Items { get; set; } = new();
    }

    public class PurchaseQuotationItemResponseDto
    {
        public Guid QuotationItemId { get; set; }
        public Guid PurchaseRequestItemId { get; set; }
        public string? ProductName { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class PurchaseQuotationResponseDto
    {
        public Guid QuotationId { get; set; }
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public decimal ProductsSubtotal { get; set; }
        public decimal PurchaseFee { get; set; }
        public decimal ShippingFee { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<PurchaseQuotationItemResponseDto> Items { get; set; } = new();
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

    public class CreatePurchaseOrderDto
    {
        /// <summary>Nhà cung cấp (nếu có).</summary>
        public string? SupplierName { get; set; }

        /// <summary>Ghi chú mua hàng (nếu có).</summary>
        public string? PurchaseNote { get; set; }
    }

    public class PurchaseOrderItemResponseDto
    {
        public Guid OrderItemId { get; set; }
        public Guid PurchaseRequestItemId { get; set; }
        public string? ProductName { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class PurchaseOrderResponseDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string PurchaseOrderCode { get; set; } = string.Empty;
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? PurchaseQuotationId { get; set; }
        public string? SupplierName { get; set; }
        public string? PurchaseNote { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? StatusNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<PurchaseOrderItemResponseDto> Items { get; set; } = new();
    }

    public class UpdatePurchaseOrderStatusRequestDto
    {
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        /// <summary>Ghi chú xử lý (nếu có).</summary>
        public string? StatusNote { get; set; }
    }

    public class UpdatePurchaseOrderStatusResponseDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string PurchaseOrderCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? StatusNote { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class ConfirmQuotationAndPayRequestDto
    {
        /// <summary>URL FE để payOS chuyển hướng khi thanh toán thành công (nếu bỏ trống dùng cấu hình mặc định).</summary>
        public string? ReturnUrl { get; set; }

        /// <summary>URL FE để payOS chuyển hướng khi khách hủy thanh toán (nếu bỏ trống dùng cấu hình mặc định).</summary>
        public string? CancelUrl { get; set; }
    }

    public class ConfirmQuotationAndPayResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public string RequestStatus { get; set; } = string.Empty;

        public Guid QuotationId { get; set; }
        public string QuotationStatus { get; set; } = string.Empty;

        public Guid PaymentId { get; set; }
        public long OrderCode { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary>Link thanh toán payOS để FE điều hướng khách hàng.</summary>
        public string CheckoutUrl { get; set; } = string.Empty;
        public string? PaymentLinkId { get; set; }
    }
}
