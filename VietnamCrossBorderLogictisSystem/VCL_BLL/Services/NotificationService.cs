using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IUnitOfWork _unitOfWork;

        public NotificationService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<NotificationListDto> GetMyNotificationsAsync(
            Guid userId, int pageNumber, int pageSize, bool unreadOnly)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 20;

            var mine = (await _unitOfWork.Repository<Notification>().GetAllAsync())
                .Where(n => n.UserId == userId)
                .ToList();

            var unreadCount = mine.Count(n => n.IsRead != true);

            var filtered = unreadOnly
                ? mine.Where(n => n.IsRead != true).ToList()
                : mine;

            var items = filtered
                .OrderByDescending(n => n.CreatedAt ?? DateTime.MinValue)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Title = n.Title ?? string.Empty,
                    Content = n.Content ?? string.Empty,
                    IsRead = n.IsRead == true,
                    CreatedAt = n.CreatedAt
                })
                .ToList();

            return new NotificationListDto
            {
                Page = new PaginatedResponse<NotificationDto>
                {
                    Items = items,
                    TotalCount = filtered.Count,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                },
                UnreadCount = unreadCount
            };
        }

        public async Task MarkAsReadAsync(Guid userId, Guid notificationId)
        {
            var repo = _unitOfWork.Repository<Notification>();
            var notification = await repo.GetByIdAsync(notificationId)
                ?? throw new NotFoundException($"Không tìm thấy thông báo với ID {notificationId}");

            // Không lộ sự tồn tại của thông báo người khác: trả về đúng lỗi "không tìm thấy".
            if (notification.UserId != userId)
                throw new NotFoundException($"Không tìm thấy thông báo với ID {notificationId}");

            if (notification.IsRead == true)
                return;

            notification.IsRead = true;
            repo.Update(notification);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<int> MarkAllAsReadAsync(Guid userId)
        {
            var repo = _unitOfWork.Repository<Notification>();
            var unread = (await repo.GetAllAsync())
                .Where(n => n.UserId == userId && n.IsRead != true)
                .ToList();

            if (unread.Count == 0)
                return 0;

            foreach (var notification in unread)
            {
                notification.IsRead = true;
                repo.Update(notification);
            }

            await _unitOfWork.SaveChangesAsync();
            return unread.Count;
        }
    }
}
