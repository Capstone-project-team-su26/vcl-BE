using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class UpdateConsignmentStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
    }

    public class UpdateConsignmentStatusResponse
    {
        public string Status { get; set; } = string.Empty;
        public string? ConsignmentCode { get; set; }
    }

    public class ConsignmentResponseDto
    {
        public Guid OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal? TotalWeight { get; set; }
        /// <summary>Tổng thể tích (cm³)</summary>
        public decimal? TotalVolume { get; set; }
        /// <summary>Tổng thể tích (m³)</summary>
        public decimal? TotalVolumeM3 { get; set; }
        public string? Route { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresInspection { get; set; }
        public DateTime? CreatedAt { get; set; }

        /// <summary>Tên các mặt hàng trong đơn (để FE hiển thị ở danh sách).</summary>
        public List<string> ItemNames { get; set; } = new();
    }

    public class CancelConsignmentRequest
    {
        public string? CancelReason { get; set; }
    }

    public class CancelConsignmentResponse
    {
        public Guid OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? CancelReason { get; set; }
        public int CancelledQuotations { get; set; }
        public string? ConsignmentCode { get; set; }
    }
}
