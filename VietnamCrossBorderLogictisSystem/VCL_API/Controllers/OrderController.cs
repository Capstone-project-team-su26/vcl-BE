using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Interface;
using VCL_BLL.DTOs;

namespace VCL_API.Controllers
{
    [ApiController]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        /// <summary>
        /// Lấy danh sách yêu cầu ký gửi
        /// </summary>
        /// <param name="pageNumber">Số trang (mặc định 1)</param>
        /// <param name="pageSize">Số lượng item mỗi trang (mặc định 10)</param>
        /// <param name="status">Trạng thái yêu cầu (tuỳ chọn)</param>
        /// <param name="searchCode">Mã yêu cầu ký gửi để tìm kiếm (tuỳ chọn)</param>
        /// <returns>Danh sách yêu cầu ký gửi phân trang</returns>
        [HttpGet("api/orders/consignments")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetConsignments(
            [FromQuery] int pageNumber = 1, 
            [FromQuery] int pageSize = 10, 
            [FromQuery] string? status = null, 
            [FromQuery] string? searchCode = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _orderService.GetConsignmentsAsync(userId, role, pageNumber, pageSize, status, searchCode);
                return Ok(new
                {
                    message = "Lấy danh sách yêu cầu ký gửi thành công.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
