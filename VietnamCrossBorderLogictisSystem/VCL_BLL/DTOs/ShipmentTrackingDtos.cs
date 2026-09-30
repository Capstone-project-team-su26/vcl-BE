using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    // ── Phía khách: theo dõi đơn ────────────────────────────────────────────────────────────

    public class OrderTrackingDto
    {
        public Guid OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string? OrderStatus { get; set; }

        /// <summary>Chặng chậm nhất trong các kiện của đơn (xem CustomerTrackingStages / trạng thái lô).</summary>
        public string? CurrentStage { get; set; }
        public string? CurrentStageText { get; set; }

        /// <summary>Kiện của đơn đang đi ở nhiều chặng khác nhau (tách chuyến).</summary>
        public bool IsSplitAcrossStages { get; set; }

        public string? OriginWarehouseName { get; set; }
        public string? DestinationWarehouseName { get; set; }
        public DateTime? EstimatedArrivalDate { get; set; }
        public bool ExportHold { get; set; }
        public string? ExportHoldReason { get; set; }

        /// <summary>
        /// Tuyến quốc tế đơn đang đi và các chặng của tuyến. Null khi đơn chưa được gán tuyến
        /// (hàng còn nằm kho nguồn chờ xếp chuyến) — màn hình phải chịu được trường hợp đó.
        /// </summary>
        public OrderTrackingRouteDto? Route { get; set; }

        /// <summary>
        /// Tiền của đơn theo báo giá đang có hiệu lực, kèm đã trả / còn phải trả.
        /// Null khi đơn chưa có báo giá nào — khách chưa biết giá thì không bày ô tiền rỗng.
        /// </summary>
        public OrderTrackingMoneyDto? Money { get; set; }

        public List<OrderTrackingParcelDto> Parcels { get; set; } = new();
        public List<OrderTrackingShipmentDto> Shipments { get; set; } = new();
        public List<OrderTrackingEventDto> Events { get; set; } = new();
    }

    /// <summary>
    /// Lộ trình đơn đi: tuyến đã cấu hình + các chặng từ kho nguồn tới tận nhà khách.
    ///
    /// Khác <see cref="OrderTrackingEventDto"/>: events là NHỮNG GÌ ĐÃ XẢY RA theo thời gian,
    /// còn đây là ĐƯỜNG ĐI, có cả chặng chưa tới. Khách cần cả hai: một cái trả lời "giờ hàng
    /// ở đâu", cái kia trả lời "còn phải qua những đâu nữa".
    /// </summary>
    public class OrderTrackingRouteDto
    {
        public string? RouteCode { get; set; }
        public string? RouteName { get; set; }

        /// <summary>AIR · SEA · ROAD · RAIL</summary>
        public string? TransportMode { get; set; }

        /// <summary>Chữ tiếng Việt của <see cref="TransportMode"/> để màn hình không tự dịch.</summary>
        public string? TransportModeText { get; set; }

        public string? OriginCountry { get; set; }
        public string? DestinationCountry { get; set; }
        public int? EstimatedTransitDays { get; set; }

        public List<OrderTrackingRouteStopDto> Stops { get; set; } = new();
    }

    /// <summary>Một chặng trên lộ trình. `Reached` = hàng đã qua chặng này chưa.</summary>
    public class OrderTrackingRouteStopDto
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public bool Reached { get; set; }

        /// <summary>Thời điểm qua chặng — null khi chưa qua, hoặc đã qua mà không có mốc lưu.</summary>
        public DateTime? ReachedAt { get; set; }
    }

    /// <summary>
    /// Tiền của đơn nhìn từ màn theo dõi. Lấy từ BÁO GIÁ đã lưu chứ không tính lại theo bảng giá
    /// hiện tại — đổi giá sau này không được làm đổi số khách đã nhìn thấy.
    /// </summary>
    public class OrderTrackingMoneyDto
    {
        public Guid QuotationId { get; set; }

        /// <summary>Trạng thái báo giá (SENT · ACCEPTED · REJECTED...).</summary>
        public string? QuotationStatus { get; set; }

        public decimal EstimatedFreightCharge { get; set; }
        public decimal ServiceFee { get; set; }
        public decimal TaxAndDuty { get; set; }

        /// <summary>Tổng phải trả = cước + phí dịch vụ + thuế.</summary>
        public decimal TotalEstimatedCost { get; set; }

        /// <summary>Đã thu (chỉ cộng giao dịch PAID).</summary>
        public decimal PaidAmount { get; set; }

        /// <summary>Còn phải trả, không bao giờ âm.</summary>
        public decimal OutstandingAmount { get; set; }

        /// <summary>UNPAID · PARTIALLY_PAID · PAID — suy từ hai số trên, không đọc cột rời.</summary>
        public string PaymentStatus { get; set; } = string.Empty;
    }

    public class OrderTrackingParcelDto
    {
        public Guid ParcelId { get; set; }
        public string? PackageCode { get; set; }
        public string? ProductName { get; set; }
        public string? Stage { get; set; }
        public string? StageText { get; set; }

        /// <summary>Trạng thái kiện gốc — sau khi về kho VN thì đọc tiếp theo luồng giao hàng.</summary>
        public string? PackageStatus { get; set; }
        public string? ShipmentCode { get; set; }
    }

    public class OrderTrackingShipmentDto
    {
        public string? ShipmentCode { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
        public string? CarrierName { get; set; }

        /// <summary>Mã vận đơn / mã tra cứu của hãng — khách tự tra trên trang của hãng được.</summary>
        public string? CarrierTrackingCode { get; set; }
        public DateTime? HandedOverAt { get; set; }
        public DateTime? EstimatedArrivalDate { get; set; }
        public List<string> PackageCodes { get; set; } = new();
    }

    public class OrderTrackingEventDto
    {
        public DateTime Time { get; set; }
        public string? Stage { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Location { get; set; }
        public string? ShipmentCode { get; set; }
    }

    public class TrackedOrderDto
    {
        public Guid OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CurrentStage { get; set; }
        public string? CurrentStageText { get; set; }
        public int ParcelCount { get; set; }
        public List<string> ShipmentCodes { get; set; } = new();
        public DateTime? LastEventAt { get; set; }
        public string? LastEventTitle { get; set; }
        public DateTime? EstimatedArrivalDate { get; set; }
    }

    public class TrackedOrderQuery
    {
        /// <summary>Lọc theo chặng, nhiều giá trị ngăn bởi dấu phẩy.</summary>
        public string? Stage { get; set; }
        public string? Search { get; set; }

        /// <summary>Mặc định chỉ trả đơn chưa giao xong; true để lấy cả đơn đã giao / đã hoàn thành.</summary>
        public bool IncludeFinished { get; set; }

        /// <summary>Tên cũ của <see cref="IncludeFinished"/>, giữ để client cũ không gãy.</summary>
        public bool IncludeArrived { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class PagedTrackedOrdersDto
    {
        public List<TrackedOrderDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    // ── Phía Sale: theo dõi lô ──────────────────────────────────────────────────────────────

    public class NextMilestoneDto
    {
        public string? Status { get; set; }
        public string? Text { get; set; }

        /// <summary>NOTE (bắt buộc ghi chú) và/hoặc loại giấy tờ phải có trên lô.</summary>
        public List<string> Requirements { get; set; } = new();

        /// <summary>Giấy tờ còn thiếu trên lô — FE khoá nút cho tới khi up đủ.</summary>
        public List<string> MissingDocuments { get; set; } = new();
    }

    public class ShipmentTrackingEventDto
    {
        public Guid Id { get; set; }
        public DateTime? Time { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
        public string? Location { get; set; }

        /// <summary>Ghi chú nội bộ.</summary>
        public string? Note { get; set; }

        /// <summary>Lời nhắn Sale nhập cho khách (null nếu dùng câu mặc định).</summary>
        public string? CustomerMessage { get; set; }
        public string? CreatedByName { get; set; }
    }

    public class ShipmentOrderRefDto
    {
        public Guid OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string? CustomerName { get; set; }
        public int ParcelCount { get; set; }
    }

    public class ShipmentTrackingSummaryDto
    {
        public Guid ShipmentId { get; set; }
        public string? ShipmentCode { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
        public string? OriginWarehouseName { get; set; }
        public string? DestinationWarehouseName { get; set; }
        public string? CarrierName { get; set; }
        public string? CarrierTrackingCode { get; set; }
        public DateTime? HandedOverAt { get; set; }
        public DateTime? EstimatedArrivalDate { get; set; }
        public DateTime? LastUpdateAt { get; set; }
        public int DaysSinceLastUpdate { get; set; }

        /// <summary>Quá ngày dự kiến mà chưa về Việt Nam.</summary>
        public bool IsOverdue { get; set; }

        /// <summary>Đã lâu không cập nhật mốc — Sale nên hỏi hãng và ghi mốc mới.</summary>
        public bool NeedsUpdate { get; set; }
        public int OrderCount { get; set; }
        public int ParcelCount { get; set; }
        public List<NextMilestoneDto> NextMilestones { get; set; } = new();
    }

    public class ShipmentTimelineDto : ShipmentTrackingSummaryDto
    {
        public List<ShipmentTrackingEventDto> Events { get; set; } = new();
        public List<ShipmentOrderRefDto> Orders { get; set; } = new();
        public List<AttachmentDto> Attachments { get; set; } = new();
    }
}
