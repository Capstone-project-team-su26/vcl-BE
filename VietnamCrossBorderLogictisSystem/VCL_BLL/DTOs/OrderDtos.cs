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
        public string? Route { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
        public bool RequiresInspection { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? PaymentConfirmedAt { get; set; }
        public DateTime? QuotationCreatedAt { get; set; }

        /// <summary>Mốc thời gian tương ứng với trạng thái hiện tại của đơn (dùng cho FE hiển thị real-time theo status).</summary>
        public DateTime? StatusUpdatedAt { get; set; }
        public Guid? WarehouseId { get; set; }

        /// <summary>Danh sách ID các quy định giá / dịch vụ phụ áp dụng cho đơn.</summary>
        public List<Guid> PricingRuleIds { get; set; } = new();

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
