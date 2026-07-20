#nullable disable
using System;
using System.Collections.Generic;

namespace VCL_DLL.Models;

public partial class PackageConfiguration
{
    public Guid Id { get; set; }

    public string ConfigCode { get; set; }

    public string ConfigName { get; set; }

    public decimal Length { get; set; }

    public decimal Width { get; set; }

    public decimal Height { get; set; }

    public decimal MaxWeight { get; set; }

    public decimal PackageFee { get; set; }

    public string Status { get; set; } = "ACTIVE";

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public virtual ICollection<Parcel> Parcels { get; set; } = new List<Parcel>();
}
