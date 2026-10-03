using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using VCL_BLL.Helpers;

namespace VCL_BLL.DTOs
{
    public class CreatePurchaseRequestItemDto
    {
        [Required(ErrorMessage = "Link sản phẩm không được để trống.")]
        [StringLength(InputLimits.ProductLinkLength, ErrorMessage = "Link sản phẩm tối đa 1000 ký tự.")]
        public string ProductLink { get; set; } = string.Empty;

        /// <summary>Website/sàn nguồn sản phẩm — tùy tuyến (VD: Taobao, 1688, Amazon, Coupang, Rakuten).</summary>
        [StringLength(InputLimits.SourceWebsiteLength, ErrorMessage = "Website nguồn tối đa 500 ký tự.")]
        public string? SourceWebsite { get; set; }

        /// <summary>Loại hàng (lấy từ dropdown /api/product-types) — dùng để đối chiếu hàng cấm/hạn chế.</summary>
        [Required(ErrorMessage = "Vui lòng chọn loại hàng.")]
        [StringLength(InputLimits.ProductTypeLength, ErrorMessage = "Loại hàng tối đa 100 ký tự.")]
        public string ProductType { get; set; } = string.Empty;

        [StringLength(InputLimits.ProductNameLength, ErrorMessage = "Tên sản phẩm tối đa 255 ký tự.")]
        public string? ProductName { get; set; }

        // Hạn mức nghiệp vụ (mặc định 999) do Admin cấu hình — service kiểm; ở đây chỉ chặn số vô lý.
        [Range(1, InputLimits.HardMaxPurchaseItemQuantity, ErrorMessage = "Số lượng mỗi sản phẩm phải từ 1 đến 1.000.000.")]
        public int Quantity { get; set; }

        /// <summary>Thuộc tính sản phẩm như size, màu, mẫu mã (nếu có).</summary>
        [StringLength(InputLimits.AttributesLength, ErrorMessage = "Thuộc tính sản phẩm tối đa 500 ký tự.")]
        public string? Attributes { get; set; }

        /// <summary>Ghi chú riêng cho từng sản phẩm (nếu có).</summary>
        [StringLength(InputLimits.ItemNoteLength, ErrorMessage = "Ghi chú sản phẩm tối đa 500 ký tự.")]
        public string? Note { get; set; }

        /// <summary>Danh sách hình ảnh tham khảo (upload qua POST /api/uploads/images).</summary>
        [MaxLength(InputLimits.MaxImagesPerPurchaseItem, ErrorMessage = "Mỗi sản phẩm tối đa 5 ảnh tham khảo.")]
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
        [StringLength(InputLimits.ReceiverNameLength, ErrorMessage = "Tên người nhận tối đa 255 ký tự.")]
        public string? ReceiverName { get; set; }

        /// <summary>Số điện thoại người nhận.</summary>
        [StringLength(InputLimits.ReceiverPhoneLength, ErrorMessage = "Số điện thoại người nhận tối đa 50 ký tự.")]
        public string? ReceiverPhone { get; set; }

        /// <summary>Địa chỉ nhận hàng.</summary>
        [StringLength(InputLimits.ReceiverAddressLength, ErrorMessage = "Địa chỉ nhận hàng tối đa 500 ký tự.")]
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

        /// <summary>
        /// Ghi chú mua hộ chung cho cả yêu cầu (nếu có). Lưu kèm tên các dịch vụ khách tick (xem
        /// BuildGeneralNote) nên tổng sau khi ghép còn phải ≤ 1000 ký tự — service kiểm lại.
        /// </summary>
        [StringLength(InputLimits.GeneralNoteLength, ErrorMessage = "Ghi chú chung tối đa 1000 ký tự.")]
        public string? GeneralNote { get; set; }

