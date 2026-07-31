using System;

namespace VCL_DLL.Models;

/// <summary>
/// Phiếu kiểm kê định kỳ tại một ô kệ: so sánh số kiện hệ thống ghi nhận
/// với số kiện đếm thực tế và lưu lại chênh lệch cùng cách xử lý (BR-47).
/// </summary>
public partial class CycleCount
{
    public Guid Id { get; set; }

    public Guid? WarehouseId { get; set; }

    public Guid BinId { get; set; }

    /// <summary>Số kiện hệ thống đang ghi nhận tại ô kệ, do server tự tính</summary>
    public int ExpectedCount { get; set; }

    /// <summary>Số kiện nhân viên kho đếm được thực tế</summary>
    public int ActualCount { get; set; }

    /// <summary>ActualCount - ExpectedCount</summary>
    public int Difference { get; set; }

    /// <summary>MATCHED, DISCREPANCY hoặc RESOLVED</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Ghi chú của nhân viên kiểm kê</summary>
    public string Note { get; set; } = string.Empty;

    public Guid? CountedById { get; set; }

    public DateTime CountedAt { get; set; }

    public Guid? ResolvedById { get; set; }

    public DateTime? ResolvedAt { get; set; }

    /// <summary>Cách xử lý chênh lệch tồn kho</summary>
    public string ResolutionNote { get; set; } = string.Empty;

    public virtual Warehouse Warehouse { get; set; }

    public virtual Bin Bin { get; set; }

    public virtual User CountedBy { get; set; }

    public virtual User ResolvedBy { get; set; }
}
