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

        public List<QuotationFeeResponseDto> AdditionalFees { get; set; } = new();
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

    /// <summary>
    /// Response trả về sau khi khách hàng xác nhận báo giá tạm tính.
    /// </summary>
    public class AcceptQuotationResponse
    {
        public Guid QuotationId { get; set; }
        public Guid OrderId { get; set; }
        public string QuotationStatus { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public DateTime AcceptedAt { get; set; }
    }

    public class CreateQuotationRequest
    {
        public Guid? WarehouseId { get; set; }
        public Guid? ServicePricingId { get; set; }
        public string? ServiceType { get; set; }
        public decimal? WeightKg { get; set; }
        public decimal? VolumeM3 { get; set; }
        public int? PackageCount { get; set; }
        public decimal? DeclaredValue { get; set; }
        public string? SalesNote { get; set; }
        public QuotationDetailsDto? Quotation { get; set; }
    }

    public class QuotationDetailsDto
    {
        public Guid? ServicePricingId { get; set; }
        public string? ServiceType { get; set; }
        public string? OriginCountry { get; set; }
        public string? DestinationCountry { get; set; }
        public string? UnitType { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? Currency { get; set; }
        public decimal? TotalWeight { get; set; }
        public decimal? TotalVolume { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public decimal? MainServiceAmount { get; set; }
        public List<AdditionalFeeDto> AdditionalFees { get; set; } = new();
        public decimal? DiscountPercent { get; set; }
        public decimal? Subtotal { get; set; }
        public decimal? Discount { get; set; }
        public decimal? Total { get; set; }
        public decimal? EstimatedFreightCharge { get; set; }
        public decimal? ServiceFee { get; set; }
        public decimal? TotalEstimatedCost { get; set; }
        public string? SalesNote { get; set; }
    }

    public class AdditionalFeeDto
    {
        public Guid? FeeId { get; set; }
        public string? Code { get; set; }
        public string? Label { get; set; }
        public decimal Amount { get; set; }
        public bool Enabled { get; set; }
    }

    public class QuotationFeeResponseDto
    {
        public Guid Id { get; set; }
        public Guid? PricingRuleId { get; set; }
        
        // Contract properties:
        public Guid? FeeId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string FeeCalculationType { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public int? Quantity { get; set; }
        public decimal Amount { get; set; }
        public bool Enabled { get; set; }
        public bool IsRequired { get; set; }
        public string? UnitNoun { get; set; }

        // Legacy properties (retained for backward compatibility):
        public string FeeName { get; set; } = string.Empty;
        public string FeeType { get; set; } = string.Empty;
        public string CalculationType { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class QuotationActionResponse
    {
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public ConsignmentResponseDto Consignment { get; set; }
    }

    public class RejectQuotationRequest
    {
        public string RejectionReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response trả về thông tin báo giá cho khách hàng xem.
    /// </summary>
    public class QuotationDetailResponse
    {
        public Guid QuotationId { get; set; }
        public Guid OrderId { get; set; }
        public string QuoteType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;

        /// <summary>Tổng cân nặng thực tế (kg)</summary>
        public decimal TotalWeight { get; set; }

        /// <summary>Tổng thể tích (m³)</summary>
        public decimal TotalVolume { get; set; }

        /// <summary>Cân nặng quy đổi thể tích (kg)</summary>
        public decimal VolumetricWeight { get; set; }

        /// <summary>Cân tính cước = MAX(ActualWeight, VolumetricWeight, MinWeight)</summary>
        public decimal ChargeableWeight { get; set; }

        /// <summary>Cước vận chuyển quốc tế</summary>
        public decimal EstimatedFreightCharge { get; set; }

        /// <summary>Phí dịch vụ / Phí nội địa</summary>
        public decimal ServiceFee { get; set; }

        /// <summary>Thuế & phí hải quan tạm tính</summary>
        public decimal TaxAndDuty { get; set; }

        /// <summary>Tổng chi phí</summary>
        public decimal TotalEstimatedCost { get; set; }

        /// <summary>Ngày tạo báo giá</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Ngày hết hạn báo giá</summary>
        public DateTime? ExpiredAt { get; set; }

        /// <summary>Ghi chú của nhân viên sales</summary>
        public string? SalesNote { get; set; }

        public List<QuotationFeeResponseDto> AdditionalFees { get; set; } = new();
    }
}
