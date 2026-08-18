using System;

namespace VCL_DLL.Models;

/// <summary>
/// Hồ sơ một kiện hàng giao không thành công và quay về kho.
/// <para>
/// Trước đây hãng vận chuyển báo giao hỏng thì hệ thống chỉ bắn thông báo rồi thôi: kiện nằm
/// đâu, ai chịu cước giao lại, bao giờ giao lại — không chỗ nào ghi. Hồ sơ này là nơi giữ toàn
/// bộ chuyện đó từ lúc hãng báo hỏng tới lúc chốt xong hướng xử lý.
/// </para>
/// <para>
/// Mỗi kiện chỉ có tối đa MỘT hồ sơ đang mở. Kiện giao lại lần hai mà hỏng tiếp thì mở hồ sơ
/// mới — lịch sử hai lần hỏng phải tách bạch để còn biết khách này giao mấy lần không được.
/// </para>
/// </summary>
public partial class ParcelReturn
{
    public Guid Id { get; set; }

    /// <summary>Mã hồ sơ dạng RTN-yyyyMMdd-xxxx, để nhân viên gọi nhau qua điện thoại.</summary>
    public string ReturnCode { get; set; }

    public Guid ParcelId { get; set; }

    /// <summary>Đơn hàng của kiện. Null với kiện chưa gắn đơn (không nên có, nhưng dữ liệu cũ có).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Phiếu giao đã hỏng, để lần ngược ra địa chỉ và mã vận đơn đã dùng.</summary>
    public Guid? DeliveryRequestId { get; set; }

    /// <summary>Mã vận đơn hãng vận chuyển của lần giao hỏng.</summary>
    public string TrackingCode { get; set; }

    /// <summary>
    /// Vì sao quay về: <c>FAILED_DELIVERY</c> giao không thành ·
    /// <c>RETURNING</c> hãng đang chuyển hoàn · <c>LOST</c> hãng báo thất lạc ·
    /// <c>CANCELLED</c> đơn giao bị huỷ.
    /// </summary>
    public string Reason { get; set; }

    /// <summary>Nguyên văn dòng trạng thái hãng trả về, giữ lại để đối chất khi cần.</summary>
    public string CarrierStatusText { get; set; }

    /// <summary>
    /// <c>PENDING_RETURN</c> hãng báo hỏng, chờ hàng quay về ·
    /// <c>IN_TRANSIT_BACK</c> hàng đang trên đường về kho ·
    /// <c>RECEIVED_AT_WAREHOUSE</c> kho VN đã cầm được hàng ·
    /// <c>RESOLVED</c> đã chốt hướng xử lý · <c>LOST</c> mất hẳn, không về được.
    /// </summary>
    public string Status { get; set; }

    /// <summary>Kho VN nhận lại hàng.</summary>
    public Guid? WarehouseId { get; set; }

    public DateTime? OpenedAt { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public Guid? ReceivedBy { get; set; }

    /// <summary>
    /// Hướng xử lý OM chốt: <c>REDELIVER</c> giao lại · <c>CUSTOMER_PICKUP</c> khách tự đến lấy ·
    /// <c>DISPOSE</c> thanh lý · <c>COMPENSATE</c> bồi thường cho khách (dùng khi hàng mất).
    /// </summary>
    public string Resolution { get; set; }

    /// <summary>
    /// Ai chịu cước giao lại: <c>CUSTOMER</c> hoặc <c>COMPANY</c>.
    /// Sai địa chỉ do khách khai thì khách chịu; xe không đi giao thì công ty chịu.
    /// </summary>
    public string FeeBearer { get; set; }

    /// <summary>
    /// Miễn phí lưu kho cho quãng hàng nằm chờ sau khi hoàn về.
    /// <para>
    /// Giao hỏng vì lỗi bên mình mà vẫn tính tiền kho những ngày đó thì không giải thích được
    /// với khách. OM tick khi chốt hướng xử lý, phần tính phí sẽ bỏ qua các lượt nằm kho bắt
    /// đầu từ <see cref="ReceivedAt"/> trở đi.
    /// </para>
    /// </summary>
    public bool WaiveStorageFee { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedBy { get; set; }

    /// <summary>Ghi chú kho nhập lúc nhận lại hàng, ví dụ tình trạng thùng.</summary>
    public string Note { get; set; }

    /// <summary>Lý do OM chọn hướng xử lý đó — bắt buộc, vì việc này động tới tiền.</summary>
    public string ResolutionNote { get; set; }

    public virtual Parcel Parcel { get; set; }

    public virtual Order Order { get; set; }

    public virtual Warehouse Warehouse { get; set; }
}
