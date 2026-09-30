using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.Exceptions;
using VCL_BLL.Services;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Admin gán nhân viên kho vào từng kho cụ thể. Nhân viên có kho được gán chỉ thao tác được ở
    /// đúng các kho đó; chưa gán thì giới hạn theo vùng như cũ.
    /// </summary>
    [ApiController]
    [Authorize]
    public class UserWarehouseController : ControllerBase
    {
        private readonly UserWarehouseService _service;

        public UserWarehouseController(UserWarehouseService service)
        {
            _service = service;
        }

        /// <summary>Các kho đang gán cho một tài khoản.</summary>
        [HttpGet("api/users/{userId:guid}/warehouses")]
        [Authorize(Roles = "Admin,ADMIN,Administrator,ADMINISTRATOR,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> GetForUser(Guid userId)
        {
            try
            {
                return Ok(new { data = await _service.GetForUserAsync(userId) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Admin thay toàn bộ danh sách kho của tài khoản (mảng rỗng = bỏ gán hết). Các kho phải đang
        /// hoạt động và cùng một vùng; tài khoản trống vùng thì tự đặt vùng theo kho.
        /// </summary>
        [HttpPut("api/users/{userId:guid}/warehouses")]
        [Authorize(Roles = "Admin,ADMIN,Administrator,ADMINISTRATOR")]
        public async Task<IActionResult> Assign(Guid userId, [FromBody] AssignUserWarehousesRequest request)
        {
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var actorId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var data = await _service.AssignAsync(userId, request ?? new AssignUserWarehousesRequest(), actorId);
                var message = data.Warehouses.Count == 0
                    ? "Đã bỏ gán kho. Tài khoản quay về giới hạn theo vùng."
                    : data.RegionChanged
                        ? $"Đã cập nhật kho phụ trách và đặt vùng tài khoản = {data.Region}."
                        : "Đã cập nhật kho phụ trách.";
                return Ok(new { message, data });
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

        /// <summary>Kho phụ trách của chính tài khoản đang đăng nhập (app kho dùng để khoá chọn kho).</summary>
        [HttpGet("api/users/me/warehouses")]
        public async Task<IActionResult> GetMine()
        {
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                return Ok(new { data = await _service.GetForUserAsync(userId) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Nhân viên được gán vào một kho (chỉ xem).</summary>
        [HttpGet("api/warehouses/{warehouseId:guid}/staff")]
        [Authorize(Roles = "Admin,ADMIN,Administrator,ADMINISTRATOR,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Sale,SALE,Sales,SALES")]
        public async Task<IActionResult> GetStaffOfWarehouse(Guid warehouseId)
        {
            try
            {
                return Ok(new { data = await _service.GetStaffOfWarehouseAsync(warehouseId) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
