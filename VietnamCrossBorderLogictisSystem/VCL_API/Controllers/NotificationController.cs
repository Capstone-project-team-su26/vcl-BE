using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Thông báo của chính người đang đăng nhập. Áp dụng cho mọi vai trò, mọi luồng
    /// (ký gửi duyệt/từ chối, mua hộ đã đặt hàng / người bán đã giao / đã nhập kho).
    /// </summary>
    [ApiController]
    [Route("api/notifications")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotificationController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        private Guid? CurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }

        /// <summary>
        /// Danh sách thông báo của mình, mới nhất trước, kèm số chưa đọc.
        /// </summary>
        /// <param name="pageNumber">Số trang (mặc định 1)</param>
        /// <param name="pageSize">Số thông báo mỗi trang (mặc định 20)</param>
        /// <param name="unreadOnly">true để chỉ lấy thông báo chưa đọc</param>
        [HttpGet]
        public async Task<IActionResult> GetMine(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] bool unreadOnly = false)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return Unauthorized(new { success = false, message = "Không thể xác định danh tính người dùng." });

            var result = await _notificationService.GetMyNotificationsAsync(userId.Value, pageNumber, pageSize, unreadOnly);
            return Ok(new
            {
                success = true,
                message = "Lấy danh sách thông báo thành công.",
                data = result.Page,
                unreadCount = result.UnreadCount
            });
        }

        /// <summary>Đánh dấu một thông báo đã đọc.</summary>
        [HttpPut("{notificationId:guid}/read")]
        public async Task<IActionResult> MarkAsRead(Guid notificationId)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return Unauthorized(new { success = false, message = "Không thể xác định danh tính người dùng." });

            try
            {
                await _notificationService.MarkAsReadAsync(userId.Value, notificationId);
                return Ok(new { success = true, message = "Đã đánh dấu thông báo là đã đọc." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
        }

        /// <summary>Đánh dấu tất cả thông báo của mình là đã đọc.</summary>
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var userId = CurrentUserId();
            if (userId == null)
                return Unauthorized(new { success = false, message = "Không thể xác định danh tính người dùng." });

            var updated = await _notificationService.MarkAllAsReadAsync(userId.Value);
            return Ok(new
            {
                success = true,
                message = $"Đã đánh dấu {updated} thông báo là đã đọc.",
                data = new { updatedCount = updated }
            });
        }

        /// <summary>Tạo và gửi thông báo trực tiếp tới một người dùng (Dành cho Staff/Admin).</summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Staff,Sale,Sales,OperationsManager,Operations")]
        public async Task<IActionResult> CreateNotification([FromBody] CreateNotificationRequestDto request)
        {
            if (request == null || request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { success = false, message = "UserId và Title không được để trống." });
            }

            try
            {
                await _notificationService.CreateNotificationAsync(request.UserId, request.Title, request.Content);
                return Ok(new { success = true, message = "Gửi thông báo thành công." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = this.UserError(ex) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi gửi thông báo: " + this.UserError(ex) });
            }
        }
    }
}
