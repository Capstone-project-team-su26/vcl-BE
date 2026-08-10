using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Phí lưu kho tại kho đích của một đơn ký gửi.
    /// Tính theo kiện/ngày, miễn phí <see cref="FreeDays"/> ngày đầu kể từ lúc kiện được
    /// xếp vào ô kệ ở kho đích, ngừng đếm khi kiện được lấy ra để đóng gói giao đi.
    /// </summary>
    public class StorageFeeDto
    {
        public Guid OrderId { get; set; }

        public string OrderCode { get; set; } = string.Empty;

        /// <summary>Số ngày đầu không tính phí.</summary>
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

        /// <summary>Thời điểm xếp vào ô kệ ở kho đích.</summary>
        public DateTime? StoredAt { get; set; }

        /// <summary>Thời điểm lấy ra khỏi ô kệ. Null nghĩa là vẫn đang nằm trong kho.</summary>
        public DateTime? ReleasedAt { get; set; }

        /// <summary>Tổng số ngày đã nằm trong kho đích (làm tròn lên).</summary>
        public int StoredDays { get; set; }

        /// <summary>Số ngày thực sự bị tính phí, đã trừ phần miễn phí.</summary>
        public int ChargeableDays { get; set; }

        public decimal Amount { get; set; }
    }
}
