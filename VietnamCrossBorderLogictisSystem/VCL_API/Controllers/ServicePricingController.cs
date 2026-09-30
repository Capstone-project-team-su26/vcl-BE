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
    [Route("api/service-pricings")]
    [ApiController]
    [Authorize]
    public class ServicePricingController : ControllerBase
    {
        private readonly IServicePricingService _servicePricingService;

        public ServicePricingController(IServicePricingService servicePricingService)
        {
            _servicePricingService = servicePricingService;
        }

        /// <summary>
        /// Xem danh sách bảng giá dịch vụ
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll()
        {
            var pricings = await _servicePricingService.GetAllAsync();
            return Ok(pricings);
        }

        /// <summary>
        /// Lấy chi tiết bảng giá dịch vụ theo ID
        /// </summary>
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var pricing = await _servicePricingService.GetByIdAsync(id);
                return Ok(pricing);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm cấu hình bảng giá dịch vụ mới
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateServicePricingRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _servicePricingService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
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
        /// Cập nhật cấu hình bảng giá dịch vụ
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServicePricingRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _servicePricingService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật bảng giá dịch vụ thành công." });
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
        /// Xóa cấu hình bảng giá dịch vụ
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _servicePricingService.DeleteAsync(id);
                return Ok(new { message = "Xóa bảng giá dịch vụ thành công." });
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
