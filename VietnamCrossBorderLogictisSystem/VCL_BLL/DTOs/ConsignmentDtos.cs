using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Yêu cầu tạo ký gửi (A1) — header + danh sách mặt hàng + phương án vận chuyển (A2).
    /// </summary>
    public class CreateConsignmentRequest
    {
        /// <summary>Phương án vận chuyển TQ → VN: "Express" hoặc "Consolidation".</summary>
        [Required]
        public string ShippingOption { get; set; } = string.Empty;

        /// <summary>Ghi chú xử lý chung cho cả yêu cầu.</summary>
        public string? Note { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Yêu cầu ký gửi phải có ít nhất một mặt hàng.")]
        public List<ConsignmentItemRequest> Items { get; set; } = new();
    }

    /// <summary>
    /// Một mặt hàng trong yêu cầu ký gửi.
    /// </summary>
    public class ConsignmentItemRequest
    {
        [Required]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Loại hàng — dùng để đối chiếu hàng cấm/hạn chế.</summary>
        [Required]
        public string ProductType { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        public decimal? Weight { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? Length { get; set; }

        /// <summary>Giá trị khai báo.</summary>
        public decimal? DeclaredValue { get; set; }

        /// <summary>Ảnh / link tham chiếu.</summary>
        public string? ReferenceUrl { get; set; }

        /// <summary>Mã tracking nội địa (nếu có) — chỉ để kho đối soát, không tính phí.</summary>
        public string? DomesticTrackingCode { get; set; }
    }

    /// <summary>
    /// Yêu cầu hủy ký gửi của khách hàng. Lý do là tùy chọn.
    /// </summary>
    public class CancelConsignmentRequest
    {
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Kết quả sau khi hủy ký gửi.
    /// </summary>
    public class CancelConsignmentResponse
    {
        public Guid OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public int CancelledQuotations { get; set; }
    }

    /// <summary>
    /// Cảnh báo hàng cấm/hạn chế phát hiện khi tạo ký gửi.
    /// </summary>
    public class RestrictedItemWarning
    {
        public string ProductName { get; set; } = string.Empty;
        public string MatchedItem { get; set; } = string.Empty;
        public string RestrictionType { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    /// <summary>
    /// Response sau khi tạo ký gửi: thông tin đơn + báo giá tạm tính + cảnh báo (nếu có).
    /// </summary>
    public class CreateConsignmentResponse
    {
        public Guid OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;
        public int ItemCount { get; set; }

        /// <summary>Báo giá tạm tính sinh tự động sau khi tạo.</summary>
        public EstimateQuotationResponse Quotation { get; set; } = new();

        /// <summary>Danh sách cảnh báo hàng hạn chế (Restricted). Hàng cấm (Banned) sẽ bị chặn tạo.</summary>
        public List<RestrictedItemWarning> Warnings { get; set; } = new();
    }
}
