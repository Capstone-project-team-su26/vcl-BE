using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Bảng điều khiển của KHÁCH: trả lời đúng một câu — "hôm nay tôi phải làm gì?".
    ///
    /// Không phải bảng thống kê. Mỗi mục trong <see cref="Actions"/> là một VIỆC khách
    /// phải tự tay làm; thứ gì hệ thống hoặc nhân viên đang lo thì nằm ở
    /// <see cref="InProgress"/> để khách biết hàng vẫn đang chạy mà không phải bấm gì.
    ///
    /// Gộp CẢ ký gửi lẫn mua hộ. Trước đây màn hình gọi hai ba endpoint danh sách rồi tự
    /// cộng ở trình duyệt, nên vừa kéo cả trăm đơn về máy khách vừa đếm sai: nó tìm đơn ở
    /// trạng thái "DELIVERED" trong khi hệ thống không bao giờ đặt đơn sang trạng thái đó
    /// (việc giao nằm ở phiếu giao và ở trạng thái từng kiện), thành ra thẻ "chờ xác nhận
    /// đã nhận" luôn bằng 0.
    /// </summary>
    public class CustomerDashboardResponse
    {
        public List<CustomerDashboardActionDto> Actions { get; set; } = new();

        public CustomerDashboardProgressDto InProgress { get; set; } = new();

        /// <summary>Tổng tiền khách đang phải trả (đã chốt số), gộp hai luồng.</summary>
        public decimal TotalAmountDue { get; set; }

        /// <summary>Tổng tiền công ty sẽ hoàn lại khách, chưa chuyển.</summary>
        public decimal TotalRefundIncoming { get; set; }

        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// Số liệu để VẼ BIỂU ĐỒ. Tách khỏi phần "việc cần làm" vì đây là thông tin để
        /// khách nhìn lại, không phải việc phải xử lý.
        /// </summary>
        public CustomerDashboardStatsDto Stats { get; set; } = new();
    }

    public class CustomerDashboardStatsDto
    {
        /// <summary>
        /// Chi tiêu 6 tháng gần nhất, cũ → mới, ĐỦ 6 tháng kể cả tháng không tiêu đồng nào.
        /// Bỏ tháng rỗng thì đường biểu đồ nói dối: nhìn như tháng nào cũng có hàng.
        /// </summary>
        public List<CustomerMonthlySpendDto> MonthlySpend { get; set; } = new();

        /// <summary>
        /// Tiền của khách đi đâu, cộng dồn trên các đơn đã có báo giá: cước quốc tế /
        /// phí dịch vụ và phụ phí / thuế. Ba phần này cộng lại bằng tổng báo giá.
        /// </summary>
        public decimal TotalFreight { get; set; }
        public decimal TotalServiceFee { get; set; }
        public decimal TotalTax { get; set; }

        /// <summary>Số đơn đã tính vào cơ cấu chi phí — để màn hình nói rõ "trên N đơn".</summary>
        public int QuotedOrderCount { get; set; }
    }

    public class CustomerMonthlySpendDto
    {
        /// <summary>Dạng yyyy-MM, để màn hình tự định dạng theo ngôn ngữ.</summary>
        public string Month { get; set; } = string.Empty;

        /// <summary>Số tiền khách ĐÃ TRẢ trong tháng (chỉ tính khoản đã vào tài khoản).</summary>
        public decimal Paid { get; set; }

        /// <summary>Số lần thu trong tháng.</summary>
        public int PaymentCount { get; set; }
    }

    /// <summary>Một việc khách phải làm. Count = 0 thì màn hình vẫn hiện thẻ (trạng thái "xong").</summary>
    public class CustomerDashboardActionDto
    {
        /// <summary>
        /// QUOTATION_CONFIRM | PAYMENT_DUE | PURCHASE_PRICE_DECISION | DELIVERY_CONFIRM | REFUND_INCOMING
        /// </summary>
        public string Key { get; set; } = string.Empty;

        public int Count { get; set; }

        /// <summary>Số tiền gắn với việc này (0 nếu việc không dính tiền).</summary>
        public decimal Amount { get; set; }

        /// <summary>Tách theo luồng để màn hình nói được "2 ký gửi, 1 mua hộ".</summary>
        public int ConsignmentCount { get; set; }
        public int PurchaseCount { get; set; }

        /// <summary>Hạn sớm nhất trong nhóm (báo giá hết hiệu lực). Null = không có hạn.</summary>
        public DateTime? EarliestDueAt { get; set; }

        /// <summary>Vài dòng đầu để khách thấy ngay đơn nào, khỏi bấm sang trang khác.</summary>
        public List<CustomerDashboardItemDto> Items { get; set; } = new();
    }

    public class CustomerDashboardItemDto
    {
        /// <summary>CONSIGNMENT hoặc PURCHASE — màn hình dẫn sang đúng trang chi tiết.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Id để mở chi tiết: đơn ký gửi là orderId, mua hộ là purchaseRequestId.</summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Dòng đến từ ĐƠN KHO của yêu cầu mua hộ (PUR-xxx-n, <c>Order.OrderType = PURCHASE</c>) thì đây là
        /// orderId của đơn kho đó — <see cref="Kind"/> vẫn là PURCHASE và <see cref="Id"/> vẫn là yêu cầu mua hộ
        /// sở hữu. Null với dòng của chính yêu cầu mua hộ hoặc đơn ký gửi. Màn hình dùng để phân biệt khoản
        /// tất toán đơn kho (trả theo luồng tất toán) với khoản trả trước / chênh giá của yêu cầu.
        /// </summary>
        public Guid? WarehouseOrderId { get; set; }

        public string Code { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        /// <summary>Hạn phải xử lý (báo giá hết hiệu lực). Null = không có hạn.</summary>
        public DateTime? DueAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Hàng đang chạy — khách KHÔNG phải làm gì, chỉ để yên tâm là đơn không nằm im.
    /// Đếm theo đơn, một đơn chỉ rơi vào đúng một chặng.
    /// </summary>
    public class CustomerDashboardProgressDto
    {
        /// <summary>Chờ nhân viên xem xét / báo giá.</summary>
        public int WaitingStaff { get; set; }

        /// <summary>Đã trả tiền, đang chờ hàng về kho nguồn (gồm cả mua hộ đang đi mua).</summary>
        public int WaitingGoods { get; set; }

        /// <summary>Hàng đã ở kho nguồn.</summary>
        public int AtOriginWarehouse { get; set; }

        /// <summary>Đang trên đường về Việt Nam.</summary>
        public int InTransit { get; set; }

        /// <summary>Đã về kho Việt Nam.</summary>
        public int AtVietnamWarehouse { get; set; }

        /// <summary>Đang giao tới khách.</summary>
        public int OutForDelivery { get; set; }

        public int Total { get; set; }
    }
}
