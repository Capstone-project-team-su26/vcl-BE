using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CreatePurchaseRequestItemDto
    {
        [Required(ErrorMessage = "Link sản phẩm không được để trống.")]
        public string ProductLink { get; set; } = string.Empty;

        /// <summary>Website/sàn nguồn sản phẩm — tùy tuyến (VD: Taobao, 1688, Amazon, Coupang, Rakuten).</summary>
        public string? SourceWebsite { get; set; }

        /// <summary>Loại hàng (lấy từ dropdown /api/product-types) — dùng để đối chiếu hàng cấm/hạn chế.</summary>
        [Required(ErrorMessage = "Vui lòng chọn loại hàng.")]
        public string ProductType { get; set; } = string.Empty;

        public string? ProductName { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        /// <summary>Thuộc tính sản phẩm như size, màu, mẫu mã (nếu có).</summary>
        public string? Attributes { get; set; }

        /// <summary>Ghi chú riêng cho từng sản phẩm (nếu có).</summary>
        public string? Note { get; set; }

        /// <summary>Danh sách hình ảnh tham khảo (upload qua POST /api/uploads/images).</summary>
        public List<string>? ImageUrls { get; set; }
    }

    public class CreatePurchaseRequestDto
    {
        /// <summary>
        /// Tuyến hàng (lấy từ dropdown GET /api/orders/consignments/routes) — không giới hạn ở tuyến Trung Quốc,
        /// mà theo đúng các tuyến quốc tế hệ thống đang khai thác trong bảng cấu hình giá.
        /// Nhận cả mã tuyến ("CN-VN", "KR-VN"...) lẫn tên hiển thị của dropdown.
        /// </summary>
        [Required(ErrorMessage = "Vui lòng chọn tuyến hàng.")]
        public string Route { get; set; } = string.Empty;

        /// <summary>
        /// Phương án vận chuyển (VD: EXPRESS, STANDARD). Phải khớp một cấu hình giá active theo Route đã chọn —
        /// FE lấy danh sách từ GET /api/orders/consignments/shipping-options?route=...
        /// </summary>
        [Required(ErrorMessage = "Vui lòng chọn phương án vận chuyển.")]
        public string ShippingOption { get; set; } = string.Empty;

        /// <summary>Tên người nhận.</summary>
        public string? ReceiverName { get; set; }

        /// <summary>Số điện thoại người nhận.</summary>
        public string? ReceiverPhone { get; set; }

        /// <summary>Địa chỉ nhận hàng.</summary>
        public string? ReceiverAddress { get; set; }

        /// <summary>
        /// Danh sách ID cấu hình bảng giá/dịch vụ phụ khách chọn (đóng gỗ, bảo hiểm...).
        /// Hợp nhất với các cờ boolean bên dưới — FE dùng cách nào cũng được.
        /// </summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        /// <summary>Khách yêu cầu đóng gói lại (map rule PACKING).</summary>
        public bool RequiresPacking { get; set; }

        /// <summary>Khách yêu cầu đóng thùng gỗ (map rule WOODEN_BOX).</summary>
        public bool RequiresWoodenCrate { get; set; }

        /// <summary>Khách đăng ký bảo hiểm (map rule INSURANCE).</summary>
        public bool RequiresInsurance { get; set; }

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
        public List<string> ImageUrls { get; set; } = new();
    }

    /// <summary>
    /// Sale tạo yêu cầu mua hộ thay cho khách hàng. Giữ nguyên toàn bộ trường của
    /// <see cref="CreatePurchaseRequestDto"/>, chỉ thêm phần chỉ định khách hàng.
    /// </summary>
    public class CreatePurchaseRequestByStaffDto : CreatePurchaseRequestDto
    {
        /// <summary>ID khách hàng (nếu chọn từ danh sách có sẵn).</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>Số điện thoại khách hàng (để tìm kiếm khách hàng).</summary>
        public string? CustomerPhone { get; set; }

        /// <summary>Email khách hàng (để tìm kiếm khách hàng).</summary>
        public string? CustomerEmail { get; set; }
    }

    public class PurchaseRequestResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }

        /// <summary>ID user (tài khoản) của khách — FE cần để xử lý.</summary>
        public Guid? UserId { get; set; }

        public string? Route { get; set; }
        public string? ShippingOption { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresPacking { get; set; }
        public bool RequiresWoodenCrate { get; set; }
        public bool RequiresInsurance { get; set; }
        public string? GeneralNote { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        /// <summary>Số dòng sản phẩm trong yêu cầu.</summary>
        public int ItemCount { get; set; }

        /// <summary>Tổng số lượng của tất cả sản phẩm.</summary>
        public int TotalQuantity { get; set; }

        /// <summary>ID các quy định giá/dịch vụ phụ khớp với các cờ khách đã tick — Sales dùng khi lên báo giá.</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        public List<PurchaseRequestItemResponseDto> Items { get; set; } = new();

        /// <summary>Cảnh báo hàng hạn chế (Restricted). Hàng cấm (Banned) sẽ bị chặn tạo.</summary>
        public List<RestrictedItemWarning> Warnings { get; set; } = new();
    }

    public class PurchaseRequestDetailResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;
        public string? Route { get; set; }
        public string? ShippingOption { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresPacking { get; set; }
        public bool RequiresWoodenCrate { get; set; }
        public bool RequiresInsurance { get; set; }

        /// <summary>ID các quy định giá/dịch vụ phụ khách đã chọn — Sales dùng khi lên báo giá.</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        public string? GeneralNote { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Thời điểm lập báo giá mới nhất (null nếu chưa có báo giá).</summary>
        public DateTime? QuotationCreatedAt { get; set; }

        /// <summary>Mốc thời gian tương ứng với trạng thái hiện tại — FE dùng để hiển thị "cập nhật lúc…".</summary>
        public DateTime? StatusUpdatedAt { get; set; }

        public int TotalQuantity { get; set; }
        public List<PurchaseRequestItemResponseDto> Items { get; set; } = new();
        public PurchaseQuotationResponseDto? Quotation { get; set; }
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

    public class PurchaseQuotationAdditionalFeeDto
    {
        public Guid? PricingRuleId { get; set; }
        public string FeeName { get; set; } = string.Empty;
        public string FeeType { get; set; } = "SURCHARGE";
        public string CalculationType { get; set; } = "FIXED";
        public decimal Value { get; set; }
        public decimal Amount { get; set; }
        public string? Note { get; set; }
    }

    public class PurchaseQuotationAdditionalFeeResponseDto
    {
        public Guid Id { get; set; }
        public Guid? PricingRuleId { get; set; }
        public string FeeName { get; set; } = string.Empty;
        public string FeeType { get; set; } = string.Empty;
        public string CalculationType { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public decimal Amount { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
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

        public List<PurchaseQuotationAdditionalFeeDto> AdditionalFees { get; set; } = new();
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
        public decimal Vat { get; set; }
        public decimal ImportTax { get; set; }
        public decimal TotalAmount { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<PurchaseQuotationItemResponseDto> Items { get; set; } = new();
        public List<PurchaseQuotationAdditionalFeeResponseDto> AdditionalFees { get; set; } = new();
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
        public string? Route { get; set; }
        public string? ShippingOption { get; set; }
        public string ReceiverName { get; set; } = string.Empty;

        /// <summary>Số dòng sản phẩm trong yêu cầu.</summary>
        public int ItemCount { get; set; }

        /// <summary>Tổng số lượng của tất cả sản phẩm.</summary>
        public int TotalQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? GeneralNote { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Thời điểm lập báo giá mới nhất (null nếu chưa có báo giá).</summary>
        public DateTime? QuotationCreatedAt { get; set; }

        /// <summary>Mốc thời gian tương ứng với trạng thái hiện tại — FE dùng để hiển thị "cập nhật lúc…".</summary>
        public DateTime? StatusUpdatedAt { get; set; }

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

        /// <summary>Phương thức thanh toán: PAYOS hoặc SEPAY. Nếu trống, mặc định dùng PAYOS hoặc theo cấu hình hệ thống.</summary>
        public string? PaymentMethod { get; set; }
    }

    /// <summary>
    /// Staff tạo đợt thanh toán cuối cho đơn mua hộ khi phát sinh chi phí thêm
    /// (lưu kho, giao lại, đóng gói thêm...) sau khi hàng đã về.
    /// </summary>
    public class CreatePurchaseFinalPaymentRequestDto
    {
        /// <summary>Danh sách chi phí phát sinh. Để trống nghĩa là không phát sinh gì -> đơn hoàn tất luôn.</summary>
        public List<ExtraFeeLineDto> ExtraFees { get; set; } = new();

        public string? ReturnUrl { get; set; }
        public string? CancelUrl { get; set; }

        /// <summary>Phương thức thanh toán: PAYOS hoặc SEPAY. Bỏ trống thì mặc định PAYOS.</summary>
        public string? PaymentMethod { get; set; }
    }

    public class CreatePurchaseFinalPaymentResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public string RequestStatus { get; set; } = string.Empty;

        public Guid QuotationId { get; set; }

        /// <summary>Tổng bill sau khi cộng chi phí phát sinh.</summary>
        public decimal TotalBillAmount { get; set; }

        /// <summary>Số tiền khách đã thanh toán trước đó.</summary>
        public decimal AlreadyPaid { get; set; }

        /// <summary>Tổng chi phí phát sinh lần này.</summary>
        public decimal ExtraFeesTotal { get; set; }

        /// <summary>Số tiền cần thanh toán nốt. Bằng 0 nghĩa là không phát sinh, đơn hoàn tất luôn.</summary>
        public decimal FinalAmount { get; set; }

        public Guid PaymentId { get; set; }
        public long? OrderCode { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string? PaymentMethod { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? PaymentLinkId { get; set; }
    }

    /// <summary>Một khoản thanh toán trong lịch sử thanh toán của đơn mua hộ.</summary>
    public class PurchasePaymentHistoryItemDto
    {
        public Guid PaymentId { get; set; }
        public long OrderCode { get; set; }

        /// <summary>FULL_PAYMENT | FINAL_PAYMENT.</summary>
        public string PaymentType { get; set; } = string.Empty;

        public decimal Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? CheckoutUrl { get; set; }
        public string? TransactionCode { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    /// <summary>Tổng hợp tình hình thanh toán của một đơn mua hộ.</summary>
    public class PurchasePaymentHistoryResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public string RequestStatus { get; set; } = string.Empty;

        /// <summary>Tổng bill hiện tại (đã gồm chi phí phát sinh nếu có).</summary>
        public decimal TotalBillAmount { get; set; }

        public decimal TotalPaid { get; set; }

        /// <summary>Số tiền còn thiếu. Bằng 0 là đã thanh toán đủ.</summary>
        public decimal Outstanding { get; set; }

        public List<PurchasePaymentHistoryItemDto> Payments { get; set; } = new();
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

    public class CalculatePurchaseItemFeeItemDto
    {
        public Guid PurchaseRequestItemId { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
    }

    public class CalculatePurchaseQuotationFeesRequestDto
    {
        public List<CalculatePurchaseItemFeeItemDto> Items { get; set; } = new();
    }

    public class CalculatePurchaseQuotationFeesResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public decimal ProductsSubtotal { get; set; }
        public decimal CalculatedPurchaseFee { get; set; }
        public decimal AppliedPurchaseFeePercent { get; set; }
        public decimal EstimatedShippingFee { get; set; }
        public decimal DomesticShippingFee { get; set; }
        public decimal TotalEstimatedCost { get; set; }
        public string? Note { get; set; }
    }

}

