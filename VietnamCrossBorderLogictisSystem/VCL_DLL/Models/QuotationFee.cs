using System;

namespace VCL_DLL.Models
{
    public partial class QuotationFee
    {
        public Guid Id { get; set; }

        public Guid? QuotationId { get; set; }

        public Guid? PurchaseQuotationId { get; set; }

        public Guid? PricingRuleId { get; set; }

        public string FeeName { get; set; } = string.Empty;

        public string FeeType { get; set; } = string.Empty;

        public string CalculationType { get; set; } = string.Empty;

        public decimal Value { get; set; }

        public decimal Amount { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; }

        public virtual Quotation? Quotation { get; set; }

        public virtual PurchaseQuotation? PurchaseQuotation { get; set; }

        public virtual PricingRule? PricingRule { get; set; }
    }
}
