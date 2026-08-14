using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Đọc thông báo của chính người đang đăng nhập.
    /// <para>
    /// Bảng NOTIFICATIONS đã được ghi từ lâu (luồng ký gửi duyệt/từ chối, nay thêm luồng mua hộ)
    /// nhưng chưa từng có API nào đọc ra, nên thông báo nằm chết trong DB. Đây là phần đọc còn thiếu.
    /// </para>
    /// </summary>
    public interface INotificationService
    {
        Task<NotificationListDto> GetMyNotificationsAsync(Guid userId, int pageNumber, int pageSize, bool unreadOnly);

        /// <summary>Đánh dấu một thông báo đã đọc. Chỉ chủ sở hữu mới đánh dấu được.</summary>
        Task MarkAsReadAsync(Guid userId, Guid notificationId);

        /// <summary>Đánh dấu tất cả đã đọc; trả về số thông báo vừa đổi.</summary>
        Task<int> MarkAllAsReadAsync(Guid userId);

        /// <summary>Tạo thông báo mới cho người dùng.</summary>
        Task CreateNotificationAsync(Guid userId, string title, string content);

        /// <summary>Gửi thông báo tới 1 người dùng cụ thể (tự catch lỗi để không hỏng luồng chính).</summary>
        Task NotifyUserAsync(Guid userId, string title, string content);

        /// <summary>Gửi thông báo tới danh sách người dùng.</summary>
        Task NotifyUsersAsync(System.Collections.Generic.IEnumerable<Guid> userIds, string title, string content);

        /// <summary>Gửi thông báo tới toàn bộ người dùng có các vai trò được chỉ định (bỏ qua excludeUserId nếu có).</summary>
        Task NotifyRolesAsync(System.Collections.Generic.IEnumerable<string> roles, string title, string content, Guid? excludeUserId = null);
    }
}
