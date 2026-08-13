using System;

namespace VCL_DLL.Models;

/// <summary>Một kiện hàng nằm trong yêu cầu giao hàng.</summary>
public partial class DeliveryRequestItem
{
    public Guid Id { get; set; }

    public Guid DeliveryRequestId { get; set; }

    public Guid ParcelId { get; set; }

    /// <summary>
    /// Kiện được lấy từ đâu lúc kho gom hàng: <c>FROM_SHELF</c> nhặt trên ô kệ (kiện đã nhập
    /// kho VN), <c>FROM_STAGING</c> lấy ở khu tạm (kiện giao thẳng, chưa từng lên kệ).
    /// FE dựa vào đây để biết màn gom hàng có hiện mã ô kệ hay không.
    /// </summary>
    public string PickSource { get; set; }

    public virtual DeliveryRequest DeliveryRequest { get; set; }

    public virtual Parcel Parcel { get; set; }
}
