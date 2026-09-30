using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_BLL.Services;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly IWarehouseService _warehouseService;
        private readonly UserWarehouseService _userWarehouses;

        public WarehouseController(IWarehouseService warehouseService, UserWarehouseService userWarehouses)
        {
            _warehouseService = warehouseService;
            _userWarehouses = userWarehouses;
        }

        private Guid? CurrentUserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        private string CurrentRole => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>
        /// Lấy danh sách các kho vận hành (chỉ Admin).
        /// </summary>
        [HttpGet("api/warehouses")]
        [Authorize(Roles = "Admin,ADMIN")]
        public async Task<IActionResult> GetWarehouses(
            [FromQuery] bool? isActive, 
            [FromQuery] string? search,
            [FromQuery] string? regionCode = null,
            [FromQuery] string? region = null)
        {
            var effectiveRegion = !string.IsNullOrWhiteSpace(regionCode) ? regionCode : region;
            if (string.IsNullOrWhiteSpace(effectiveRegion))
            {
                var tokenRegion = User.FindFirst("region")?.Value;
                if (!string.IsNullOrWhiteSpace(tokenRegion))
                {
                    effectiveRegion = tokenRegion;
                }
            }

            var result = await _warehouseService.GetWarehousesAsync(isActive, search, effectiveRegion);
            return Ok(new
            {
                items = result
            });
        }

        /// <summary>
        /// Lấy danh sách các kho đang hoạt động (dùng cho nghiệp vụ, tất cả các role hệ thống).
        /// </summary>
        [HttpGet("api/warehouses/active")]
        [AuthorizeRoles("Admin,ADMIN,Sale,SALE,Staff,STAFF,WarehouseStaff,WAREHOUSESTAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Customer,CUSTOMER")]
        public async Task<IActionResult> GetActiveWarehouses(
            [FromQuery] string? regionCode = null,
            [FromQuery] string? region = null)
        {
            var effectiveRegion = !string.IsNullOrWhiteSpace(regionCode) ? regionCode : region;

            // Nhân viên kho đã được gán kho: chỉ thấy đúng các kho đó (đọc từ DB, không phụ thuộc
            // vùng trong token nên gán/bỏ gán có hiệu lực ngay).
            var assigned = await _userWarehouses.GetListScopeAsync(CurrentUserId, CurrentRole);
            if (assigned != null)
            {
                var own = await _warehouseService.GetActiveWarehousesAsync(effectiveRegion);
                return Ok(new { items = own.Where(w => assigned.Contains(w.Id)).ToList() });
            }

            if (string.IsNullOrWhiteSpace(effectiveRegion))
            {
                var tokenRegion = User.FindFirst("region")?.Value;
                if (!string.IsNullOrWhiteSpace(tokenRegion))
                {
                    effectiveRegion = tokenRegion;
                }
            }

            var result = await _warehouseService.GetActiveWarehousesAsync(effectiveRegion);
            return Ok(new { items = result });
        }

        /// <summary>
        /// Thêm mới một kho vận hành (chỉ Admin).
        /// </summary>
        [HttpPost("api/warehouses")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateWarehouse([FromBody] CreateWarehouseRequestDto request)
        {
            try
            {
                var result = await _warehouseService.CreateWarehouseAsync(request);
                return StatusCode(201, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Cập nhật thông tin kho vận hành (chỉ Admin).
        /// </summary>
        [HttpPut("api/warehouses/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateWarehouse(Guid id, [FromBody] CreateWarehouseRequestDto request)
        {
            try
            {
                var result = await _warehouseService.UpdateWarehouseAsync(id, request);
                return Ok(result);
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

        /// <summary>
        /// Xóa kho vận hành (chỉ Admin).
        /// </summary>
        [HttpDelete("api/warehouses/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteWarehouse(Guid id)
        {
            try
            {
                await _warehouseService.DeleteWarehouseAsync(id);
                return Ok(new { message = "Xóa kho thành công." });
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

        /// <summary>
        /// Lấy danh sách vị trí kho.
        /// </summary>
        [HttpGet("api/warehouses/{warehouseId}/locations")]
        [AuthorizeRoles("Admin,ADMIN,Sale,SALE,Sales,SALES,Staff,STAFF,WarehouseStaff,WAREHOUSESTAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> GetWarehouseLocations(Guid warehouseId, [FromQuery] bool? isActive)
        {
            try
            {
                var result = await _warehouseService.GetWarehouseLocationsAsync(warehouseId, isActive);
                return Ok(new { items = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo mới vị trí kho (Admin, Operations, Warehouse Staff).
        /// </summary>
        [HttpPost("api/warehouses/{warehouseId}/locations")]
        [AuthorizeRoles("Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF")]
        public async Task<IActionResult> CreateWarehouseLocation(Guid warehouseId, [FromBody] CreateLocationRequestDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var result = await _warehouseService.CreateWarehouseLocationAsync(warehouseId, request);
                return StatusCode(201, result);
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

        /// <summary>
        /// Cập nhật thông tin vị trí kho (Admin, Operations, Warehouse Staff).
        /// </summary>
        [HttpPut("api/warehouse-locations/{locationId}")]
        [AuthorizeRoles("Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF")]
        public async Task<IActionResult> UpdateWarehouseLocation(Guid locationId, [FromBody] CreateLocationRequestDto request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var result = await _warehouseService.UpdateWarehouseLocationAsync(locationId, request);
                return Ok(result);
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

        /// <summary>
        /// Xóa vị trí kho (Admin, Operations).
        /// </summary>
        [HttpDelete("api/warehouse-locations/{locationId}")]
        [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> DeleteWarehouseLocation(Guid locationId)
        {
            try
            {
                await _warehouseService.DeleteWarehouseLocationAsync(locationId);
                return Ok(new { message = "Xóa vị trí kho thành công." });
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

        /// <summary>
        /// Lấy danh sách vị trí kho đang hoạt động (dùng cho nghiệp vụ, tất cả các role hệ thống).
        /// </summary>
        [HttpGet("api/warehouses/{warehouseId}/locations/active")]
        [AuthorizeRoles("Admin,Sale,Staff,WarehouseStaff,OperationsManager,Operations")]
        public async Task<IActionResult> GetActiveWarehouseLocations(Guid warehouseId)
        {
            try
            {
                var result = await _warehouseService.GetWarehouseLocationsAsync(warehouseId, true);
                return Ok(new { items = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
