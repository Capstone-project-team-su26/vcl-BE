using System;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Kết quả một bước đóng đơn ở cuối luồng: khách xác nhận đã nhận, OM chốt hoàn thành,
    /// hoặc Sale từ chối vì khách không trả nốt tiền.
    /// </summary>
    public class OrderClosingResponseDto
    {
        public Guid OrderId { get; set; }

        public string ConsignmentCode { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string StatusLabel { get; set; } = string.Empty;

        public string ActedByName { get; set; } = string.Empty;

        public DateTime ActedAt { get; set; }

        public string? Note { get; set; }
    }

    public class CompleteConsignmentRequestDto
    {
        public string? Note { get; set; }
    }

    /// <summary>
    /// Sale từ chối đơn vì khách quá hạn không thanh toán đợt cuối. Tiền cọc không hoàn lại.
    /// </summary>
    public class RejectUnpaidConsignmentRequestDto
    {
        public string? Reason { get; set; }
    }
}
