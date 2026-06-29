#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class PricingRule
{
    public Guid Id { get; set; }

    public Guid? ServicePricingId { get; set; }

    public string RuleName { get; set; }

    public string RuleCode { get; set; }

    public string RuleType { get; set; }

    public string ConditionType { get; set; }

    public string ConditionValue { get; set; }

    public string CalculationType { get; set; }

    public decimal Value { get; set; }

    public decimal? MinAmount { get; set; }

    public decimal? MaxAmount { get; set; }

    public bool IsRequired { get; set; }

    public string Status { get; set; }

    public string Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ServicePricing ServicePricing { get; set; }
}
