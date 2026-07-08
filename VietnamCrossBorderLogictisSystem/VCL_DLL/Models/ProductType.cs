#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class ProductType
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public bool IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }
}
