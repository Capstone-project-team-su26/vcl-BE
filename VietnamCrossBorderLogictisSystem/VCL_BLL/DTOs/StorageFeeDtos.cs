using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Phí lưu kho của một đơn ký gửi, CHỈ tính trên kho đích Việt Nam.
    /// <para>
    /// Thời gian nằm kho nguồn (chờ gom container) không tính tiền vì đó là lịch của công ty
    /// chứ không phải khách chậm — nên các chặng ở kho nguồn cũng không xuất hiện trong
    /// <see cref="StorageFeeParcelDto.Legs"/>.
    /// </para>
    /// Tính theo kiện/ngày kể từ lúc kiện được xếp vào ô kệ kho VN, ngừng đếm khi kiện được
    /// xuất kho. Một kiện có thể vào kho VN hai lượt (giao hỏng rồi hoàn về) — số ngày cộng dồn
    /// mọi lượt, sau đó mới trừ <see cref="FreeDays"/> ngày miễn phí MỘT LẦN.
    /// </summary>
    public class StorageFeeDto
    {
        public Guid OrderId { get; set; }

        public string OrderCode { get; set; } = string.Empty;

        /// <summary>Số ngày miễn phí tại kho VN của một kiện.</summary>
        public int FreeDays { get; set; }

        /// <summary>
        /// Số ngày ân hạn nối ngay sau kỳ miễn phí để khách thu xếp đến lấy hàng.
        /// Chỉ từ ngày thứ <c>FreeDays + GraceDays + 1</c> mới bắt đầu tính tiền.
        /// </summary>
        public int GraceDays { get; set; }

        /// <summary>Đơn giá một kiện một ngày, lấy từ SERVICE_PRICINGS (ServiceType = STORAGE).</summary>
        public decimal UnitPrice { get; set; }

        public string Currency { get; set; } = "VND";

        public decimal TotalAmount { get; set; }

        /// <summary>Vì sao ra con số này — hữu ích khi tổng bằng 0.</summary>
        public string? Note { get; set; }

        public List<StorageFeeParcelDto> Parcels { get; set; } = new();
    }

    /// <summary>
    /// Bảng phí lưu kho để khách xem TRƯỚC khi quyết định gửi lại kho hay giao thẳng.
    /// <para>
    /// Khác <see cref="StorageFeeDto"/> ở chỗ kia tính trên hàng đã nằm kho thật, còn đây là ước
    /// tính theo số kiện và số ngày khách dự định gửi — dùng lúc tạo đơn, khi chưa có kiện nào.
    /// </para>
    /// </summary>
    public class StorageFeeEstimateDto
    {
        /// <summary>Số ngày miễn phí kể từ khi hàng về kho VN.</summary>
        public int FreeDays { get; set; }

        /// <summary>Số ngày ân hạn nối sau kỳ miễn phí.</summary>
        public int GraceDays { get; set; }

        /// <summary>Tổng số ngày không mất phí = FreeDays + GraceDays.</summary>
        public int TotalFreeDays { get; set; }

        public decimal UnitPrice { get; set; }
        public string Currency { get; set; } = "VND";

        /// <summary>Vài mốc ngày mẫu để khách hình dung chi phí.</summary>
        public List<StorageFeeEstimateLineDto> Samples { get; set; } = new();

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Một dòng ước tính: gửi bao nhiêu ngày thì mất bao nhiêu tiền.</summary>
    public class StorageFeeEstimateLineDto
    {
        public int Days { get; set; }
        public int ChargeableDays { get; set; }

        /// <summary>Thành tiền cho MỘT kiện ở mốc ngày này.</summary>
        public decimal AmountPerParcel { get; set; }
    }

    public class StorageFeeParcelDto
    {
        public Guid ParcelId { get; set; }

        public string PackageCode { get; set; } = string.Empty;

        /// <summary>Lần đầu tiên kiện được xếp vào ô kệ tại kho VN.</summary>
        public DateTime? StoredAt { get; set; }

        /// <summary>
        /// Lần cuối cùng kiện được lấy ra khỏi ô kệ. Null nghĩa là kiện vẫn đang nằm trong
        /// một kho nào đó và đồng hồ vẫn đang chạy.
        /// </summary>
        public DateTime? ReleasedAt { get; set; }

        /// <summary>Tổng số ngày nằm kho VN, cộng dồn mọi lượt (mỗi lượt làm tròn lên).</summary>
        public int StoredDays { get; set; }

        /// <summary>Số ngày thực sự bị tính phí, đã trừ phần miễn phí và ân hạn.</summary>
        public int ChargeableDays { get; set; }

        public decimal Amount { get; set; }

        /// <summary>
        /// Chi tiết từng chặng nằm kho để FE hiện cho khách hiểu con số ở đâu ra,
        /// ví dụ "Kho HCM: 12 ngày". Cộng <see cref="StorageFeeLegDto.Days"/>
        /// của mọi chặng ra đúng <see cref="StoredDays"/>.
        /// </summary>
        public List<StorageFeeLegDto> Legs { get; set; } = new();
    }

    /// <summary>Một lượt kiện nằm trong kho: vào ô kệ lúc nào, ra lúc nào, ở kho nào.</summary>
    public class StorageFeeLegDto
    {
        public Guid? WarehouseId { get; set; }

        public string WarehouseName { get; set; } = string.Empty;

        /// <summary>Luôn là DESTINATION — chặng ở kho nguồn không được đưa vào danh sách.</summary>
        public string WarehouseType { get; set; } = string.Empty;

        public DateTime StoredAt { get; set; }

        /// <summary>Null nghĩa là kiện vẫn đang nằm ở kho này.</summary>
        public DateTime? ReleasedAt { get; set; }

        /// <summary>Số ngày của riêng chặng này, làm tròn lên.</summary>
        public int Days { get; set; }
    }
}
