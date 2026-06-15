using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Response trả về báo giá tạm tính cho FE hiển thị.
    /// </summary>
    public class EstimateQuotationResponse
    {
        public Guid QuotationId { get; set; }
        public Guid OrderId { get; set; }
        public string ConsignmentType { get; set; } = string.Empty;
        public string QuoteType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        /// <summary>Tổng cân nặng thực tế (kg)</summary>
        public decimal TotalWeight { get; set; }

        /// <summary>Tổng thể tích (cm³)</summary>
        public decimal TotalVolume { get; set; }

        /// <summary>Cân nặng quy đổi từ thể tích (kg) = TotalVolume / 5000</summary>
        public decimal VolumetricWeight { get; set; }

        /// <summary>Cân tính phí = MAX(TotalWeight, VolumetricWeight)</summary>
        public decimal ChargeableWeight { get; set; }

        /// <summary>Phí vận chuyển ước tính</summary>
        public decimal EstimatedFreightCharge { get; set; }

        /// <summary>Phí dịch vụ</summary>
        public decimal ServiceFee { get; set; }

        /// <summary>Thuế & phí hải quan tạm tính</summary>
        public decimal TaxAndDuty { get; set; }

        /// <summary>Tổng chi phí dự kiến = FreightCharge + ServiceFee + TaxAndDuty</summary>
        public decimal TotalEstimatedCost { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiredAt { get; set; }

        /// <summary>Chi tiết từng sản phẩm trong đơn hàng</summary>
        public List<OrderItemDetail> Items { get; set; } = new();
    }

    /// <summary>
    /// Chi tiết một sản phẩm trong đơn hàng, kèm thể tích tính toán.
    /// </summary>
    public class OrderItemDetail
    {
        public Guid Id { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal? Weight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? DeclaredValue { get; set; }

        /// <summary>Thể tích item = Length × Width × Height (cm³)</summary>
        public decimal ItemVolume { get; set; }
    }
}
