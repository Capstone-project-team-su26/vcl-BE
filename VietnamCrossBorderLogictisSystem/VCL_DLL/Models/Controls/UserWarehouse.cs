#nullable disable
using System;

namespace VCL_DLL.Models;

/// <summary>
/// Nhân viên kho được Admin gán vào một kho cụ thể.
/// <para>
/// Tài khoản có ít nhất một dòng ở đây chỉ thao tác được tại đúng các kho đó. Tài khoản chưa có
/// dòng nào giữ cách giới hạn cũ theo <see cref="User.Region"/>, để tài khoản đang dùng không gãy.
/// </para>
/// </summary>
public partial class UserWarehouse
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid WarehouseId { get; set; }

    /// <summary>Admin đã gán.</summary>
    public Guid? AssignedById { get; set; }

    public DateTime AssignedAt { get; set; }

    public string Note { get; set; }
}
