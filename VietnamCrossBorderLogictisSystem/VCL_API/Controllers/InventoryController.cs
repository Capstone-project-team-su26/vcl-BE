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
    [Route("api/inventories")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Ghi nhận tồn kho khả dụng từ kiện hàng đã put-away thành công
        /// </summary>
        /// <param name="parcelId">ID của kiện hàng</param>
        [HttpPost("from-parcel/{parcelId}")]
        [Authorize(Roles = "Admin,WarehouseStaff")]
        public async Task<IActionResult> RecordInventoryFromParcel(Guid parcelId)
        {
            try
            {
                var result = await _inventoryService.RecordInventoryFromParcelAsync(parcelId);
                return Created(string.Empty, new { message = "Ghi nhận tồn kho khả dụng thành công.", data = result });
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
        /// Lấy danh sách hàng tồn kho (loại bỏ các dòng zombie AVAILABLE có số lượng 0).
        /// Mỗi dòng kèm thông tin khách hàng, mã ký gửi, mã kiện, ô kệ, kho,
        /// thời điểm bắt đầu lưu kho, cân nặng và thể tích thực tế.
        /// </summary>
        /// <param name="status">Lọc theo trạng thái tồn kho, ví dụ AVAILABLE</param>
        /// <param name="customerId">Lọc theo khách hàng sở hữu hàng tồn</param>
        /// <param name="warehouseId">Lọc theo kho đang lưu hàng</param>
        [HttpGet]
        public async Task<IActionResult> GetInventories([FromQuery] string? status, [FromQuery] Guid? customerId, [FromQuery] Guid? warehouseId)
        {
            var result = await _inventoryService.GetInventoriesAsync(status, customerId, warehouseId);
            return Ok(new { message = "Lấy danh sách tồn kho thành công.", items = result });
        }

        /// <summary>
        /// Điều chuyển kiện hàng của một bản ghi tồn kho sang ô kệ khác trong cùng kho.
        /// </summary>
        /// <param name="inventoryId">ID bản ghi tồn kho cần điều chuyển</param>
        /// <param name="request">Ô kệ đích và lý do điều chuyển</param>
        [HttpPost("{inventoryId}/relocate")]
        [Authorize(Roles = "Admin,WarehouseStaff")]
        public async Task<IActionResult> RelocateInventory(Guid inventoryId, [FromBody] RelocateInventoryRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

            try
            {
                var result = await _inventoryService.RelocateInventoryAsync(inventoryId, request, userId);
                return Ok(new { message = "Điều chuyển vị trí kiện hàng thành công.", data = result });
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
        /// Lấy nhật ký di chuyển kiện hàng trong kho, sắp xếp mới nhất trước.
        /// </summary>
        /// <param name="warehouseId">Lọc theo kho diễn ra thao tác</param>
        /// <param name="parcelId">Lọc theo kiện hàng</param>
        /// <param name="limit">Số dòng tối đa, mặc định 50</param>
        [HttpGet("movements")]
        public async Task<IActionResult> GetMovements([FromQuery] Guid? warehouseId, [FromQuery] Guid? parcelId, [FromQuery] int limit = 50)
        {
            var result = await _inventoryService.GetMovementsAsync(warehouseId, parcelId, limit);
            return Ok(new { message = "Lấy nhật ký di chuyển kiện hàng thành công.", items = result });
        }
    }
}
