using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Authorize]
    public class CustomerDashboardController : ControllerBase
    {
        private readonly ICustomerDashboardService _service;

        public CustomerDashboardController(ICustomerDashboardService service)
        {
            _service = service;
        }

        /// <summary>
        /// Bảng điều khiển của khách: những việc đang chờ chính khách xử lý, gộp cả đơn ký
        /// gửi lẫn đơn mua hộ, kèm số đơn đang chạy theo từng chặng.
        /// </summary>
        /// <remarks>
        /// Một lời gọi thay cho ba bốn lời gọi danh sách mà màn hình phải tự cộng. Khách
        /// chỉ thấy dữ liệu của chính mình: hồ sơ khách tra từ tài khoản trong token,
        /// không nhận customerId từ tham số.
        /// </remarks>
        /// <response code="200">Trả bảng việc cần làm (kể cả khi không có việc nào)</response>
        [HttpGet("api/customers/me/dashboard")]
        [ProducesResponseType(typeof(CustomerDashboardResponse), 200)]
        public async Task<IActionResult> GetMyDashboard()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            var data = await _service.GetAsync(userId);
            return Ok(new { message = "Lấy bảng việc cần làm thành công.", data });
        }
    }
}
