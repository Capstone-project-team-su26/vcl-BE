using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
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
    [EnableCors("AllowAll")]
    [Route("api/picking-lists")]
    [ApiController]
    [Authorize]
    /// <summary>
    /// Danh sách soạn hàng của luồng cũ — chỉ còn phần ĐỌC để tra dữ liệu đã có. Việc bốc hàng
    /// nay làm ở POST /api/warehouse-release-requests/{id}/pick, nơi kiện được chuyển thật sang
    /// ô ở khu xuất.
    /// </summary>
    public class PickingListController : ControllerBase
    {
        private readonly IPickingListService _pickingListService;

        public PickingListController(IPickingListService pickingListService)
        {
            _pickingListService = pickingListService;
        }

        /// <summary>
        /// Lấy chi tiết phiếu picking theo ID
        /// </summary>
        [HttpGet("{pickingListId}")]
        public async Task<IActionResult> GetPickingListById(Guid pickingListId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _pickingListService.GetPickingListByIdAsync(pickingListId, userId, userRole);
                return Ok(new { message = "Lấy chi tiết phiếu picking thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }

    }
}
