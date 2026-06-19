#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class PricingRule
{
    public Guid Id { get; set; }

    public string ShippingServiceType { get; set; }

    public string ConsignmentType { get; set; }

    public string Route { get; set; }

    public string UnitType { get; set; }

    public decimal PricePerWeight { get; set; }

    public decimal? PricePerVolume { get; set; }

    public decimal ServiceFee { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
