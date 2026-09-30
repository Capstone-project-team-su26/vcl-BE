using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class WarehouseReceivingNoteExpectedItemDto
    {
        /// <summary>Thùng gỗ của kiện — kho đối chiếu đúng loại thùng.</summary>
        public PackageConfigurationResponseDto? PackageConfiguration { get; set; }

        /// <summary>Dịch vụ khách chọn cho kiện (kiểm hàng, bảo hiểm...).</summary>
        public List<ItemServiceDto> Services { get; set; } = new();

        /// <summary>
        /// Dòng hàng gốc trên đơn. Kho gửi lại đúng id này khi chốt số thực tế để hệ thống so
        /// được với khai báo.
        /// </summary>
        /// <remarks>
        /// Thiếu field này thì màn kiểm đếm không có gì để tham chiếu ngược, mọi dòng bị coi là
        /// hàng phát sinh ngoài khai báo — <c>declaredQuantity</c> ghi 0 và biên bản báo lệch
        /// toàn bộ dù hàng về đúng.
        /// </remarks>
        public Guid? OrderItemId { get; set; }

        public string ProductName { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int Quantity { get; set; }

        /// <summary>Cân nặng khách khai, để kho biết mình đang lệch bao nhiêu so với cái gì.</summary>
        public decimal? Weight { get; set; }

        public decimal? DeclaredValue { get; set; }
    }

    /// <summary>
    /// Kết quả scan phiếu tiếp nhận kho (VCL-146) — thông tin để Warehouse Staff nhận hàng.
    /// </summary>
    public class WarehouseReceivingNoteScanResponseDto
    {
        public Guid ReceivingNoteId { get; set; }
        public string ReceivingNoteCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public Guid ConsignmentOrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;

        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;

        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string WarehouseNote { get; set; } = string.Empty;

        /// <summary>Nhãn trạng thái tiếng Việt (giống danh sách/chi tiết) để app không tự map.</summary>
        public string StatusText { get; set; } = string.Empty;

        /// <summary><c>CONSIGNMENT</c> (ký gửi) · <c>PURCHASE</c> (mua hộ — NCC giao tới kho).</summary>
        public string OrderType { get; set; } = "CONSIGNMENT";

        /// <summary>Phiếu của hàng mua hộ: NCC giao tới kho, phiếu tự mở khi Sale đặt NCC.</summary>
        public bool IsPurchase { get; set; }

        /// <summary>Mã yêu cầu mua hộ của khách (PUR-…). Rỗng với ký gửi.</summary>
        public string? PurchaseCode { get; set; }

        /// <summary>Mã đơn mua NCC nội bộ (PO-…). Rỗng với ký gửi / mua hộ đời cũ.</summary>
        public string? PurchaseOrderCode { get; set; }

        /// <summary>Tên NCC — chỉ nội bộ, ẩn với khách.</summary>
        public string? SupplierName { get; set; }

        /// <summary>Mã đơn thật bên NCC (Taobao/1688/Shopee…) — thứ in trên kiện NCC giao. Ẩn với khách.</summary>
        public string? SupplierOrderCode { get; set; }

        /// <summary>Hãng vận chuyển nội địa NCC dùng (khi NCC đã phát hàng).</summary>
        public string? DomesticCarrier { get; set; }

        /// <summary>Mã vận đơn nội địa NCC → kho (khi NCC đã phát hàng).</summary>
        public string? DomesticTrackingCode { get; set; }

        public List<WarehouseReceivingNoteExpectedItemDto> ExpectedItems { get; set; } = new();
    }

    public class WarehouseReceivingNoteResponseDto
    {
        public Guid Id { get; set; }
        public string ReceivingNoteCode { get; set; } = string.Empty;
        public Guid ConsignmentOrderId { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string WarehouseNote { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class CreateWarehouseReceivingNoteRequestDto
    {
        public Guid ConsignmentOrderId { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseNote { get; set; } = string.Empty;
    }

    /// <summary>Số liệu thực tế nhân viên kho ghi nhận cho một dòng hàng</summary>
    public class ConfirmReceiptItemDto
    {
        /// <summary>Dòng hàng khai báo tương ứng, để trống nếu là hàng phát sinh ngoài khai báo</summary>
        public Guid? OrderItemId { get; set; }

        /// <summary>Bắt buộc khi không có OrderItemId</summary>
        public string ProductName { get; set; } = string.Empty;

        public string ProductType { get; set; } = string.Empty;

        /// <summary>Số lượng đếm thực tế tại kho</summary>
        public int ActualQuantity { get; set; }

        /// <summary>Trọng lượng cân thực tế tại kho, đơn vị kg</summary>
        public decimal? ActualWeight { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Yêu cầu xác nhận nhập kho kèm số liệu thực tế từng dòng hàng</summary>
    /// <summary>
    /// Một kiện hàng thật kho cân đo được lúc nhận.
    /// </summary>
    /// <remarks>
    /// Dòng hàng của phiếu là thứ khách KHAI (tên hàng, số lượng); còn kiện là thùng hàng THẬT
    /// nằm trên bàn cân, có kích thước để tính cước theo thể tích. Hai thứ khác nhau nên tách
    /// riêng, nhưng kho khai cả hai trong cùng một lần chốt — cân xong đo luôn, không phải mở
    /// thêm màn check-in nữa.
    /// </remarks>
    public class ConfirmReceiptParcelDto
    {
        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }

        /// <summary>Dòng hàng của đơn mà kiện này chứa, bỏ trống cũng được.</summary>
        public Guid? OrderItemId { get; set; }
    }

    public class ConfirmReceiptRequestDto
    {
        public string WarehouseNote { get; set; } = string.Empty;

        /// <summary>
        /// Kiện thật kho nhận được. Bỏ trống thì phiếu vẫn chốt bình thường, chỉ là chưa có kiện
        /// nào để xếp kệ.
        /// </summary>
        public List<ConfirmReceiptParcelDto>? Parcels { get; set; }

        public List<ConfirmReceiptItemDto> Items { get; set; } = new List<ConfirmReceiptItemDto>();
    }

    /// <summary>Dòng chi tiết biên bản nhập kho, đối chiếu khai báo với thực tế</summary>
    public class WarehouseReceivingNoteItemDto
    {
        public Guid Id { get; set; }
        public Guid? OrderItemId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int DeclaredQuantity { get; set; }
        public int ActualQuantity { get; set; }

        /// <summary>Âm là thiếu so với khai báo, dương là thừa</summary>
        public int QuantityDifference { get; set; }

        public decimal? DeclaredWeight { get; set; }
        public decimal? ActualWeight { get; set; }
        public decimal? WeightDifference { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Biên bản nhập kho đầy đủ, gồm phần đầu phiếu và các dòng đối chiếu</summary>
    public class WarehouseReceivingNoteDetailDto : WarehouseReceivingNoteResponseDto
    {
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;

        /// <summary>Tuyến của đơn, để in lên phiếu cho kho biết hàng đi đâu.</summary>
        public string Route { get; set; } = string.Empty;

        /// <summary><c>CONSIGNMENT</c> (ký gửi) · <c>PURCHASE</c> (mua hộ — NCC giao tới kho).</summary>
        public string OrderType { get; set; } = "CONSIGNMENT";

        /// <summary>Phiếu của hàng mua hộ: NCC giao tới kho, phiếu tự mở khi Sale đặt NCC.</summary>
        public bool IsPurchase { get; set; }

        /// <summary>Mã yêu cầu mua hộ của khách (PUR-…). Rỗng với ký gửi.</summary>
        public string? PurchaseCode { get; set; }

        /// <summary>Mã đơn mua NCC nội bộ (PO-…). Rỗng với ký gửi / mua hộ đời cũ.</summary>
        public string? PurchaseOrderCode { get; set; }

        /// <summary>Tên NCC — chỉ nội bộ, ẩn với khách.</summary>
        public string? SupplierName { get; set; }

        /// <summary>Mã đơn thật bên NCC (Taobao/1688/Shopee…) — thứ in trên kiện NCC giao. Ẩn với khách.</summary>
        public string? SupplierOrderCode { get; set; }

        /// <summary>Hãng vận chuyển nội địa NCC dùng (khi NCC đã phát hàng).</summary>
        public string? DomesticCarrier { get; set; }

        /// <summary>Mã vận đơn nội địa NCC → kho (khi NCC đã phát hàng).</summary>
        public string? DomesticTrackingCode { get; set; }

        /// <summary>
        /// Hàng KHÁCH KHAI BÁO trên đơn. Có ngay từ lúc phiếu vừa lập — đây là thứ kho cầm phiếu ra
        /// đối chiếu với hàng thật. Khác <see cref="Items"/> ở chỗ Items chỉ sinh ra sau khi kho
        /// bấm xác nhận nhận hàng, nên phiếu mới lập thì Items rỗng còn danh sách này đã có.
        /// </summary>
        public List<WarehouseReceivingNoteExpectedItemDto> ExpectedItems { get; set; } = new();

        /// <summary>True khi có bất kỳ dòng nào lệch số lượng hoặc trọng lượng</summary>
        public bool HasDiscrepancy { get; set; }

        /// <summary>Dòng đối chiếu khai báo với thực tế — rỗng cho tới khi kho xác nhận nhận hàng.</summary>
        public List<WarehouseReceivingNoteItemDto> Items { get; set; } = new List<WarehouseReceivingNoteItemDto>();

        /// <summary>Kiện thật đã nhận của đơn này, kèm cân nặng và kích thước kho đo được.</summary>
        public List<ReceivingNoteParcelDto> Parcels { get; set; } = new();

        public string ApprovedByName { get; set; } = string.Empty;
        public DateTime? ApprovedAt { get; set; }

        /// <summary>Lý do OM từ chối, null khi phiếu không bị từ chối.</summary>
        public string? RejectionReason { get; set; }

        /// <summary>
        /// Kho được phép xếp hàng lên kệ chưa. Chốt thật nằm ở
        /// <c>ParcelService.PutAwayParcelAsync</c>; field này để màn hình biết đường bật/tắt nút.
        /// </summary>
        public bool CanPutAway { get; set; }

        /// <summary>Sale lập phiếu.</summary>
        public string CreatedByName { get; set; } = string.Empty;

        /// <summary>Quản lý kho cho phép nhận hàng theo phiếu. Rỗng nghĩa là phiếu chưa được duyệt.</summary>
        public string ReceiveApprovedByName { get; set; } = string.Empty;
        public DateTime? ReceiveApprovedAt { get; set; }
        public string? ApprovalNote { get; set; }

        public string ReceivedByName { get; set; } = string.Empty;
        public DateTime? ReceivedAt { get; set; }

        /// <summary>Biên bản có chênh lệch số lượng hoặc hàng ngoài khai báo, cần quản lý kho xem lại.</summary>
        public bool RequiresReview { get; set; }

        /// <summary>Link phiếu PDF khách mang tới kho, phát hành khi quản lý kho duyệt.</summary>
        public string? ReceiptPdfUrl { get; set; }

        /// <summary>Nhãn trạng thái đã tính cả bước chờ quyết lệch (giống danh sách).</summary>
        public string StatusText { get; set; } = string.Empty;

        /// <summary>Phiếu đang chờ quản lý kho quyết định (giống danh sách).</summary>
        public bool AwaitingApproval { get; set; }

        /// <summary><c>RECEIVE</c> · <c>DISCREPANCY</c> · <c>DISCREPANCY_ACK</c>; null khi không chờ ai.</summary>
        public string? ApprovalStage { get; set; }
    }

    /// <summary>
    /// Một dòng trong danh sách phiếu tiếp nhận — đủ để kho chọn việc và OM quyết duyệt hay không
    /// mà không phải mở từng phiếu.
    /// </summary>
    public class WarehouseReceivingNoteListItemDto
    {
        public Guid Id { get; set; }
        public string ReceivingNoteCode { get; set; } = string.Empty;

        public Guid ConsignmentOrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;

        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;

        /// <summary><c>CONSIGNMENT</c> (ký gửi) · <c>PURCHASE</c> (mua hộ — NCC giao tới kho).</summary>
        public string OrderType { get; set; } = "CONSIGNMENT";

        /// <summary>Phiếu của hàng mua hộ: NCC giao tới kho, phiếu tự mở khi Sale đặt NCC.</summary>
        public bool IsPurchase { get; set; }

        /// <summary>Mã yêu cầu mua hộ của khách (PUR-…). Rỗng với ký gửi.</summary>
        public string? PurchaseCode { get; set; }

        /// <summary>Mã đơn mua NCC nội bộ (PO-…). Rỗng với ký gửi / mua hộ đời cũ.</summary>
        public string? PurchaseOrderCode { get; set; }

        /// <summary>Tên NCC — chỉ nội bộ, ẩn với khách.</summary>
        public string? SupplierName { get; set; }

        /// <summary>Mã đơn thật bên NCC (Taobao/1688/Shopee…) — thứ in trên kiện NCC giao. Ẩn với khách.</summary>
        public string? SupplierOrderCode { get; set; }

        /// <summary>Hãng vận chuyển nội địa NCC dùng (khi NCC đã phát hàng).</summary>
        public string? DomesticCarrier { get; set; }

        /// <summary>Mã vận đơn nội địa NCC → kho (khi NCC đã phát hàng).</summary>
        public string? DomesticTrackingCode { get; set; }

        /// <summary>ACTIVE · PARTIALLY_RECEIVED · RECEIVED · APPROVED · REJECTED.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Nhãn tiếng Việt để FE hiện thẳng, khỏi tự map.</summary>
        public string StatusText { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public string ApprovedByName { get; set; } = string.Empty;
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }

        /// <summary>Số dòng hàng khách khai trên đơn.</summary>
        public int DeclaredItemCount { get; set; }

        /// <summary>Số dòng kho đã cân đếm lại.</summary>
        public int CheckedItemCount { get; set; }

        /// <summary>Có dòng nào lệch số lượng hoặc trọng lượng không — cái OM cần nhìn trước tiên.</summary>
        public bool HasDiscrepancy { get; set; }

        /// <summary>Số kiện đã check-in cho đơn này.</summary>
        public int ParcelCount { get; set; }

        /// <summary>Phiếu đang chờ OM quyết, dùng để FE bật/tắt nút duyệt.</summary>
        public bool AwaitingApproval { get; set; }

        /// <summary>
        /// <c>RECEIVE</c>: chờ duyệt cho nhận hàng · <c>DISCREPANCY</c>: lệch số lượng sau kiểm đếm, chặn xếp kệ ·
        /// <c>DISCREPANCY_ACK</c>: đã tự chốt (số lượng khớp) nhưng lệch cân, chờ chấp nhận số thực tế — không chặn xếp kệ.
        /// Quyết định cả ba bước qua <c>PUT /api/warehouse-receiving-notes/{id}/status</c>; bước DISCREPANCY_ACK chỉ nhận APPROVED.
        /// </summary>
        public string? ApprovalStage { get; set; }
    }

    /// <summary>Kiện thật của đơn, hiện trên phiếu để đối chiếu và theo dõi.</summary>
    public class ReceivingNoteParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;

        /// <summary>RECEIVED · STORED · IN_TRANSIT…</summary>
        public string PackageStatus { get; set; } = string.Empty;
        public string PackageStatusText { get; set; } = string.Empty;

        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }

        /// <summary>Thể tích quy đổi (cm³) — kho nhìn để biết kiện chiếm bao nhiêu chỗ trên kệ.</summary>
        public decimal? Volume { get; set; }

        /// <summary>Ô kệ đang chứa kiện, rỗng khi chưa xếp.</summary>
        public string BinCode { get; set; } = string.Empty;
        public DateTime? CheckInTime { get; set; }
    }

    /// <summary>OM duyệt hoặc từ chối biên bản đối chiếu của phiếu tiếp nhận.</summary>
    public class UpdateReceivingNoteStatusDto
    {
        /// <summary><c>APPROVED</c> hoặc <c>REJECTED</c>.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Bắt buộc khi từ chối.</summary>
        public string? RejectionReason { get; set; }

        /// <summary>Ghi chú duyệt. Bắt buộc khi Admin quyết định thay quản lý kho.</summary>
        public string? Reason { get; set; }
    }
}
