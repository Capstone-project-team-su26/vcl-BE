using System;

namespace VCL_DLL.Models;

/// <summary>
/// Nhật ký đổi trạng thái yêu cầu mua hộ: ai đổi, từ trạng thái nào sang nào, kèm ghi chú
/// và ảnh bằng chứng tại thời điểm đó. Ghi thêm dòng mới, không sửa dòng cũ.
/// </summary>
public partial class PurchaseRequestHistory
{
    public Guid Id { get; set; }

    public Guid PurchaseRequestId { get; set; }

    /// <summary>Trạng thái trước khi đổi. Null nếu đây là bản ghi đầu tiên.</summary>
    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>Ảnh bằng chứng của lần đổi này, nối bằng '|'.</summary>
    public string? ProofImageUrls { get; set; }

    public Guid? ChangedBy { get; set; }

    /// <summary>Lưu sẵn tên người đổi để hiển thị lịch sử không phải join lại bảng USERS.</summary>
    public string? ChangedByName { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual PurchaseRequest PurchaseRequest { get; set; } = null!;
}