        [Required(ErrorMessage = "Danh sách sản phẩm cần mua hộ phải có ít nhất một sản phẩm.")]
        [MinLength(1, ErrorMessage = "Danh sách sản phẩm cần mua hộ phải có ít nhất một sản phẩm.")]
        // Hạn mức nghiệp vụ (mặc định 50 dòng) do Admin cấu hình — service kiểm; ở đây là trần cứng.
        [MaxLength(InputLimits.HardMaxItemsPerRequest, ErrorMessage = "Mỗi yêu cầu mua hộ tối đa 500 dòng sản phẩm.")]
        public List<CreatePurchaseRequestItemDto> Items { get; set; } = new();
    }

    public class PurchaseRequestItemResponseDto
    {
        public Guid ItemId { get; set; }
        public string ProductLink { get; set; } = string.Empty;
        public string? SourceWebsite { get; set; }
        public string? ProductType { get; set; }
        /// <summary>Tên loại hàng tra từ PRODUCT_TYPES (cột ProductType có thể là Id). Null khi chưa phân loại.</summary>
        public string? ProductTypeName { get; set; }
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

        /// <summary>Nhãn tiếng Việt của <see cref="Status"/>, FE hiển thị thẳng.</summary>
        public string StatusDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// CHẶNG THẬT đầu–cuối (VCL_BLL.Helpers.PurchaseRequestStage): AWAITING_QUOTE, NEED_MORE_INFO,
        /// AWAITING_PAYMENT, PREPAID, ORDERING_SUPPLIER, IN_TRANSIT, ARRIVED_VN, DELIVERED, AWAITING_REFUND,
        /// COMPLETED, CANCELLED, REJECTED. Suy từ đơn mua NCC + đơn kho + khoản hoàn — khác <see cref="Status"/>
        /// (COMPLETED của Status có thể chỉ là tất toán đợt cuối đời cũ).
        /// </summary>
        public string OverallStage { get; set; } = string.Empty;

        /// <summary>Nhãn tiếng Việt của <see cref="OverallStage"/>.</summary>
        public string OverallStageText { get; set; } = string.Empty;

        /// <summary>True khi mọi phần hàng đã giao / đóng đơn và không còn khoản hoàn chờ chuyển.</summary>
        public bool IsFullyCompleted { get; set; }

        /// <summary>Kho dự kiến nhận hàng Sale đã chọn ở bước xác nhận mua hàng. Null nếu chưa chọn.</summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>Tên kho tra từ DB theo <see cref="WarehouseId"/>.</summary>
        public string? WarehouseName { get; set; }

        /// <summary>Ảnh bằng chứng mua hộ đã tải lên (hoá đơn / màn hình đã đặt hàng).</summary>
        public List<string> ProofImages { get; set; } = new();

        /// <summary>
        /// Mã đơn kho sinh khi duyệt nhập kho (`PUR-…`). Null nếu đơn chưa được nhập kho.
        /// Cùng nghĩa với <c>orderCode</c> ở các API kho.
        /// </summary>
        public string? OrderCode { get; set; }

        /// <summary>
        /// Link phiếu nhập kho (PDF). Null nếu đơn chưa nhập kho hoặc sinh phiếu lỗi.
        /// <para>
        /// Có ở đây để phiếu tra lại được bất cứ lúc nào. Trước đây link chỉ xuất hiện đúng một lần
        /// trong response của bước duyệt nhập kho — FE không lưu lại thì coi như mất, dù file vẫn còn.
        /// Link ổn định, sinh lại phiếu cũng không đổi URL.
        /// </para>
        /// </summary>
        public string? ReceiptPdfUrl { get; set; }

        /// <summary>Thời điểm phát hành phiếu. Null nếu chưa có phiếu.</summary>
        public DateTime? ReceiptGeneratedAt { get; set; }

        public int TotalQuantity { get; set; }
        public List<PurchaseRequestItemResponseDto> Items { get; set; } = new();
        public PurchaseQuotationResponseDto? Quotation { get; set; }

        /// <summary>
        /// Hàng của đơn đang đi theo lô nào và tới đâu rồi. Rỗng khi đơn chưa đóng kiện.
        /// Dùng chung cấu trúc với đơn ký gửi để hai màn hiển thị y hệt nhau.
        /// </summary>
        public List<ConsignmentShipmentGroup> Shipments { get; set; } = new();

