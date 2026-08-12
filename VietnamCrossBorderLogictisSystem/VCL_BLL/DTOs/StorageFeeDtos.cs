using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Phí lưu kho của một đơn ký gửi, tính trên CẢ HAI KHO: kho nguồn quốc tế và kho đích VN.
    /// Tính theo kiện/ngày kể từ lúc kiện được xếp vào ô kệ, ngừng đếm khi kiện được xuất kho.
    /// Một kiện đi trọn hành trình có thể nằm kho hai lần (nguồn rồi đích) — số ngày được cộng
    /// dồn cả hai chặng, sau đó mới trừ <see cref="FreeDays"/> ngày miễn phí MỘT LẦN cho cả
    /// hành trình (không phải mỗi kho được miễn một lần).
    /// </summary>
    public class StorageFeeDto
    {
        public Guid OrderId { get; set; }

        public string OrderCode { get; set; } = string.Empty;

        /// <summary>Số ngày miễn phí cho cả hành trình của một kiện.</summary>
        public int FreeDays { get; set; }

        /// <summary>Đơn giá một kiện một ngày, lấy từ SERVICE_PRICINGS (ServiceType = STORAGE).</summary>
        public decimal UnitPrice { get; set; }

        public string Currency { get; set; } = "VND";

        public decimal TotalAmount { get; set; }

        /// <summary>Vì sao ra con số này — hữu ích khi tổng bằng 0.</summary>
        public string? Note { get; set; }

        public List<StorageFeeParcelDto> Parcels { get; set; } = new();
    }

    public class StorageFeeParcelDto
    {
        public Guid ParcelId { get; set; }

        public string PackageCode { get; set; } = string.Empty;

        /// <summary>Lần đầu tiên kiện được xếp vào ô kệ, tính trên toàn hành trình.</summary>
        public DateTime? StoredAt { get; set; }

        /// <summary>
        /// Lần cuối cùng kiện được lấy ra khỏi ô kệ. Null nghĩa là kiện vẫn đang nằm trong
        /// một kho nào đó và đồng hồ vẫn đang chạy.
        /// </summary>
        public DateTime? ReleasedAt { get; set; }

        /// <summary>Tổng số ngày nằm kho, cộng dồn mọi chặng (mỗi chặng làm tròn lên).</summary>
        public int StoredDays { get; set; }

        /// <summary>Số ngày thực sự bị tính phí, đã trừ phần miễn phí của cả hành trình.</summary>
        public int ChargeableDays { get; set; }

        public decimal Amount { get; set; }

        /// <summary>
        /// Chi tiết từng chặng nằm kho để FE hiện cho khách hiểu con số ở đâu ra,
        /// ví dụ "Kho nguồn: 10 ngày · Kho VN: 2 ngày". Cộng <see cref="StorageFeeLegDto.Days"/>
        /// của mọi chặng ra đúng <see cref="StoredDays"/>.
        /// </summary>
        public List<StorageFeeLegDto> Legs { get; set; } = new();
    }

    /// <summary>Một lượt kiện nằm trong kho: vào ô kệ lúc nào, ra lúc nào, ở kho nào.</summary>
    public class StorageFeeLegDto
    {
        public Guid? WarehouseId { get; set; }

        public string WarehouseName { get; set; } = string.Empty;

        /// <summary>SOURCE hoặc DESTINATION — lấy theo cấu hình kho.</summary>
        public string WarehouseType { get; set; } = string.Empty;

        public DateTime StoredAt { get; set; }

        /// <summary>Null nghĩa là kiện vẫn đang nằm ở kho này.</summary>
        public DateTime? ReleasedAt { get; set; }

        /// <summary>Số ngày của riêng chặng này, làm tròn lên.</summary>
        public int Days { get; set; }
    }
}
