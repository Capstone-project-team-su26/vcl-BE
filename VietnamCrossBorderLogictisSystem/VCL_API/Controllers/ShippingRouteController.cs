using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/shipping-routes")]
    [ApiController]
    [Authorize]
    public class ShippingRouteController : ControllerBase
    {
        private readonly IShippingRouteService _shippingRouteService;

        public ShippingRouteController(IShippingRouteService shippingRouteService)
        {
            _shippingRouteService = shippingRouteService;
        }

        /// <summary>
        /// Danh sách tuyến vận chuyển quốc tế đã cấu hình.
        /// </summary>
        /// <param name="transportMode">Lọc theo hình thức: AIR, SEA, ROAD, RAIL</param>
        /// <param name="isActive">Lọc theo trạng thái đang hoạt động</param>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? transportMode, [FromQuery] bool? isActive)
        {
            var data = await _shippingRouteService.GetAllAsync(transportMode, isActive);
            return Ok(new { message = "Lấy danh sách tuyến vận chuyển thành công.", data });
        }

        /// <summary>
        /// Lấy chi tiết một tuyến vận chuyển.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var data = await _shippingRouteService.GetByIdAsync(id);
                return Ok(new { message = "Lấy chi tiết tuyến vận chuyển thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm tuyến vận chuyển mới.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateShippingRouteRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _shippingRouteService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new { message = "Thêm tuyến vận chuyển thành công.", data = created });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Cập nhật tuyến vận chuyển.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShippingRouteRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _shippingRouteService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật tuyến vận chuyển thành công." });
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
        /// Xóa tuyến vận chuyển.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _shippingRouteService.DeleteAsync(id);
                return Ok(new { message = "Xóa tuyến vận chuyển thành công." });
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
