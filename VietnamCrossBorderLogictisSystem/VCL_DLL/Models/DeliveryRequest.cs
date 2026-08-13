using System;
using System.Collections.Generic;

namespace VCL_DLL.Models;

/// <summary>
/// Yêu cầu giao hàng chặng cuối tới nhà khách. Sale lập, OM duyệt, kho gom hàng rồi đặt
/// đơn vị giao hàng.
/// <para>
/// Đây là điểm hội tụ của hai nhánh sau khi hàng về kho VN: kiện giao thẳng đi vào đây ngay
/// sau khi kho kiểm đếm, còn kiện khách gửi lại kho thì nằm kệ tới lúc khách muốn nhận mới
/// vào. Khác phiếu xuất kho ở chỗ phiếu xuất kho lo đưa hàng rời kệ đi quốc tế, còn phiếu này
/// lo giao tới tận tay khách.
/// </para>
/// </summary>
public partial class DeliveryRequest
{
    public Guid Id { get; set; }

    public string DeliveryCode { get; set; }

    /// <summary>Đơn hàng của khách. Mỗi phiếu giao chỉ phục vụ một đơn.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>DELIVERY_PENDING · DELIVERY_APPROVED · DELIVERY_REJECTED · DELIVERY_DISPATCHED.</summary>
    public string Status { get; set; }

    public string ReceiverName { get; set; }

    public string ReceiverPhone { get; set; }

    /// <summary>Số nhà, tên đường.</summary>
    public string AddressDetail { get; set; }

    /// <summary>
    /// Ba cấp hành chính tách riêng vì GoShip bắt buộc như vậy, trong khi đơn hàng chỉ lưu
    /// địa chỉ dạng một chuỗi.
    /// </summary>
    public string Province { get; set; }

    public string District { get; set; }

    public string Ward { get; set; }

    /// <summary>Ngày khách hẹn nhận, nếu có.</summary>
    public DateTime? ScheduledDate { get; set; }

    public string Note { get; set; }

    /// <summary>Mã vận đơn bên giao hàng trả về, có sau khi kho bấm đặt giao.</summary>
    public string CarrierTrackingCode { get; set; }

    public DateTime? DispatchedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    /// <summary>Bắt buộc có khi OM từ chối phiếu.</summary>
    public string RejectionReason { get; set; }

    public virtual Order Order { get; set; }

    public virtual ICollection<DeliveryRequestItem> Items { get; set; } = new List<DeliveryRequestItem>();
}
