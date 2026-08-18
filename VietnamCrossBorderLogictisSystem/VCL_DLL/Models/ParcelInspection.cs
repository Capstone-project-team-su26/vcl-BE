using System;

namespace VCL_DLL.Models;

/// <summary>
/// Biên bản kiểm đếm MỘT kiện hàng tại kho đích Việt Nam.
/// <para>
/// Trước đây kho gõ số lượng và cân nặng thực tế trên app rồi bấm xác nhận, nhưng những con số
/// đó không được gửi lên server — chỉ nằm trong bộ nhớ máy. Hàng về thiếu hay móp thùng thì Sale,
/// OM và khách đều không thấy, và không có gì để đòi hãng vận chuyển.
/// </para>
/// <para>
/// Mỗi lần kho xác nhận kiểm đếm một kiện là một bản ghi. Kiện kiểm lại lần hai (ví dụ mở ra
/// đếm lại) thì thêm bản ghi mới chứ không ghi đè, để còn lần được ai đếm ra bao nhiêu lúc nào.
/// </para>
/// </summary>
public partial class ParcelInspection
{
    public Guid Id { get; set; }

    public Guid ParcelId { get; set; }

    /// <summary>Lô quốc tế mà kiện về theo. Dùng để OM lọc chênh lệch theo từng chuyến.</summary>
    public Guid? ShipmentId { get; set; }

    /// <summary>Đơn hàng của kiện, để Sale tra ngược khi khách hỏi.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Số lượng theo khai báo/manifest tại thời điểm kiểm.</summary>
    public int? DeclaredQuantity { get; set; }

    /// <summary>Số lượng kho đếm được thực tế.</summary>
    public int? ActualQuantity { get; set; }

    /// <summary>Cân nặng theo khai báo (kg).</summary>
    public decimal? DeclaredWeight { get; set; }

    /// <summary>Cân nặng kho cân lại thực tế (kg).</summary>
    public decimal? ActualWeight { get; set; }

    /// <summary>
    /// Tình trạng kiện lúc nhận: <c>INTACT</c> nguyên vẹn · <c>MISSING</c> thiếu hàng ·
    /// <c>DAMAGED</c> hư hỏng · <c>WET</c> ẩm ướt · <c>SEAL_BROKEN</c> rách niêm phong.
    /// </summary>
    public string Condition { get; set; }

    /// <summary>Mô tả của nhân viên kho: móp góc nào, ướt bao nhiêu, thiếu món gì.</summary>
    public string Note { get; set; }

    /// <summary>
    /// Có lệch so với khai báo hay không — tính sẵn lúc lưu để OM lọc nhanh mà không phải
    /// so lại từng dòng. Lệch số lượng, lệch cân nặng, hoặc tình trạng khác nguyên vẹn.
    /// </summary>
    public bool HasDiscrepancy { get; set; }

    public Guid? InspectedBy { get; set; }

    public DateTime? InspectedAt { get; set; }

    public virtual Parcel Parcel { get; set; }

    public virtual Shipment Shipment { get; set; }

    public virtual Order Order { get; set; }
}