        /// <summary>Số kiện của đơn bị kho Việt Nam ghi nhận lệch khi kiểm đếm.</summary>
        public int DiscrepancyParcelCount { get; set; }
    }

    /// <summary>
    /// Xác nhận mua hộ &amp; cập nhật tiến độ — theo bản tả kỹ thuật của FE.
    /// Dùng cho nhóm trạng thái đi mua hàng, tách khỏi luồng duyệt yêu cầu
    /// (<see cref="UpdatePurchaseRequestStatusRequestDto"/>) để không đá nhau.
    /// </summary>
    public class ConfirmPurchaseRequestDto
    {
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        /// <summary>URL ảnh hoá đơn / màn hình đã đặt hàng. Tối đa 3 ảnh.</summary>
        public List<string>? ProofImages { get; set; }

        [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
        public string? GeneralNote { get; set; }

        /// <summary>Kho dự kiến nhận hàng, Sale chọn ở bước 1.</summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>FE gửi kèm cùng giá trị với <see cref="WarehouseId"/> — nhận để không phụ thuộc thứ tự field.</summary>
        public Guid? DestinationWarehouseId { get; set; }

        /// <summary>Tên kho FE gửi kèm; BE bỏ qua và tự tra theo Id để tránh lệch dữ liệu.</summary>
        public string? WarehouseName { get; set; }
    }

    /// <summary>Lý do khách từ chối báo giá mua hộ. Bắt buộc — Sale cần biết vì sao để chỉnh giá.</summary>
    public class RejectPurchaseQuotationRequestDto
    {
        [Required(ErrorMessage = "Vui lòng nhập lý do từ chối báo giá.")]
        [StringLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")]
        public string RejectionReason { get; set; } = string.Empty;
    }

    public class ApproveStoreRequestDto
    {
        [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
        public string? Note { get; set; }

        /// <summary>Kho thực tế đã nhập hàng. Bỏ trống thì lấy kho dự kiến đã chọn trước đó.</summary>
        public Guid? WarehouseId { get; set; }
    }

    public class ConfirmPurchaseResponseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        /// <summary>Nhãn tiếng Việt của trạng thái, FE hiển thị thẳng khỏi tự map.</summary>
        public string StatusDisplayName { get; set; } = string.Empty;

        public List<string> ProofImages { get; set; } = new();
        public string? GeneralNote { get; set; }

        /// <summary>Kho dự kiến nhận hàng đã lưu (null nếu chưa chọn).</summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>Tên kho tra từ DB theo <see cref="WarehouseId"/>.</summary>
        public string? WarehouseName { get; set; }

        /// <summary>
        /// Mã đơn kho sinh ra khi duyệt nhập kho — nhân viên kho dùng mã này để check-in cân đo.
        /// Chỉ có giá trị khi đơn chuyển sang <c>STORED</c>, qua <c>approve-store</c> hoặc
        /// <c>confirm-purchase</c>; các bước khác trả null.
        /// <para>
        /// Cùng tên và cùng nghĩa với <c>orderCode</c> bên ParcelDtos — mã đơn kho không phân biệt loại.
        /// Ở luồng mua hộ nó trùng <see cref="PurchaseCode"/>. KHÔNG đặt tên là <c>consignmentCode</c>:
        /// đơn mua hộ không có mã ký gửi, và cột DB <c>ORDERS.consignment_code</c> chỉ là tên cũ có từ
        /// thời hệ thống mới chỉ chạy ký gửi — không để tên cột đó lộ ra hợp đồng API.
        /// </para>
        /// </summary>
        public string? OrderCode { get; set; }

