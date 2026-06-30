using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly IWarehouseService _warehouseService;

        public WarehouseController(IWarehouseService warehouseService)
        {
            _warehouseService = warehouseService;
        }

        /// <summary>
        /// Lấy danh sách các kho nội địa.
        /// Cả Admin và Sale đều có thể gọi (Sale lọc isActive=true).
        /// </summary>
        [HttpGet("api/warehouses")]
        [Authorize(Roles = "Admin,Sale")]
        public async Task<IActionResult> GetWarehouses([FromQuery] bool? isActive, [FromQuery] string? search)
        {
            var result = await _warehouseService.GetWarehousesAsync(isActive, search);
            return Ok(new
            {
                items = result
            });
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
                return BadRequest(new { message = ex.Message });
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
                return BadRequest(new { message = ex.Message });
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
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
