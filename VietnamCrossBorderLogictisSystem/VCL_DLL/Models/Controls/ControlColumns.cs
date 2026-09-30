#nullable disable
using System;

namespace VCL_DLL.Models;

// Các cột kiểm soát bổ sung theo nghiệp vụ chuẩn (docs migrate luồng ký gửi). Tách khỏi file
// model sinh tự động để lần chạy EF Core Power Tools sau không ghi đè mất.

public partial class Warehouse
{
    /// <summary>
    /// Quản lý chịu trách nhiệm duyệt nhập/xuất tại kho này. Null trong giai đoạn chuyển đổi:
    /// khi chưa gán, quyền duyệt rơi về vai trò Operations Manager như trước.
    /// </summary>
    public Guid? ManagerId { get; set; }
}

public partial class Order
{
    public byte[] RowVersion { get; set; }
}

public partial class Payment
{
    /// <summary>Số tiền thực nhận do cổng thanh toán báo về hoặc Admin nhập khi đối soát.</summary>
    public decimal? ReceivedAmount { get; set; }
    public Guid? ConfirmedById { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>GATEWAY_PAYOS · GATEWAY_SEPAY · ADMIN_MANUAL · SYSTEM_ZERO · LEGACY.</summary>
    public string ConfirmationSource { get; set; }

    public byte[] RowVersion { get; set; }
}

public partial class Quotation
{
    public Guid? CreatedById { get; set; }
    public Guid? AcceptedById { get; set; }
    public DateTime? AcceptedAt { get; set; }

    /// <summary>NOT_REQUIRED · PENDING · APPROVED · REJECTED.</summary>
    public string PriceApprovalStatus { get; set; }
    public string OverrideReason { get; set; }
    public Guid? PriceApprovedById { get; set; }
    public DateTime? PriceApprovedAt { get; set; }
    public string PriceApprovalNote { get; set; }

    public byte[] RowVersion { get; set; }
}

public partial class QuotationFee
{
    /// <summary>Số tiền tính theo bảng giá khi Sales nhập số khác. Null nếu không ghi đè.</summary>
    public decimal? OriginalAmount { get; set; }
    public bool IsOverride { get; set; }

    /// <summary>Kiện (dòng hàng) chịu khoản phí này. Null với phí tính cho cả đơn.</summary>
    public Guid? OrderItemId { get; set; }
}

public partial class WarehouseReceivingNote
{
    public Guid? CreatedById { get; set; }

    /// <summary>Quản lý kho cho phép nhận hàng theo phiếu. Null nghĩa là phiếu chưa được duyệt.</summary>
    public Guid? ReceiveApprovedById { get; set; }
    public DateTime? ReceiveApprovedAt { get; set; }
    public string ApprovalNote { get; set; }

