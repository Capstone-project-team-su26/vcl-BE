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
    [Route("api/carriers")]
    public class CarrierController : ControllerBase
    {
        private readonly ICarrierService _carrierService;

        public CarrierController(ICarrierService carrierService)
        {
            _carrierService = carrierService;
        }

        /// <summary>
        /// Xem danh sách tất cả các đơn vị vận chuyển (chỉ Admin).
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,ADMIN")]
        public async Task<IActionResult> GetAll()
        {
            var carriers = await _carrierService.GetAllAsync();
            return Ok(carriers);
        }

        /// <summary>
        /// Lấy danh sách các đơn vị vận chuyển đang hoạt động (dùng cho nghiệp vụ, Staff hoặc Operations).
        /// </summary>
        [HttpGet("active")]
        [AllowAnonymous]
        public async Task<IActionResult> GetActive()
        {
            var carriers = await _carrierService.GetActiveAsync();
            return Ok(carriers);
        }

        /// <summary>
        /// Lấy thông tin chi tiết một đơn vị vận chuyển theo ID (chỉ Admin).
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var carrier = await _carrierService.GetByIdAsync(id);
                return Ok(carrier);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo mới một đơn vị vận chuyển (chỉ Admin).
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateCarrierRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _carrierService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật thông tin đơn vị vận chuyển (chỉ Admin).
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCarrierRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _carrierService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật đơn vị vận chuyển thành công." });
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
        /// Xóa đơn vị vận chuyển (chỉ Admin).
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _carrierService.DeleteAsync(id);
                return Ok(new { message = "Xóa đơn vị vận chuyển thành công." });
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
