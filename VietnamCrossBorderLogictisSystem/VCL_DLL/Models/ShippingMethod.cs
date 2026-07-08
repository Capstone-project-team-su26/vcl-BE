#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class ShippingMethod
{
    public Guid Id { get; set; }

    public string MethodName { get; set; }

    public string MethodCode { get; set; }

    public string Description { get; set; }

    /// <summary>Thời gian vận chuyển dự kiến (VD: "3-5 ngày").</summary>
    public string EstimatedTransitTime { get; set; }

    /// <summary>Điều kiện áp dụng (nếu có).</summary>
    public string ApplicableCondition { get; set; }

    public bool IsActive { get; set; }

    public string InternalNote { get; set; }

    public DateTime? CreatedAt { get; set; }
}