    public Guid? ReceivedById { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public byte[] RowVersion { get; set; }
}

public partial class Inventory
{
    public byte[] RowVersion { get; set; }
}

/// <summary>Lịch sử phân công quản lý kho. Quyết định duyệt cũ không đổi khi đổi quản lý.</summary>
public partial class WarehouseManagerHistory
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? ManagerId { get; set; }
    public Guid? PreviousManagerId { get; set; }
    public Guid? ChangedById { get; set; }
    public string Reason { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>Nhật ký chuyển trạng thái đơn: ai, lúc nào, từ đâu sang đâu, vì sao.</summary>
public partial class OrderStatusHistory
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string FromStatus { get; set; }
    public string ToStatus { get; set; }
    public string EventCode { get; set; }
    public string Note { get; set; }
    public Guid? ActorId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Dịch vụ khách chọn cho riêng một kiện (kiểm hàng, bảo hiểm, đóng lại carton...).</summary>
public partial class OrderItemService
{
    public Guid Id { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid PricingRuleId { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Luồng xuất kho quốc tế (docs/migrate-source-cu/06-luong-xuat-kho-quoc-te.md) ────────────

public partial class WroRequest
{
    /// <summary>Kho nguồn của phiếu — suy ra từ tuyến lúc lập, chốt lại để phân quyền theo vùng.</summary>
    public Guid? OriginWarehouseId { get; set; }
    public Guid? DestinationWarehouseId { get; set; }

    public Guid? SubmittedById { get; set; }
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Quản lý kho đã duyệt / từ chối. Khác người gửi duyệt.</summary>
    public Guid? DecidedById { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string DecisionNote { get; set; }

    /// <summary>Phiếu gốc khi container đầy phải tách phiếu.</summary>
    public Guid? SplitFromId { get; set; }

    /// <summary>Phiếu có từ trước khi bật luồng mới: không đòi giấy tờ mới.</summary>
    public bool IsLegacy { get; set; }

    public byte[] RowVersion { get; set; }
}

/// <summary>
/// Một kiện trong phiếu xuất: RESERVED → PICKED → HANDED_OVER (hoặc REMOVED khi bị bỏ khỏi phiếu).
/// Trạng thái xuất nằm ở đây chứ không ghi đè PARCELS.package_status, để màn kho, sức chứa ô và
/// kiểm kê vẫn đọc kiện như cũ cho tới lúc hàng rời kho.
/// </summary>
public partial class WroParcel
{
    public Guid Id { get; set; }
    public Guid WroRequestId { get; set; }
    public Guid ParcelId { get; set; }
    public string Status { get; set; }

    /// <summary>Ô kệ khu lưu kho trước khi bốc — dùng để trả kiện về đúng chỗ nếu bỏ khỏi phiếu.</summary>
    public Guid? SourceBinId { get; set; }

    /// <summary>Ô ở khu xuất kiện đang nằm sau khi bốc.</summary>
    public Guid? StagingBinId { get; set; }

    public Guid? PickedById { get; set; }
    public DateTime? PickedAt { get; set; }
    public Guid? RemovedById { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string RemovedReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public partial class Shipment
{
    public Guid? CreatedById { get; set; }
    public DateTime? HandedOverAt { get; set; }
    public byte[] RowVersion { get; set; }
}

public partial class ShipmentHandover
{
    public string RecipientName { get; set; }
    public string RecipientPhone { get; set; }
    public string WaybillCode { get; set; }
    public string VehicleNumber { get; set; }
    public string SealNumber { get; set; }
    public string Note { get; set; }
    public Guid? HandedOverById { get; set; }
}

public partial class ShippingRoute
{
    /// <summary>Tuyến bắt buộc có tờ khai xuất trước khi bàn giao lô.</summary>
    public bool RequiresCustomsExport { get; set; }
}

public partial class Order
{
    /// <summary>Khách giữ hàng lại kho nguồn: kiện của đơn không được đưa vào phiếu xuất.</summary>
    public bool ExportHold { get; set; }
    public string ExportHoldReason { get; set; }
    public Guid? ExportHoldById { get; set; }
    public DateTime? ExportHoldAt { get; set; }
}

/// <summary>
/// Giấy tờ đính kèm cho đơn / phiếu xuất / kiện trong phiếu / lô. File nằm riêng tư trên volume
/// của container, chỉ tải được qua API có kiểm quyền.
/// </summary>
public partial class Attachment
{
    public Guid Id { get; set; }

    /// <summary>ORDER · WRO · WRO_PARCEL · SHIPMENT.</summary>
    public string EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>PERMIT · PICKING_ISSUE · PARCEL_PHOTO · CUSTOMS_EXPORT · COMMERCIAL_INVOICE · HANDOVER_RECORD · WAYBILL · CUSTOMS_IMPORT · TAX_RECEIPT · INCIDENT · OTHER.</summary>
    public string DocumentType { get; set; }

    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long Size { get; set; }
    public string Sha256 { get; set; }

    /// <summary>Đường dẫn tương đối trong thư mục lưu trữ.</summary>
    public string StorageKey { get; set; }

    public Guid? UploadedById { get; set; }
    public DateTime UploadedAt { get; set; }
    public string Note { get; set; }
}

public partial class Tracking
{
    /// <summary>Người ghi mốc — Sale chịu trách nhiệm cập nhật hành trình nên phải biết ai ghi.</summary>
    public Guid? CreatedById { get; set; }

    /// <summary>
    /// Lời nhắn hiển thị cho khách ở mốc này. Tách khỏi <c>Note</c> (ghi chú nội bộ) để Sale ghi
    /// được chi tiết với đối tác/hải quan mà không lộ ra màn hình của khách.
    /// </summary>
    public string CustomerMessage { get; set; }
}

// ── Luồng hàng về Việt Nam (docs/migrate-source-cu/07-luong-hang-ve-viet-nam.md) ─────────────

public partial class ParcelInspection
{
    /// <summary>Kích thước kho VN đo lại (cm) — cùng với cân nặng là số dùng để tính cước cuối.</summary>
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
}

/// <summary>
/// Sự cố của một kiện: hỏng / sai hàng / thiếu hàng lúc tiếp nhận ở VN, cân lệch bất thường,
/// hoặc khách khiếu nại sau khi nhận. Khách chọn cách xử lý, quản lý kho quyết định.
/// </summary>
public partial class ParcelIncident
{
    public Guid Id { get; set; }
    public string IncidentCode { get; set; }
    public Guid ParcelId { get; set; }
    public Guid? OrderId { get; set; }

    /// <summary>DAMAGED · WRONG_ITEM · MISSING_ITEMS · WEIGHT_DEVIATION · COMPLAINT.</summary>
    public string IncidentType { get; set; }

    /// <summary>OPEN · CUSTOMER_RESPONDED · RESOLVED.</summary>
    public string Status { get; set; }

    /// <summary>RECEIVING (lúc kho VN tiếp nhận) · AFTER_DELIVERY (khách khiếu nại).</summary>
    public string Stage { get; set; }

    public string Description { get; set; }
    public Guid? ReportedById { get; set; }
    public DateTime ReportedAt { get; set; }

    /// <summary>ACCEPT · COMPENSATE · DISPOSE.</summary>
    public string CustomerChoice { get; set; }
    public string CustomerNote { get; set; }
    public DateTime? CustomerRespondedAt { get; set; }

    /// <summary>ACCEPT · COMPENSATE · DISPOSE.</summary>
    public string Resolution { get; set; }
    public string ResolutionNote { get; set; }
    public decimal? CompensationAmount { get; set; }
    public Guid? ResolvedById { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public DateTime? CompensationPaidAt { get; set; }
    public Guid? CompensationPaidById { get; set; }
    public string CompensationReference { get; set; }

    public byte[] RowVersion { get; set; }
}

public partial class DeliveryRequest
{
    /// <summary>Phí giao lại (giao thất bại rồi giao lần nữa). 0 / null là miễn phí.</summary>
    public decimal? RedeliveryFee { get; set; }

    /// <summary>Khoản thu phí giao lại — phải PAID thì kho mới đặt giao được.</summary>
    public Guid? RedeliveryFeePaymentId { get; set; }

    /// <summary>Yêu cầu giao trước đó bị hoàn hàng, yêu cầu này giao lại.</summary>
    public Guid? SourceDeliveryRequestId { get; set; }

    public DateTime? ReturnedAt { get; set; }
    public Guid? ReturnedById { get; set; }

    /// <summary>Người nhận thực tế khi Sale ghi bằng chứng giao tay (hãng không báo về).</summary>
    public string ProofReceivedBy { get; set; }
    public DateTime? ProofAt { get; set; }
    public Guid? ProofById { get; set; }
}

public partial class Order
{
    /// <summary>Đơn tự hoàn thành vì hết hạn khiếu nại mà khách không xác nhận.</summary>
    public bool CompletedAutomatically { get; set; }
}