        /// <summary>Link phiếu nhập kho mua hộ (PDF), phát hành khi OM duyệt. Null nếu sinh phiếu lỗi.</summary>
        public string? ReceiptPdfUrl { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public class PurchaseRequestHistoryItemDto
    {
        public Guid HistoryId { get; set; }
        public string? FromStatus { get; set; }
        public string ToStatus { get; set; } = string.Empty;
        public string ToStatusDisplayName { get; set; } = string.Empty;
        public string? Note { get; set; }
        public List<string> ProofImages { get; set; } = new();
        public Guid? ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
        public DateTime? CreatedAt { get; set; }
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
        /// <summary>
        /// Đồng tiền của <c>Items[].UnitPrice</c> — CNY, USD, JPY... Bỏ trống hoặc "VND" là không quy đổi.
        /// Chỉ áp cho giá sản phẩm; <see cref="PurchaseFee"/>, <see cref="ShippingFee"/> và phụ phí
        /// luôn nhập bằng VND vì đó là phí của công ty, không phải giá mua ở nước ngoài.
        /// </summary>
        [StringLength(10)]
        public string? Currency { get; set; }

        /// <summary>Phí mua hộ.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Phí mua hộ không được âm.")]
        public decimal PurchaseFee { get; set; }

        /// <summary>
        /// Không còn dùng: cước quốc tế tạm tính = <see cref="EstimatedWeight"/> × <see cref="FreightRatePerKg"/>.
        /// Giữ để FE cũ gửi lên không bị lỗi.
        /// </summary>
        [Range(0, double.MaxValue, ErrorMessage = "Phí vận chuyển không được âm.")]
        public decimal ShippingFee { get; set; }

        /// <summary>Ship nội địa từ NCC tới kho nguồn (VND) — khách trả trước.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Ship nội địa không được âm.")]
        public decimal DomesticShippingFee { get; set; }

        /// <summary>Đơn giá cước quốc tế /kg (VND). Bỏ trống thì lấy theo bảng giá khớp tuyến.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Đơn giá cước không được âm.")]
        public decimal? FreightRatePerKg { get; set; }

        /// <summary>Bảng giá cước Sale chọn (tuỳ chọn).</summary>
        public Guid? ServicePricingId { get; set; }

        /// <summary>Cân ước tính (kg). Bỏ trống thì 0,2 kg / món, tối thiểu 1 kg.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Cân ước tính không được âm.")]
        public decimal? EstimatedWeight { get; set; }

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

        /// <summary>Đơn giá đã quy đổi sang VND.</summary>
        public decimal UnitPrice { get; set; }

        /// <summary>Đơn giá gốc theo ngoại tệ Sale đã nhập. Null khi nhập thẳng VND.</summary>
        public decimal? UnitPriceOriginal { get; set; }

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

        /// <summary>Cước quốc tế TẠM TÍNH (thu ở VN theo cân đo thật).</summary>
        public decimal ShippingFee { get; set; }

        /// <summary>Ship nội địa NCC → kho nguồn (trả trước).</summary>
        public decimal DomesticShippingFee { get; set; }

        public decimal? FreightRatePerKg { get; set; }
        public decimal? EstimatedWeight { get; set; }
        public decimal? VatRate { get; set; }

        /// <summary>Tổng VAT (phần phí trả trước + phần cước tạm tính).</summary>
        public decimal Vat { get; set; }

        /// <summary>Thuế nhập khẩu TẠM TÍNH (thu ở VN, tính lại theo giá mua thật).</summary>
        public decimal ImportTax { get; set; }

        /// <summary>Tổng dự kiến = <see cref="PrepayAmount"/> + <see cref="EstimatedLaterAmount"/>.</summary>
        public decimal TotalAmount { get; set; }

        /// <summary>Báo giá đời cũ (thu cọc theo tỷ lệ) — không có phần trả trước tách riêng.</summary>
        public bool IsLegacy { get; set; }

        /// <summary>Khách trả 100% khi chấp nhận: tiền hàng + phí mua + ship nội địa + phụ phí + VAT phần phí.</summary>
        public decimal PrepayAmount { get; set; }

        /// <summary>Tạm tính thu ở VN: cước quốc tế + VAT cước + thuế nhập khẩu.</summary>
        public decimal EstimatedLaterAmount { get; set; }

        /// <summary>
        /// Số khách trả khi xác nhận báo giá — ĐÚNG số confirm-and-pay tạo khoản thu (= PrepayAmount).
        /// FE hiện số này, không tự tính cọc. Báo giá đời cũ: 0 (không thanh toán online được).
        /// </summary>
        public decimal DepositAmount { get; set; }

        /// <summary>Phần còn lại sau khoản trả trước (tạm tính, thu ở VN theo cân đo thật).</summary>
        public decimal RemainingAmount { get; set; }

        /// <summary>Diễn giải khoản trả trước (hoặc lý do không thanh toán được với báo giá đời cũ).</summary>
        public string DepositDescription { get; set; } = string.Empty;

        /// <summary>false: báo giá đời cũ — confirm-and-pay sẽ từ chối, khách liên hệ Sale lập lại báo giá.</summary>
        public bool CanPayOnline { get; set; }

        public string? Note { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        /// <summary>Đồng tiền Sale đã nhập giá sản phẩm. "VND" nghĩa là không quy đổi.</summary>
        public string? Currency { get; set; }

        /// <summary>Tỷ giá đã áp dụng, khoá cứng theo báo giá này — không đổi khi Admin sửa bảng tỷ giá.</summary>
        public decimal? ExchangeRate { get; set; }

        public DateTime? ExchangeRateAt { get; set; }

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

        /// <summary>Nhãn tiếng Việt của <see cref="Status"/>, FE hiển thị thẳng.</summary>
        public string StatusDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// CHẶNG THẬT đầu–cuối (VCL_BLL.Helpers.PurchaseRequestStage): AWAITING_QUOTE, NEED_MORE_INFO,
        /// AWAITING_PAYMENT, PREPAID, ORDERING_SUPPLIER, IN_TRANSIT, ARRIVED_VN, DELIVERED, AWAITING_REFUND,
        /// COMPLETED, CANCELLED, REJECTED. Suy từ đơn mua NCC + đơn kho + khoản hoàn — khác <see cref="Status"/>
        /// (COMPLETED của Status có thể chỉ là tất toán đợt cuối đời cũ).
        /// </summary>
        public string OverallStage { get; set; } = string.Empty;

        /// <summary>Nhãn tiếng Việt của <see cref="OverallStage"/>.</summary>
        public string OverallStageText { get; set; } = string.Empty;

        /// <summary>True khi mọi phần hàng đã giao / đóng đơn và không còn khoản hoàn chờ chuyển.</summary>
        public bool IsFullyCompleted { get; set; }

        /// <summary>Kho dự kiến nhận hàng Sale đã chọn. Null nếu chưa chọn.</summary>
        public Guid? WarehouseId { get; set; }

        public string? WarehouseName { get; set; }

        /// <summary>Tên / mã / SĐT khách (hồ sơ Customer) — web Sale không phải gọi thêm danh bạ khách.</summary>
        public string? CustomerName { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerPhone { get; set; }

        /// <summary>Số đơn mua NCC chưa huỷ của yêu cầu.</summary>
        public int ActivePurchaseOrderCount { get; set; }

        /// <summary>
        /// Số dòng sản phẩm còn đưa được vào một đơn mua MỚI — đúng luật PurchaseOrderService.ApplyContentAsync:
        /// có trong báo giá khách đã chấp nhận, chưa nằm trong đơn mua nào chưa huỷ, và
        /// SL khách đặt − SL đã đóng "không mua được" &gt; 0. Chưa có báo giá ACCEPTED thì 0.
        /// </summary>
        public int OpenLineCount { get; set; }

        /// <summary>Tổng số lượng còn mua được của các dòng tính trong <see cref="OpenLineCount"/>.</summary>
        public int OpenQuantity { get; set; }

        /// <summary>Tổng số lượng đã đóng "không mua được" (đã/đang hoàn tiền cho khách).</summary>
        public int ClosedQuantity { get; set; }

        /// <summary>Tiền trả trước đã thu (khoản PREPAYMENT, hoặc cọc/trả một lần đời cũ, trạng thái PAID).</summary>
        public decimal PrepaidAmount { get; set; }

        public List<PurchaseRequestListProductDto> Items { get; set; } = new();
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

        /// <summary>PREPAYMENT | PRICE_DIFFERENCE (đời cũ: DEPOSIT | FULL_PAYMENT | FINAL_PAYMENT).</summary>
        public string PaymentType { get; set; } = string.Empty;

        /// <summary>Đơn mua NCC của khoản chênh giá.</summary>
        public Guid? PurchaseOrderId { get; set; }

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

        /// <summary>Tiền hàng — khách trả 100% ngay đợt cọc.</summary>
        public decimal ProductsSubtotal { get; set; }

        /// <summary>Phần dịch vụ &amp; cước (tổng bill trừ tiền hàng).</summary>
        public decimal ServicesSubtotal { get; set; }

        /// <summary>Tiền cọc đợt đầu theo công thức (tiền hàng + 50% dịch vụ).</summary>
        public decimal DepositAmount { get; set; }

        /// <summary>Diễn giải cách tính cọc để FE hiển thị.</summary>
        public string DepositDescription { get; set; } = string.Empty;

        /// <summary>Báo giá đời cũ (thu cọc theo tỷ lệ).</summary>
        public bool IsLegacy { get; set; }

        /// <summary>Luồng mới: phần tạm tính (cước quốc tế + VAT + thuế NK) thu ở VN theo cân đo thật.</summary>
        public decimal EstimatedLaterAmount { get; set; }

        public List<PurchasePaymentHistoryItemDto> Payments { get; set; } = new();

        /// <summary>Tổng đã chuyển trả khách.</summary>
        public decimal TotalRefunded { get; set; }

        /// <summary>Tổng đang chờ chuyển trả khách.</summary>
        public decimal TotalPendingRefund { get; set; }

        /// <summary>Các khoản hoàn kèm chi tiết từng dòng (tiền hàng, chênh giá, phí, VAT, phí huỷ).</summary>
        public List<PurchaseRefundDto> Refunds { get; set; } = new();
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

        /// <summary>Số tiền của đợt này — chính là <see cref="DepositAmount"/>.</summary>
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;

        /// <summary>Diễn giải cách tính cọc để FE hiển thị, ví dụ "100% tiền hàng + 50% phí dịch vụ &amp; cước".</summary>
        public string DepositDescription { get; set; } = string.Empty;

        /// <summary>Tổng bill của báo giá.</summary>
        public decimal TotalBillAmount { get; set; }

        /// <summary>Tiền hàng — khách trả 100% ngay đợt đầu.</summary>
        public decimal ProductsSubtotal { get; set; }

        /// <summary>Phần dịch vụ &amp; cước (tổng bill trừ tiền hàng) — gồm phí mua hộ, cước, thuế, phụ phí.</summary>
        public decimal ServicesSubtotal { get; set; }

        /// <summary>Phần dịch vụ khách trả ở đợt đầu (mặc định 50%).</summary>
        public decimal ServicesDepositAmount { get; set; }

        /// <summary>Tổng tiền cọc đợt đầu = tiền hàng + phần dịch vụ trả trước.</summary>
        public decimal DepositAmount { get; set; }

        /// <summary>Phần còn lại sẽ thu ở đợt cuối (chưa gồm chi phí phát sinh sau này).</summary>
        public decimal RemainingAmount { get; set; }

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
        public decimal FreightRatePerKg { get; set; }
        public decimal EstimatedWeight { get; set; }
        public Guid? ServicePricingId { get; set; }
        public decimal VatRate { get; set; }

        /// <summary>Phần trả trước dự kiến (chưa gồm phụ phí Sale thêm).</summary>
        public decimal EstimatedPrepayAmount { get; set; }

        /// <summary>Tổng dự kiến (trả trước + cước quốc tế + VAT; chưa gồm thuế nhập khẩu và phụ phí).</summary>
        public decimal TotalEstimatedCost { get; set; }
        public string? Note { get; set; }
    }

}

