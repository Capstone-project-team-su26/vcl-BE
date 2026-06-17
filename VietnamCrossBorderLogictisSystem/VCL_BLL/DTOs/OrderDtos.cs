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
        public decimal? TotalVolume { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class CancelConsignmentRequest
    {
        public string? CancelReason { get; set; }
        public string? Reason { get; set; }
    }

    public class CancelConsignmentResponse
    {
        public Guid OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public int CancelledQuotations { get; set; }
        public string? ConsignmentCode { get; set; }
    }
}
