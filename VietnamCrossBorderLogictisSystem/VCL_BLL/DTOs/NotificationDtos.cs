using System;

namespace VCL_BLL.DTOs
{
    public class NotificationDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    /// <summary>Danh sách thông báo kèm số chưa đọc, để FE vẽ chấm đỏ mà khỏi gọi thêm lần nữa.</summary>
    public class NotificationListDto
    {
        public PaginatedResponse<NotificationDto> Page { get; set; } = new();
        public int UnreadCount { get; set; }
    }
}
