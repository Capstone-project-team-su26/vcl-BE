using System;

namespace VCL_BLL.DTOs
{
    public class UpdateConsignmentStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
    }
}
