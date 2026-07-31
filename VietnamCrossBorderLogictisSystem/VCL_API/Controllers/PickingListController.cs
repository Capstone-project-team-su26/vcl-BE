using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/picking-lists")]
    [ApiController]
    [Authorize]
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
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Quét mã vạch kiểm đếm mặt hàng khi lấy hàng khỏi vị trí (Warehouse Staff / Staff)
        /// </summary>
        [HttpPost("{pickingListId}/scan-item")]
        [Authorize(Roles = "WarehouseStaff,Staff,OperationsManager,Operations,Admin")]
        public async Task<IActionResult> ScanPickingItem(Guid pickingListId, [FromBody] ScanPickingItemRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _pickingListService.ScanPickingItemAsync(pickingListId, userId, userRole, request);
                return Ok(new { message = "Quét mã xác nhận mặt hàng thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Xác nhận đã lấy hàng khỏi vị trí kho theo phiếu picking (Warehouse Staff, Staff hoặc Operations)
        /// </summary>
        [HttpPut("{pickingListId}/confirm")]

        [Authorize(Roles = "WarehouseStaff,Staff,OperationsManager,Operations,Admin")]
        public async Task<IActionResult> ConfirmPicking(Guid pickingListId, [FromBody] ConfirmPickingRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _pickingListService.ConfirmPickingListAsync(pickingListId, userId, userRole, request);
                return Ok(new { message = "Xác nhận picking thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }
    }
}
