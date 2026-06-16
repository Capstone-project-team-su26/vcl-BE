using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
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
}
