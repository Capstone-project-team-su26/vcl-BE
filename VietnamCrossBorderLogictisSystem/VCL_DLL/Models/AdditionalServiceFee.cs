#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class AdditionalServiceFee
{
    public Guid Id { get; set; }

    public string FeeName { get; set; }

    public string FeeCode { get; set; }

    public string CalculationType { get; set; }

    public decimal Value { get; set; }

    public string Unit { get; set; }

    public bool IsActive { get; set; }

    public string Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
