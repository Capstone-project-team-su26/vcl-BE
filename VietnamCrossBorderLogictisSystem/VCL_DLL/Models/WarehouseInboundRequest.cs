using System;
using System.Collections.Generic;

namespace VCL_DLL.Models;

/// <summary>
/// Phiếu nhập kho tại kho VN, cho OM duyệt trước khi kho xếp hàng lên kệ.
/// <para>
/// Dùng cho nhánh khách gửi lại hàng ở kho VN: hàng về tới nơi, kho mở lô kiểm đếm rồi phân
/// loại, kiện nào khách chưa cần nhận thì lập phiếu này. Kiện giao thẳng cho khách không đi
/// qua đây.
/// </para>
/// <para>
/// KHÔNG nhầm với phiếu tiếp nhận kho (<c>WAREHOUSE_RECEIVING_NOTES</c>) — phiếu đó dành cho
/// khách ký gửi hàng vào kho nguồn và không có bước OM duyệt.
/// </para>
/// </summary>
public partial class WarehouseInboundRequest
{
    public Guid Id { get; set; }

    public string InboundCode { get; set; }

    /// <summary>Lô vận chuyển quốc tế mà các kiện này vừa về theo.</summary>
    public Guid? ShipmentId { get; set; }

    /// <summary>Kho VN nhận hàng vào.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>INBOUND_PENDING · INBOUND_APPROVED · INBOUND_REJECTED.</summary>
    public string Status { get; set; }

    public string Note { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    /// <summary>Bắt buộc có khi OM từ chối phiếu.</summary>
    public string RejectionReason { get; set; }

    public virtual Shipment Shipment { get; set; }

    public virtual Warehouse Warehouse { get; set; }

    public virtual ICollection<WarehouseInboundItem> Items { get; set; } = new List<WarehouseInboundItem>();
}
