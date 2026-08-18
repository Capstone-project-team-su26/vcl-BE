using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Một trạng thái của yêu cầu ký gửi để FE đổ dropdown/lọc (code + nhãn hiển thị).
    /// </summary>
    public class ConsignmentStatusDto
    {
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>
    /// Chi tiết đầy đủ một yêu cầu ký gửi: header + khách hàng + mặt hàng + báo giá mới nhất.
    /// </summary>
    public class ConsignmentDetailResponse
    {
        public Guid OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string ConsignmentType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? OrderType { get; set; }
        public decimal? TotalWeight { get; set; }
        /// <summary>Tổng thể tích (cm³)</summary>
        public decimal? TotalVolume { get; set; }
        public string? Note { get; set; }
        public string? Route { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresInspection { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? PaymentConfirmedAt { get; set; }

        /// <summary>Thời điểm lập báo giá mới nhất của đơn (null nếu chưa có báo giá).</summary>
        public DateTime? QuotationCreatedAt { get; set; }

        /// <summary>Mốc thời gian tương ứng với trạng thái hiện tại của đơn (dùng cho FE hiển thị real-time theo status).</summary>
        public DateTime? StatusUpdatedAt { get; set; }

        /// <summary>Link phiếu xác nhận ký gửi (PDF) sinh khi Sale xác nhận yêu cầu. Null nếu chưa sinh.</summary>
        public string? ReceiptPdfUrl { get; set; }

        /// <summary>
        /// Khách muốn hàng đi đâu khi về tới kho VN: <c>DIRECT_DELIVERY</c> giao ngay,
        /// <c>STORE_AT_VN</c> gửi lại kho. Null là khách không chọn lúc đặt.
        /// Sale nhìn trường này để biết trước cần chuẩn bị giao hay chuẩn bị chỗ kệ.
        /// </summary>
        public string? DefaultDestinationHandling { get; set; }

        /// <summary>Nhãn tiếng Việt của <see cref="DefaultDestinationHandling"/>.</summary>
        public string? DefaultDestinationHandlingText { get; set; }

        /// <summary>Danh sách ID các quy định giá / dịch vụ phụ áp dụng cho đơn.</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        public ConsignmentCustomerInfo Customer { get; set; } = new();
        public List<ConsignmentItemDetail> Items { get; set; } = new();

        /// <summary>Báo giá mới nhất của đơn (null nếu chưa có).</summary>
        public ConsignmentQuotationSummary? Quotation { get; set; }

        /// <summary>
        /// Hàng của đơn đang đi theo lô nào và tới đâu rồi. Rỗng khi đơn chưa đóng kiện.
        /// <para>
        /// Sale là người nghe khách hỏi "hàng em tới đâu rồi", nên cần thấy đúng thứ màn Vận
        /// chuyển của OM đang thấy: mã lô, chặng, và kết quả kho VN kiểm đếm từng kiện.
        /// </para>
        /// </summary>
        public List<ConsignmentShipmentGroup> Shipments { get; set; } = new();

        /// <summary>Số kiện của đơn bị kho Việt Nam ghi nhận lệch khi kiểm đếm.</summary>
        public int DiscrepancyParcelCount { get; set; }

        /// <summary>
        /// Thời điểm Sale xác nhận đơn đủ điều kiện cho hàng vào kho VN. Null là chưa xác nhận,
        /// và kho sẽ không lập được phiếu nhập kho cho đơn này.
        /// </summary>
        public DateTime? WarehouseNotifiedAt { get; set; }

        /// <summary>Ghi chú Sale để lại lúc xác nhận đủ điều kiện nhập kho.</summary>
        public string? WarehouseNotifiedNote { get; set; }
    }

    /// <summary>
    /// Một lô vận chuyển quốc tế kèm những kiện của đơn đang đi theo lô đó.
    /// Kiện đã đóng nhưng chưa được xếp lô nằm ở nhóm có <see cref="ShipmentId"/> null.
    /// </summary>
    public class ConsignmentShipmentGroup
    {
        public Guid? ShipmentId { get; set; }
        public string? ShipmentCode { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }

        /// <summary>Mã vận đơn / số container bên vận chuyển thứ ba cấp, để Sale tra cứu hộ khách.</summary>
        public string? CarrierTrackingCode { get; set; }

        public string? OriginWarehouseName { get; set; }
        public string? DestinationWarehouseName { get; set; }
        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }

        public List<ConsignmentParcelStatus> Parcels { get; set; } = new();
    }

    /// <summary>Một kiện của đơn: đang ở đâu, khách muốn xử lý thế nào, kho đếm ra sao.</summary>
    public class ConsignmentParcelStatus
    {
        public Guid ParcelId { get; set; }
        public string? PackageCode { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
        public decimal? Weight { get; set; }

        /// <summary>Xử lý khi về kho VN của riêng kiện này (có thể khác nguyện vọng ban đầu của đơn).</summary>
        public string? DestinationHandling { get; set; }
        public string? DestinationHandlingText { get; set; }

        public DateTime? CheckInTime { get; set; }
        public DateTime? PutAwayTime { get; set; }

        /// <summary>Kết quả kho VN đối chiếu kiện này (null nếu kho chưa đếm).</summary>
        public ConsignmentParcelCheckResult? Inspection { get; set; }
    }

    /// <summary>Một hàng đợi việc của Sale ở chặng cuối (chờ tất toán hoặc chờ xuất kho).</summary>
    public class SettlementQueueResponse
    {
        public List<SettlementQueueItem> Items { get; set; } = new();
    }

    public class SettlementQueueItem
    {
        public Guid OrderId { get; set; }
        public string? OrderCode { get; set; }

        /// <summary><c>CONSIGNMENT</c> hoặc <c>PURCHASE</c> — màn hình dùng để mở đúng trang chi tiết.</summary>
        public string OrderType { get; set; } = string.Empty;

        /// <summary>Có giá trị với đơn mua hộ, để mở được màn chi tiết yêu cầu mua hộ.</summary>
        public Guid? PurchaseRequestId { get; set; }

        public string? Status { get; set; }

        public Guid? CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }

        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public string? Route { get; set; }

        /// <summary>Số kiện của đơn đã về tới kho VN.</summary>
        public int ParcelCount { get; set; }
        public decimal TotalWeight { get; set; }

        /// <summary>Số kiện kho ghi nhận lệch — Sale nên xử lý trước khi chốt tiền.</summary>
        public int DiscrepancyParcelCount { get; set; }

        public DateTime? ArrivedAt { get; set; }

        /// <summary>Thời điểm Sale xác nhận đủ điều kiện nhập kho (chỉ đơn ký gửi).</summary>
        public DateTime? WarehouseNotifiedAt { get; set; }

        /// <summary>Số kiện đủ điều kiện đưa vào phiếu xuất kho ngay.</summary>
        public int ReadyToDeliverParcelCount { get; set; }

        /// <summary>
        /// Số tiền đợt cuối đang chờ khách trả. Null là Sale chưa chốt phí cuối, nên màn khách
        /// hiển thị "chờ nhân viên chốt" thay vì bày một nút trả tiền chưa dùng được.
        /// </summary>
        public decimal? PendingPaymentAmount { get; set; }

        /// <summary>Link thanh toán của đợt cuối đang chờ, để khách bấm trả ngay.</summary>
        public string? PendingCheckoutUrl { get; set; }
    }

    /// <summary>
    /// Hàng đợi "Đơn hàng cần xử lý" của Sale — mỗi dòng là MỘT VIỆC, không phải một đơn.
    /// </summary>
    /// <remarks>
    /// Một đơn có thể vừa có kiện giao thẳng vừa có kiện gửi lại kho: hai việc khác nhau, hai
    /// nút khác nhau, hai người xử lý tiếp khác nhau. Gộp chung một dòng thì Sale phải đọc bảng
    /// hai tầng mới biết mình cần bấm gì, nên tách phẳng ngay từ server.
    /// </remarks>
    public class OrderActionQueueResponse
    {
        public List<OrderActionQueueItem> Items { get; set; } = new();
    }

    public class OrderActionQueueItem : SettlementQueueItem
    {
        /// <summary>Khoá dòng do server sinh (<c>{orderId}:{nhóm}</c>), để mỗi màn khỏi tự ghép một kiểu.</summary>
        public string RowKey { get; set; } = string.Empty;

        /// <summary><c>DIRECT_DELIVERY</c> hoặc <c>STORE_AT_VN</c>.</summary>
        public string HandlingGroup { get; set; } = string.Empty;
        public string HandlingGroupText { get; set; } = string.Empty;

        /// <summary>Số kiện riêng của nhóm này; <c>ParcelCount</c> vẫn là tổng kiện đã về của đơn.</summary>
        public int GroupParcelCount { get; set; }
        public decimal GroupTotalWeight { get; set; }

        /// <summary>Kiện của nhóm, để màn hình gửi thẳng lên khi lập phiếu — khỏi đoán lại.</summary>
        public List<Guid> ParcelIds { get; set; } = new();

        /// <summary>
        /// Tình trạng việc: <c>AWAITING_RECEIVING_NOTE</c>, <c>AWAITING_DELIVERY_REQUEST</c>,
        /// <c>AWAITING_WAREHOUSE_NOTICE</c>, <c>NOTIFIED_AWAITING_INBOUND</c>,
        /// <c>INBOUND_PENDING_APPROVAL</c>, <c>INBOUND_REJECTED</c>.
        /// </summary>
        public string ActionState { get; set; } = string.Empty;
        public string ActionStateText { get; set; } = string.Empty;

        /// <summary>
        /// <c>CREATE_RECEIVING_NOTE</c>, <c>CREATE_DELIVERY_REQUEST</c>, <c>NOTIFY_WAREHOUSE</c>,
        /// <c>RENOTIFY_WAREHOUSE</c> hoặc <c>NONE</c>.
        /// </summary>
        public string NextAction { get; set; } = "NONE";

        /// <summary>Kho sẽ nhận hàng của đơn — chỉ có ở dòng lập phiếu tiếp nhận, để FE đổ sẵn.</summary>
        public Guid? ReceivingWarehouseId { get; set; }
        public string? ReceivingWarehouseName { get; set; }

        /// <summary>Nút có bấm được không; false thì <see cref="BlockedReason"/> nói vì sao.</summary>
        public bool CanAct { get; set; }
        public string? BlockedReason { get; set; }

        public string? InboundCode { get; set; }
        public string? InboundStatus { get; set; }
        public string? InboundRejectionReason { get; set; }
    }

    /// <summary>Sale báo cho kho rằng đơn này có hàng khách xin gửi lại, kho vào lập phiếu nhập kho.</summary>
    public class NotifyWarehouseRequest
    {
        /// <summary>Ghi chú kèm theo, ví dụ điều kiện đã thoả thuận riêng với khách. Không bắt buộc.</summary>
        [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
        public string? Note { get; set; }
    }

    public class NotifyWarehouseResponse
    {
        public Guid OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public DateTime WarehouseNotifiedAt { get; set; }
        public string? WarehouseNotifiedByName { get; set; }
        public string? Note { get; set; }

        /// <summary>Số kiện của đơn khách xin gửi lại kho VN — kho sẽ lập phiếu nhập cho đúng ngần này.</summary>
        public int StoreAtVnParcelCount { get; set; }

        /// <summary>
        /// True khi đơn đã được báo cho kho từ trước. Bấm lại không dời mốc và không bắn thông
        /// báo lần hai; màn hình dùng cờ này để nói "đã báo rồi" thay vì mừng hụt.
        /// </summary>
        public bool AlreadyNotified { get; set; }
    }

    /// <summary>Biên bản đối chiếu kiện tại kho VN, rút gọn cho Sale đọc.</summary>
    public class ConsignmentParcelCheckResult
    {
        public bool HasDiscrepancy { get; set; }

        /// <summary>Mô tả lệch một dòng, ví dụ "Thiếu 1 kiện; nhẹ hơn 0,6 kg". Null khi khớp.</summary>
        public string? Summary { get; set; }

        public string? ConditionText { get; set; }
        public int? ActualQuantity { get; set; }
        public decimal? ActualWeight { get; set; }
        public decimal? DeclaredWeight { get; set; }
        public string? Note { get; set; }
        public DateTime? InspectedAt { get; set; }
    }

    public class ConsignmentCustomerInfo
    {
        public Guid? CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    public class ConsignmentItemDetail
    {
        public Guid Id { get; set; }
        public string? ProductName { get; set; }
        public string? ProductType { get; set; }
        public int? Quantity { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? Length { get; set; }
        public decimal? DeclaredValue { get; set; }

        /// <summary>Danh sách URL ảnh của mặt hàng (đã tách từ chuỗi lưu DB).</summary>
        public List<string> ReferenceUrls { get; set; } = new();

        public string? DomesticTrackingCode { get; set; }
        public Guid? PackageConfigurationId { get; set; }
        public PackageConfigurationResponseDto? PackageConfiguration { get; set; }

        /// <summary>Cân nặng quy đổi của dòng sản phẩm = Length × Width × Height × Quantity / 5000</summary>
        public decimal? VolumetricWeight { get; set; }
    }

    public class ConsignmentQuotationSummary
    {
        public Guid QuotationId { get; set; }
        public string? QuoteType { get; set; }
        public string? Status { get; set; }
        public decimal? EstimatedFreightCharge { get; set; }
        public decimal? DomesticShippingFee { get; set; }
        public decimal? ServiceFee { get; set; }
        public decimal? TaxAndDuty { get; set; }
        /// <summary>Tổng thể tích (cm³)</summary>
        public decimal? TotalVolume { get; set; }
        public decimal TotalEstimatedCost { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ExpiredAt { get; set; }

        /// <summary>Kho tiếp nhận hàng do Sale chọn khi lập báo giá (kho quốc tế đầu tuyến, VD kho Trung Quốc).</summary>
        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public string? WarehouseCode { get; set; }
        public string? WarehouseAddress { get; set; }

        /// <summary>Mã vùng của kho theo ISO (VD "CN", "VN"). Dùng để lọc đơn theo kho phụ trách.</summary>
        public string? WarehouseRegion { get; set; }

        /// <summary>ORIGIN (kho đầu tuyến) hoặc DESTINATION (kho đích).</summary>
        public string? WarehouseType { get; set; }
    }

    /// <summary>
    /// Yêu cầu tạo ký gửi (A1) — header + danh sách mặt hàng + phương án vận chuyển (A2).
    /// </summary>
    public class CreateConsignmentRequest
    {
        /// <summary>Tuyến hàng (VD: "China-Vietnam"). Khớp với PricingRule.Route đang active.</summary>
        [Required]
        public string Route { get; set; } = string.Empty;

        /// <summary>Phương án vận chuyển. Phải khớp một PricingRule active theo Route đã chọn (FE lấy danh sách từ API pricing-rules).</summary>
        [Required]
        public string ShippingOption { get; set; } = string.Empty;

        /// <summary>Tên người nhận hàng.</summary>
        public string? ReceiverName { get; set; }

        /// <summary>Số điện thoại người nhận.</summary>
        public string? ReceiverPhone { get; set; }

        /// <summary>Địa chỉ giao hàng (người nhận cuối tại VN).</summary>
        public string? ReceiverAddress { get; set; }

        /// <summary>Danh sách ID cấu hình bảng giá/dịch vụ phụ (ví dụ: đóng gỗ, kiểm hàng, bảo hiểm...).</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        // --- Các cờ dịch vụ phụ dạng boolean (FE hiện gửi kiểu này thay vì PricingRuleIds).
        // BE map cờ -> PricingRule active theo RuleType tương ứng, union với PricingRuleIds.

        /// <summary>Khách yêu cầu kiểm hàng (map rule INSPECTION).</summary>
        public bool RequiresInspection { get; set; }

        /// <summary>Khách yêu cầu đóng gói lại (map rule PACKING).</summary>
        public bool RequiresPacking { get; set; }

        /// <summary>Khách yêu cầu đóng thùng gỗ (map rule WOODEN_BOX).</summary>
        public bool RequiresWoodenCrate { get; set; }

        /// <summary>Khách đăng ký bảo hiểm (map rule INSURANCE).</summary>
        public bool RequiresInsurance { get; set; }

        /// <summary>
        /// Khách muốn xử lý thế nào khi hàng về Việt Nam:
        /// <c>DIRECT_DELIVERY</c> giao thẳng tới nhà · <c>STORE_AT_VN</c> gửi lại kho chờ nhận sau.
        /// <para>
        /// Bỏ trống nghĩa là khách chưa chọn — kho sẽ hỏi lại lúc hàng về, khác với việc khách
        /// chủ động chọn giao thẳng. Chọn gửi lại kho thì phát sinh phí lưu kho sau kỳ miễn phí.
        /// </para>
        /// </summary>
        public string? DefaultDestinationHandling { get; set; }

        /// <summary>Ghi chú xử lý chung cho cả yêu cầu.</summary>
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Yêu cầu ký gửi phải có ít nhất một mặt hàng.")]
        public List<ConsignmentItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Một mặt hàng trong yêu cầu ký gửi.
    /// </summary>
    public class ConsignmentItemRequest
    {
        [Required]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Loại hàng — dùng để đối chiếu hàng cấm/hạn chế.</summary>
        [Required]
        public string ProductType { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        public decimal? Weight { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? Length { get; set; }

        /// <summary>Giá trị khai báo.</summary>
        public decimal? DeclaredValue { get; set; }

        /// <summary>Danh sách URL ảnh của mặt hàng (upload qua POST /api/uploads/images).</summary>
        public List<string>? ReferenceUrls { get; set; }

        /// <summary>Mã tracking nội địa (nếu có) — chỉ để kho đối soát, không tính phí.</summary>
        public string? DomesticTrackingCode { get; set; }

        /// <summary>ID cấu hình thùng đóng gói khách chọn (nếu trống hệ thống sẽ tự gợi ý).</summary>
        public Guid? PackageConfigurationId { get; set; }

        /// <summary>Gộp ReferenceUrls thành 1 chuỗi lưu DB (phân cách "|"), bỏ trùng/rỗng.</summary>
        public string? CombinedReferenceUrls()
        {
            if (ReferenceUrls == null) return null;
            var distinct = ReferenceUrls
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct()
                .ToList();
            return distinct.Any() ? string.Join("|", distinct) : null;
        }

        /// <summary>Tách chuỗi ảnh lưu DB (phân cách "|") thành danh sách URL cho response.</summary>
        public static List<string> SplitReferenceUrls(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return new List<string>();
            return stored.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.Length > 0)
                .ToList();
        }
    }



    /// <summary>
    /// Cảnh báo hàng cấm/hạn chế phát hiện khi tạo ký gửi.
    /// </summary>
    public class RestrictedItemWarning
    {
        public string ProductName { get; set; } = string.Empty;
        public string MatchedItem { get; set; } = string.Empty;
        public string RestrictionType { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    /// <summary>
    /// Yêu cầu kiểm tra hàng cấm/hạn chế (chỉ kiểm tra, không tạo Order).
    /// </summary>
    public class ValidateItemsRequest
    {
        [Required]
        [MinLength(1, ErrorMessage = "Phải có ít nhất một mặt hàng để kiểm tra.")]
        public List<ConsignmentItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Kết quả kiểm tra một mặt hàng so với danh mục hàng cấm/hạn chế.
    /// </summary>
    public class ItemValidationResult
    {
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Mức độ: BANNED (cấm), RESTRICTED (hạn chế), WARNING (cảnh báo).</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Tên mục trong danh mục đã khớp.</summary>
        public string MatchedItem { get; set; } = string.Empty;

        /// <summary>Quốc gia áp dụng của rule (nếu có).</summary>
        public string? Country { get; set; }

        /// <summary>Lý do hạn chế (lấy từ Note của rule).</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Kết quả kiểm tra cả danh sách mặt hàng.
    /// </summary>
    public class ValidateItemsResponse
    {
        /// <summary>True nếu KHÔNG có mặt hàng nào bị cấm (BANNED). Hàng hạn chế vẫn cho tạo.</summary>
        public bool CanCreate { get; set; }

        /// <summary>Chỉ chứa các mặt hàng có vấn đề (cấm/hạn chế/cảnh báo).</summary>
        public List<ItemValidationResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Response sau khi tạo ký gửi: thông tin đơn + báo giá tạm tính + cảnh báo (nếu có).
    /// </summary>
    public class CreateConsignmentResponse
    {
        public Guid OrderId { get; set; }

        /// <summary>Mã gửi hàng thân thiện (mã nghiệp vụ chính hiển thị cho Customer thay UUID).</summary>
        public string ConsignmentCode { get; set; } = string.Empty;

        /// <summary>ID khách hàng (Customer) sở hữu đơn.</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>ID user (tài khoản) của khách — FE cần để xử lý.</summary>
        public Guid? UserId { get; set; }

        public string Status { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public bool RequiresInspection { get; set; }
        public int ItemCount { get; set; }

        /// <summary>Danh sách ID các quy định giá / dịch vụ phụ áp dụng cho đơn.</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        /// <summary>Báo giá tạm tính sinh tự động sau khi tạo.</summary>
        public EstimateQuotationResponse Quotation { get; set; } = new();

        /// <summary>Danh sách cảnh báo hàng hạn chế (Restricted). Hàng cấm (Banned) sẽ bị chặn tạo.</summary>
        public List<RestrictedItemWarning> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Yêu cầu tạo ký gửi bởi nhân viên (Sales) thay cho khách hàng.
    /// </summary>
    public class CreateConsignmentByStaffRequest
    {
        /// <summary>ID khách hàng (nếu chọn từ danh sách có sẵn)</summary>
        public Guid? CustomerId { get; set; }

        /// <summary>Số điện thoại khách hàng (để tìm kiếm khách hàng)</summary>
        public string? CustomerPhone { get; set; }

        /// <summary>Email khách hàng (để tìm kiếm khách hàng)</summary>
        public string? CustomerEmail { get; set; }

        /// <summary>Tuyến hàng (VD: "China-Vietnam" hoặc "TQ-VN"). Khớp với PricingRule.Route đang active.</summary>
        [Required(ErrorMessage = "Vui lòng chọn tuyến hàng.")]
        public string Route { get; set; } = string.Empty;

        /// <summary>Phương án vận chuyển. Phải khớp một PricingRule active theo Route đã chọn.</summary>
        [Required(ErrorMessage = "Vui lòng chọn phương thức vận chuyển.")]
        public string ShippingOption { get; set; } = string.Empty;

        /// <summary>Tên người nhận hàng.</summary>
        public string? ReceiverName { get; set; }

        /// <summary>Số điện thoại người nhận.</summary>
        public string? ReceiverPhone { get; set; }

        /// <summary>Địa chỉ giao hàng (người nhận cuối tại VN).</summary>
        public string? ReceiverAddress { get; set; }

        /// <summary>Danh sách ID cấu hình bảng giá/dịch vụ phụ (ví dụ: đóng gỗ, kiểm hàng, bảo hiểm...).</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

        // --- Các cờ dịch vụ phụ dạng boolean (Sale tick hộ khách thay vì gửi PricingRuleIds).
        // BE map cờ -> PricingRule active theo RuleType tương ứng, union với PricingRuleIds.

        /// <summary>Khách yêu cầu kiểm hàng (map rule INSPECTION).</summary>
        public bool RequiresInspection { get; set; }

        /// <summary>Khách yêu cầu đóng gói lại (map rule PACKING).</summary>
        public bool RequiresPacking { get; set; }

        /// <summary>Khách yêu cầu đóng thùng gỗ (map rule WOODEN_BOX).</summary>
        public bool RequiresWoodenCrate { get; set; }

        /// <summary>Khách đăng ký bảo hiểm (map rule INSURANCE).</summary>
        public bool RequiresInsurance { get; set; }

        /// <summary>Ghi chú từ Sales/Khách hàng.</summary>
        public string? Note { get; set; }

        /// <summary>
        /// Nguyện vọng của khách khi hàng về tới kho VN: <c>DIRECT_DELIVERY</c> giao ngay hoặc
        /// <c>STORE_AT_VN</c> gửi lại kho (phát sinh phí lưu kho sau kỳ miễn phí).
        /// Sale hỏi khách rồi tick hộ. Bỏ trống hoặc gửi giá trị lạ thì coi như khách chưa chọn,
        /// lúc hàng về kho mặc định giao ngay.
        /// </summary>
        public string? DefaultDestinationHandling { get; set; }

        [Required(ErrorMessage = "Yêu cầu ký gửi phải có ít nhất một mặt hàng.")]
        [MinLength(1, ErrorMessage = "Yêu cầu ký gửi phải có ít nhất một mặt hàng.")]
        public List<ConsignmentItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Thông tin tra cứu công khai một đơn ký gửi theo mã vận đơn (không yêu cầu đăng nhập).
    /// Chỉ trả về dữ liệu trạng thái, không lộ thông tin nhạy cảm của khách hàng.
    /// </summary>
    public class PublicTrackingResponse
    {
        public string ConsignmentCode { get; set; } = string.Empty;
        public string? ConsignmentType { get; set; }
        public string? Route { get; set; }
        public string Status { get; set; } = string.Empty;
        public string StatusLabel { get; set; } = string.Empty;
        public string? ReceiverName { get; set; }
        public int ItemCount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public List<PublicTrackingParcelDto> Parcels { get; set; } = new();
    }

    /// <summary>Trạng thái một kiện hàng thuộc đơn ký gửi trong kết quả tra cứu công khai.</summary>
    public class PublicTrackingParcelDto
    {
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
        public DateTime? CheckInTime { get; set; }
        public DateTime? PutAwayTime { get; set; }
    }

}
