namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Response DTO cho báo giá tạm tính.
    /// </summary>
    public class EstimateQuotationResponseDto
    {
        public Guid QuotationId { get; set; }
        public Guid OrderId { get; set; }
        public string ConsignmentType { get; set; }
        public string QuoteType { get; set; }
        public string Status { get; set; }

        // Thông tin tính toán
        public decimal TotalActualWeight { get; set; }
        public decimal TotalVolumetricWeight { get; set; }
        public decimal ChargeableWeight { get; set; }
        public decimal TotalVolume { get; set; }
        public decimal TotalDeclaredValue { get; set; }

        // Chi phí
        public decimal EstimatedFreightCharge { get; set; }
        public decimal ServiceFee { get; set; }
        public decimal TaxAndDuty { get; set; }
        public decimal TotalEstimatedCost { get; set; }

        public string Currency { get; set; }
        public int ItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiredAt { get; set; }

        public List<QuotationItemDetailDto> Items { get; set; } = new();
    }

    /// <summary>
    /// Chi tiết từng item trong báo giá tạm tính.
    /// </summary>
    public class QuotationItemDetailDto
    {
        public Guid ItemId { get; set; }
        public string ProductName { get; set; }
        public decimal? Weight { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? DeclaredValue { get; set; }
    }
}
