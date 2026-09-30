using VCL_BLL.Helpers;
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

        public async Task CreateNotificationAsync(Guid userId, string title, string content)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("UserId không hợp lệ.");

            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Tiêu đề thông báo không được để trống.");

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title.Trim(),
                Content = content?.Trim() ?? string.Empty,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Repository<Notification>().AddAsync(notification);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task NotifyUserAsync(Guid userId, string title, string content)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(title)) return;

            try
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = title.Trim(),
                    Content = content?.Trim() ?? string.Empty,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Repository<Notification>().AddAsync(notification);
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NOTIFICATION ERROR] To {userId}: {ex.Message}");
            }
        }

        public async Task NotifyUsersAsync(System.Collections.Generic.IEnumerable<Guid> userIds, string title, string content)
        {
            if (userIds == null || string.IsNullOrWhiteSpace(title)) return;

            var distinctUserIds = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (!distinctUserIds.Any()) return;

            try
            {
                var now = DateTime.UtcNow;
                var trimmedTitle = title.Trim();
                var trimmedContent = content?.Trim() ?? string.Empty;
                var repo = _unitOfWork.Repository<Notification>();

                foreach (var uid in distinctUserIds)
                {
                    await repo.AddAsync(new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = uid,
                        Title = trimmedTitle,
                        Content = trimmedContent,
                        IsRead = false,
                        CreatedAt = now
                    });
                }

                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NOTIFICATION ERROR] Bulk users: {ex.Message}");
            }
        }

        public async Task NotifyRolesAsync(System.Collections.Generic.IEnumerable<string> roles, string title, string content, Guid? excludeUserId = null)
        {
            if (roles == null || string.IsNullOrWhiteSpace(title)) return;

            var roleSet = roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!roleSet.Any()) return;
            var notifyWarehouseStaff = roleSet.Any(StaffRoleHelper.IsWarehouseStaffRole);

            try
            {
                var allUsers = await _unitOfWork.Repository<User>().GetAllAsync();
                var targetUserIds = allUsers
                    .Where(u => !string.IsNullOrWhiteSpace(u.Role)
                        && (roleSet.Contains(u.Role.Trim())
                            // Gửi cho "WarehouseStaff" thì cả biến thể tên ("Warehouse Staff VN"…) cũng nhận.
                            || (notifyWarehouseStaff && StaffRoleHelper.IsWarehouseStaffRole(u.Role))))
                    .Where(u => !excludeUserId.HasValue || u.Id != excludeUserId.Value)
                    .Select(u => u.Id)
                    .ToList();

                if (targetUserIds.Any())
                {
                    await NotifyUsersAsync(targetUserIds, title, content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NOTIFICATION ERROR] Roles ({string.Join(",", roleSet)}): {ex.Message}");
            }
        }
    }
}
