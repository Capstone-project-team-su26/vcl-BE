using VCL_API.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.Exceptions;
using VCL_BLL.Services;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>Quản lý phụ trách duyệt nhập/xuất của từng kho.</summary>
    [ApiController]
    [Authorize]
    public class WarehouseManagerController : ControllerBase
    {
        private readonly WarehouseManagerService _service;

        public WarehouseManagerController(WarehouseManagerService service)
        {
            _service = service;
        }

        /// <summary>Quản lý hiện tại của kho và lịch sử đổi quản lý.</summary>
        [HttpGet("api/warehouses/{warehouseId}/manager")]
        [AuthorizeRoles("Admin,ADMIN,Administrator,OperationsManager,OPERATIONSMANAGER,Operations,Sale,SALE,WarehouseStaff,WAREHOUSESTAFF")]
        public async Task<IActionResult> Get(Guid warehouseId)
        {
            try
            {
                return Ok(new { data = await _service.GetAsync(warehouseId) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Admin gán hoặc đổi quản lý kho, bắt buộc lý do. Phiếu đang chờ duyệt chuyển sang quản lý mới.
        /// </summary>
        [HttpPut("api/warehouses/{warehouseId}/manager")]
        [Authorize(Roles = "Admin,ADMIN,Administrator,ADMINISTRATOR")]
        public async Task<IActionResult> Assign(Guid warehouseId, [FromBody] AssignWarehouseManagerRequest request)
        {
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var actorId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                return Ok(new { message = "Đã cập nhật quản lý kho.", data = await _service.AssignAsync(warehouseId, request, actorId) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }
    }
}
