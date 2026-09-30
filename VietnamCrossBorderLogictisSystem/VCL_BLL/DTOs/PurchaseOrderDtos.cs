using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class PurchaseOrderItemInputDto
    {
        [Required]
        public Guid PurchaseRequestItemId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng mua phải lớn hơn 0.")]
        public int Quantity { get; set; }

        /// <summary>Đơn giá mua thực tế theo <see cref="SavePurchaseOrderDto.Currency"/>.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Đơn giá không được âm.")]
        public decimal UnitPrice { get; set; }
    }

    /// <summary>Sale lập / sửa đơn mua NCC (chỉ khi đơn ở DRAFT hoặc REJECTED).</summary>
    public class SavePurchaseOrderDto
    {
        [Required(ErrorMessage = "Phải chọn nhà cung cấp trong danh mục.")]
        public Guid? SupplierId { get; set; }

        /// <summary>Kho nguồn nhận hàng. Bỏ trống thì lấy kho dự kiến của yêu cầu.</summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>Đồng tiền giá mua (CNY, USD...). Bỏ trống = VND.</summary>
        [StringLength(10)]
        public string? Currency { get; set; }

        public string? PurchaseNote { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Đơn mua phải có ít nhất một sản phẩm.")]
        public List<PurchaseOrderItemInputDto> Items { get; set; } = new();
    }

    public class PurchaseOrderCustomerDecisionDto
    {
        public bool Accept { get; set; }

        /// <summary>Bắt buộc khi từ chối.</summary>
        public string? Reason { get; set; }

        /// <summary>PAYOS hoặc SEPAY.</summary>
        public string? PaymentMethod { get; set; }

        /// <summary>URL web quay về khi trả xong (chỉ nhận origin cho phép — xem PaymentReturnUrls).</summary>
        public string? ReturnUrl { get; set; }

        /// <summary>URL web quay về khi khách huỷ thanh toán; bỏ trống thì dùng ReturnUrl.</summary>
        public string? CancelUrl { get; set; }
    }

    public class PurchaseOrderDecisionDto
    {
        public bool Approve { get; set; }

        /// <summary>Bắt buộc khi từ chối.</summary>
        public string? Note { get; set; }
    }

    /// <summary>Sale đặt NCC. Phải tải ảnh bằng chứng (PURCHASE_PROOF) lên /api/attachments trước.</summary>
    public class PlacePurchaseOrderDto
    {
        [Required(ErrorMessage = "Phải nhập mã đơn bên NCC.")]
        [StringLength(100)]
        public string? SupplierOrderCode { get; set; }

        public string? Note { get; set; }
    }

    public class PurchaseOrderProgressDto
    {
        /// <summary>SUPPLIER_CONFIRMED hoặc SUPPLIER_SHIPPED.</summary>
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string? Status { get; set; }

        /// <summary>Bắt buộc khi NCC phát hàng.</summary>
        [StringLength(100)]
        public string? DomesticTrackingCode { get; set; }

        [StringLength(100)]
        public string? DomesticCarrier { get; set; }

        public string? Note { get; set; }
    }

    public class CancelPurchaseOrderDto
    {
        public string? Reason { get; set; }

        /// <summary>
        /// Ai gây ra việc huỷ đơn ĐÃ đặt NCC:
        /// <c>CUSTOMER</c> (mặc định) — khách tự huỷ: hoàn tiền hàng khách thực trả − phí huỷ, giữ phí mua hộ + VAT phí;
        /// <c>SUPPLIER</c> — NCC hết hàng / huỷ đơn: hoàn đủ tiền hàng + phí mua hộ + VAT phí tương ứng, không phí huỷ.
        /// </summary>
        public string? Cause { get; set; }
    }

    /// <summary>Kế toán xác nhận đã chuyển trả khách một khoản hoàn.</summary>
    public class CompletePurchaseRefundDto
    {
        /// <summary>Mã giao dịch chuyển khoản — bắt buộc, vì đây là bằng chứng tiền đã ra.</summary>
        public string? TransactionCode { get; set; }

        /// <summary>Khoản hoàn cần đóng. Bắt buộc khi đơn / yêu cầu có nhiều khoản đang chờ.</summary>
        public Guid? RefundId { get; set; }

        /// <summary>Số tiền đã chuyển — nếu gửi thì phải khớp đúng số tiền của khoản hoàn.</summary>
        public decimal? Amount { get; set; }
    }

    /// <summary>Một dòng NCC giao thiếu trong đơn mua đã đặt.</summary>
    public class SupplierShortageInputDto
    {
        [Required]
        public Guid PurchaseOrderId { get; set; }

        [Required]
        public Guid PurchaseRequestItemId { get; set; }

        /// <summary>Số lượng NCC không giao (không bao giờ tới kho).</summary>
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng thiếu phải lớn hơn 0.")]
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Sale / Admin đóng phần KHÔNG mua được của yêu cầu mua hộ (NCC hết hàng, mua ít hơn khách đặt)
    /// và phần NCC giao thiếu — hệ thống tính và ghi khoản hoàn theo từng sản phẩm.
    /// </summary>
    public class CloseUnfulfilledDto
    {
        /// <summary>Lý do — bắt buộc.</summary>
        public string? Reason { get; set; }

        /// <summary>Chỉ đóng các sản phẩm này. Bỏ trống = mọi sản phẩm còn phần chưa vào đơn mua nào.</summary>
        public List<Guid>? PurchaseRequestItemIds { get; set; }

        /// <summary>Phần NCC giao thiếu trong các đơn mua đã đặt (không bắt buộc).</summary>
        public List<SupplierShortageInputDto>? SupplierShortages { get; set; }
    }

    /// <summary>Chi tiết một dòng của khoản hoàn — đủ để hiện công thức cho khách / kế toán.</summary>
    public class PurchaseRefundLineDto
    {
        public Guid LineId { get; set; }
        public Guid? PurchaseOrderId { get; set; }
        public string? PurchaseOrderCode { get; set; }
        public Guid? PurchaseRequestItemId { get; set; }
        public string? ProductName { get; set; }

        /// <summary>PRICE_DIFF · UNFULFILLED · SUPPLIER_SHORT · CANCEL_CUSTOMER · CANCEL_SUPPLIER · PRICE_DIFF_RETURN.</summary>
        public string ReasonCode { get; set; } = string.Empty;
        public string ReasonText { get; set; } = string.Empty;
        public int Quantity { get; set; }

        /// <summary>Đơn giá tiền hàng khách đã trả cho một đơn vị.</summary>
        public decimal UnitPrice { get; set; }
        public decimal GoodsAmount { get; set; }
        public decimal PriceDifferenceAmount { get; set; }
        public decimal ServiceFeeAmount { get; set; }
        public decimal VatAmount { get; set; }

        /// <summary>Thuế NK không thu của phần hàng này (âm trên hoá đơn đơn kho).</summary>
        public decimal ImportTaxAdjustment { get; set; }

        /// <summary>True: thuế NK đã thu nên trả lại trong khoản hoàn; false: trừ trên hoá đơn đơn kho.</summary>
        public bool ImportTaxInRefund { get; set; }
        public decimal CancelFeeAmount { get; set; }
        public decimal Amount { get; set; }
        public string? Formula { get; set; }
        public string? Note { get; set; }
    }

    /// <summary>Một khoản hoàn tiền mua hộ (dòng PURCHASE_PAYMENTS loại REFUND_*) kèm chi tiết.</summary>
    public class PurchaseRefundDto
    {
        public Guid RefundId { get; set; }
        public Guid PurchaseRequestId { get; set; }
        public Guid? PurchaseOrderId { get; set; }
        public string? PurchaseOrderCode { get; set; }

        /// <summary>REFUND_PRICE_DIFF · REFUND_CANCEL · REFUND_UNFULFILLED.</summary>
        public string RefundType { get; set; } = string.Empty;
        public string RefundTypeText { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        /// <summary>PENDING · REFUNDED · CANCELLED.</summary>
        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string? TransactionCode { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? RefundedAt { get; set; }

        /* Tổng theo thành phần — cộng từ các dòng. */
        public decimal GoodsAmount { get; set; }
        public decimal PriceDifferenceAmount { get; set; }
        public decimal ServiceFeeAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal ImportTaxAdjustment { get; set; }
        public decimal CancelFeeAmount { get; set; }

        /// <summary>Khoản hoàn tạo trước khi có bảng chi tiết — chỉ có tổng tiền.</summary>
        public bool IsLegacy { get; set; }
        public List<PurchaseRefundLineDto> Lines { get; set; } = new();
    }

    /// <summary>Toàn bộ tiền hoàn của một yêu cầu mua hộ.</summary>
    public class PurchaseRefundSummaryDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;

        /// <summary>Tổng khách đã trả cho yêu cầu (trả trước + chênh giá), không gồm hoá đơn chặng VN.</summary>
        public decimal TotalCollected { get; set; }

        /// <summary>Tổng đã chuyển trả khách.</summary>
        public decimal TotalRefunded { get; set; }

        /// <summary>Tổng đang chờ chuyển trả.</summary>
        public decimal TotalPendingRefund { get; set; }

        /// <summary>Còn có thể hoàn tối đa = đã thu − đã hoàn − đang chờ hoàn.</summary>
        public decimal RefundableRemaining { get; set; }
        public List<PurchaseRefundDto> Refunds { get; set; } = new();
    }

    public class CloseUnfulfilledResponseDto
    {
        public PurchaseRefundDto Refund { get; set; } = new();
        public PurchaseRefundSummaryDto Summary { get; set; } = new();
        public string RequestStatus { get; set; } = string.Empty;
    }

    public class PurchaseOrderItemResponseDto
    {
        public Guid OrderItemId { get; set; }
        public Guid PurchaseRequestItemId { get; set; }
        public string? ProductName { get; set; }

        /// <summary>Số lượng khách đặt ở yêu cầu.</summary>
        public int RequestedQuantity { get; set; }

        public int Quantity { get; set; }

        /// <summary>Đơn giá mua VND (đã quy đổi).</summary>
        public decimal UnitPrice { get; set; }

        /// <summary>Đơn giá mua theo nguyên tệ (null nếu VND). Ẩn với khách.</summary>
        public decimal? UnitPriceOriginal { get; set; }

        /// <summary>Đơn giá VND đã báo khách.</summary>
        public decimal? QuotedUnitPrice { get; set; }

        public decimal LineTotal { get; set; }
    }

    public class PurchaseOrderResponseDto
    {
        public Guid PurchaseOrderId { get; set; }
        public string PurchaseOrderCode { get; set; } = string.Empty;
        public int? SequenceNo { get; set; }
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public Guid? PurchaseQuotationId { get; set; }
        public string? CustomerName { get; set; }

        public Guid? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }

        public string? Currency { get; set; }
        public decimal? ExchangeRate { get; set; }
        public string? PurchaseNote { get; set; }

        /// <summary>Tổng tiền hàng mua thực tế (VND).</summary>
        public decimal TotalAmount { get; set; }

        /// <summary>Tiền hàng theo giá đã báo khách cho đúng các dòng này.</summary>
        public decimal QuotedGoodsAmount { get; set; }

        /// <summary>Khách phải trả thêm (giá thực vượt giá báo quá ngưỡng). 0 = không có.</summary>
        public decimal PriceDifferenceAmount { get; set; }

        /// <summary>Ngưỡng lệch giá (%) đang áp dụng.</summary>
        public decimal PriceToleranceRate { get; set; }

        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;

        public string? CustomerRejectReason { get; set; }
        public DateTime? CustomerDecisionAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionNote { get; set; }

        public string? SupplierOrderCode { get; set; }
        public DateTime? OrderedAt { get; set; }
        public DateTime? SupplierConfirmedAt { get; set; }
        public DateTime? SupplierShippedAt { get; set; }
        public string? DomesticCarrier { get; set; }
        public string? DomesticTrackingCode { get; set; }

        public DateTime? CancelledAt { get; set; }
        public string? CancelReason { get; set; }

        /// <summary>Khoản chênh giá đang chờ khách trả (nếu có).</summary>
        public Guid? PriceDifferencePaymentId { get; set; }
        public string? PriceDifferenceCheckoutUrl { get; set; }
        public string? PriceDifferencePaymentStatus { get; set; }

        /* ── Tiền đi NGƯỢC: công ty trả lại khách ───────────────────────────────────
           Có hai lý do: giá mua thực thấp hơn giá đã báo, hoặc huỷ đơn sau khi đã đặt NCC.
           Cả hai đều nằm trong PURCHASE_PAYMENTS với PaymentType bắt đầu bằng REFUND. */

        /// <summary>Số tiền phải hoàn / đã hoàn. 0 = không có khoản hoàn nào.</summary>
        public decimal RefundAmount { get; set; }

        public Guid? RefundPaymentId { get; set; }

        /// <summary>REFUND_PRICE_DIFF (giá thực thấp hơn) hoặc REFUND_CANCEL (huỷ sau khi đặt NCC).</summary>
        public string? RefundType { get; set; }

        /// <summary>PENDING / REFUNDED / CANCELLED.</summary>
        public string? RefundStatus { get; set; }
        public string? RefundStatusText { get; set; }
        public DateTime? RefundedAt { get; set; }

        /// <summary>Tỷ lệ phí huỷ (%) đang cấu hình. 0 = huỷ không mất phí.</summary>
        public decimal CancelFeeRate { get; set; }

        /// <summary>
        /// Phí huỷ đã trừ vào khoản hoàn loại REFUND_CANCEL — cộng từ chi tiết khoản hoàn
        /// (tính trên tiền hàng KHÁCH THỰC TRẢ, gồm cả phần chênh giá đã trả).
        /// </summary>
        public decimal CancelFeeAmount { get; set; }

        /// <summary>Tổng các khoản hoàn còn hiệu lực (chờ hoàn + đã hoàn) của đơn mua này.</summary>
        public decimal TotalRefundAmount { get; set; }

        /// <summary>Mọi khoản hoàn liên quan tới đơn mua này, mới nhất trước, kèm chi tiết từng dòng.</summary>
        public List<PurchaseRefundDto> Refunds { get; set; } = new();

        /// <summary>Trạng thái hoá đơn chặng VN của đơn kho (PENDING / PAID / CANCELLED).</summary>
        public string? WarehouseInvoiceStatus { get; set; }

        /// <summary>Đơn kho sinh ra khi đặt NCC — từ đây theo dõi như ký gửi.</summary>
        public Guid? WarehouseOrderId { get; set; }
        public string? WarehouseOrderCode { get; set; }
        public string? WarehouseOrderStatus { get; set; }
        public string? ReceivingNoteCode { get; set; }
        public string? ReceivingNoteStatus { get; set; }

        /// <summary>Số ảnh bằng chứng đặt hàng đã tải lên (PURCHASE_PROOF). Ẩn với khách.</summary>
        public int ProofCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<PurchaseOrderItemResponseDto> Items { get; set; } = new();
    }
}
