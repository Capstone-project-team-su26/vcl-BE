using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Tham số hệ thống Admin chỉnh được. Hiện có giới hạn tạo đơn ký gửi / mua hộ
    /// (cân nặng, giá trị khai báo, kích thước, số kiện, số dòng, số lượng).
    /// </summary>
    [Route("api/system-settings")]
    [ApiController]
    [Authorize]
    public class SystemSettingsController : ControllerBase
    {
        private const string AdminRoles = "Admin,ADMIN,Administrator,ADMINISTRATOR";

        private readonly IOrderLimitSettingsService _orderLimits;

        public SystemSettingsController(IOrderLimitSettingsService orderLimits)
        {
            _orderLimits = orderLimits;
        }

        private bool IsAdmin =>
            User.IsInRole("Admin") || User.IsInRole("ADMIN") || User.IsInRole("Administrator") || User.IsInRole("ADMINISTRATOR");

        /// <summary>
        /// Giới hạn tạo đơn đang áp dụng — mọi tài khoản đã đăng nhập (khách + nhân viên) đọc được để form
        /// hiện gợi ý và kiểm trước. Giá trị null = không giới hạn. Admin nhận thêm người sửa / thời điểm sửa.
        /// </summary>
        [HttpGet("order-limits")]
        public async Task<IActionResult> GetOrderLimits()
        {
            var data = await _orderLimits.GetAsync(includeAudit: IsAdmin);
            return Ok(new { message = "Lấy cấu hình giới hạn đơn hàng thành công.", data });
        }

        /// <summary>
        /// Admin sửa một hoặc nhiều giới hạn: <c>{ items: [{ key, value }], note? }</c>.
        /// <c>value = null</c> nghĩa là không giới hạn (chỉ với giới hạn cho phép). Khoá không gửi thì giữ nguyên.
        /// </summary>
        [HttpPut("order-limits")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> UpdateOrderLimits([FromBody] UpdateOrderLimitSettingsRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var data = await _orderLimits.UpdateAsync(request, adminId);
                return Ok(new { message = "Đã cập nhật giới hạn đơn hàng.", data });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>Nhật ký thay đổi giới hạn tạo đơn (mới nhất trước).</summary>
        [HttpGet("order-limits/history")]
        [Authorize(Roles = AdminRoles)]
        public async Task<IActionResult> GetOrderLimitsHistory([FromQuery] int take = 50)
        {
            var data = await _orderLimits.GetHistoryAsync(take);
            return Ok(new { message = "Lấy nhật ký thay đổi giới hạn đơn hàng thành công.", data });
        }
    }
}
