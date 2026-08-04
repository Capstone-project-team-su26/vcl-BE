using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/additional-service-fees")]
    [ApiController]
    [Authorize]
    public class AdditionalServiceFeeController : ControllerBase
    {
        private readonly IAdditionalServiceFeeService _service;

        public AdditionalServiceFeeController(IAdditionalServiceFeeService service)
        {
            _service = service;
        }

        /// <summary>
        /// Xem danh sách phí dịch vụ bổ sung.
        /// </summary>
        /// <param name="activeOnly">Nếu true, chỉ lấy các loại phí đang hoạt động. Mặc định là false</param>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = false)
        {
            var data = await _service.GetAllAsync(activeOnly);
            return Ok(new { message = "Lấy danh sách cấu hình phí dịch vụ bổ sung thành công.", data });
        }

        /// <summary>
        /// Lấy chi tiết một loại phí dịch vụ bổ sung.
        /// </summary>
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var data = await _service.GetByIdAsync(id);
                return Ok(new { message = "Lấy chi tiết cấu hình phí dịch vụ bổ sung thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Admin tạo mới một loại phí dịch vụ bổ sung.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateAdditionalServiceFeeRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await _service.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id },
                    new { message = "Tạo cấu hình phí dịch vụ bổ sung thành công.", data = created });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Admin cập nhật thông tin loại phí dịch vụ bổ sung (bao gồm bật/tắt kích hoạt qua isActive).
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAdditionalServiceFeeRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                await _service.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật cấu hình phí dịch vụ bổ sung thành công." });
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
        /// Admin xóa loại phí dịch vụ bổ sung.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _service.DeleteAsync(id);
                return Ok(new { message = "Xóa cấu hình phí dịch vụ bổ sung thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
