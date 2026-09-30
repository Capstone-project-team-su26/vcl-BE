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
    [Route("api/shipping-methods")]
    [ApiController]
    [Authorize]
    public class ShippingMethodsController : ControllerBase
    {
        private readonly IShippingMethodService _service;

        public ShippingMethodsController(IShippingMethodService service)
        {
            _service = service;
        }

        /// <summary>Admin xem toàn bộ phương thức vận chuyển.</summary>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAll()
        {
            var data = await _service.GetAllAsync(activeOnly: false);
            return Ok(new { message = "Lấy danh sách phương thức vận chuyển thành công.", data });
        }

        /// <summary>Danh sách phương thức đang hoạt động (cho Customer/Staff chọn khi tạo ký gửi/báo giá).</summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActive()
        {
            var data = await _service.GetAllAsync(activeOnly: true);
            return Ok(new { message = "Lấy danh sách phương thức vận chuyển đang hoạt động thành công.", data });
        }

        /// <summary>Lấy chi tiết một phương thức vận chuyển.</summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var data = await _service.GetByIdAsync(id);
                return Ok(new { message = "Lấy chi tiết phương thức vận chuyển thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Admin tạo phương thức vận chuyển mới.</summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateShippingMethodRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await _service.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id },
                    new { message = "Tạo phương thức vận chuyển thành công.", data = created });
            }
            catch (ConflictException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>Admin cập nhật phương thức vận chuyển (bao gồm vô hiệu hóa qua isActive).</summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShippingMethodRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                await _service.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật phương thức vận chuyển thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ConflictException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>Admin xóa phương thức vận chuyển.</summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return Ok(new { message = "Xóa phương thức vận chuyển thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
