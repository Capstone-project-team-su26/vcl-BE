using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Một biên bản kiểm đếm kiện tại kho VN, dựng cho màn theo dõi của OM.
    /// <para>
    /// OM không xử lý chênh lệch trong hệ thống — việc đòi hãng vận chuyển hay đền khách làm
    /// bên ngoài. Cái OM cần ở đây là biết kiện nào lệch, lệch bao nhiêu, ai đếm, lúc nào,
    /// để cầm số liệu đó đi làm việc.
    /// </para>
    /// </summary>
    public class ParcelInspectionDto
    {
        public Guid InspectionId { get; set; }

        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;

        public Guid? ShipmentId { get; set; }
        public string? ShipmentCode { get; set; }

        public Guid? OrderId { get; set; }
        public string? OrderCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }

        public int? DeclaredQuantity { get; set; }
        public int? ActualQuantity { get; set; }
        public decimal? DeclaredWeight { get; set; }
        public decimal? ActualWeight { get; set; }

        /// <summary>Âm là thiếu so với khai báo.</summary>
        public int? QuantityDifference { get; set; }
        public decimal? WeightDifference { get; set; }

        public string? Condition { get; set; }
        public string? ConditionText { get; set; }
        public string? Note { get; set; }

        public bool HasDiscrepancy { get; set; }

        /// <summary>Gộp thành một câu để OM đọc lướt danh sách, ví dụ "Thiếu 1 kiện · nhẹ hơn 0,4 kg".</summary>
        public string DiscrepancySummary { get; set; } = string.Empty;

        public string? InspectedByName { get; set; }
        public DateTime? InspectedAt { get; set; }

        /// <summary>Kho đích nơi phát hiện chênh lệch.</summary>
        public string? WarehouseName { get; set; }
    }

    /// <summary>
    /// Kho lưu kết quả đối chiếu MỘT kiện, ngay tại chỗ khi vừa cân đếm xong.
    /// <para>
    /// Tách khỏi bước xác nhận nhận cả lô: nhân viên đếm tới đâu lưu tới đó, rời màn hình hay
    /// hết pin cũng không mất, và OM thấy được ngay cả khi lô chưa chốt xong.
    /// </para>
    /// </summary>
    public class SaveParcelInspectionRequest
    {
        [Range(0, 100000, ErrorMessage = "Số lượng thực tế không hợp lệ.")]
        public int? ActualQuantity { get; set; }

        [Range(0, 100000, ErrorMessage = "Cân nặng thực tế không hợp lệ.")]
        public decimal? ActualWeight { get; set; }

        /// <summary><c>INTACT</c> · <c>MISSING</c> · <c>DAMAGED</c> · <c>WET</c> · <c>SEAL_BROKEN</c>.</summary>
        [MaxLength(30, ErrorMessage = "Tình trạng kiện tối đa 30 ký tự.")]
        public string? Condition { get; set; }

        [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
        public string? Note { get; set; }
    }

    public class ParcelInspectionSummaryDto
    {
        public int Total { get; set; }

        /// <summary>Số biên bản có chênh lệch — con số OM quan tâm nhất.</summary>
        public int WithDiscrepancy { get; set; }

        /// <summary>Chênh lệch phát hiện trong 7 ngày gần nhất.</summary>
        public int RecentDiscrepancy { get; set; }

        /// <summary>Số kiện hư hỏng / ẩm ướt / rách niêm phong.</summary>
        public int DamagedParcels { get; set; }
    }

    public class ParcelInspectionListDto
    {
        public ParcelInspectionSummaryDto Summary { get; set; } = new();
        public List<ParcelInspectionDto> Items { get; set; } = new();
    }
}
