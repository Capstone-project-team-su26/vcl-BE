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
    [Route("api/pricing-rules")]
    [ApiController]
    [Authorize]
    public class PricingRuleController : ControllerBase
    {
        private readonly IPricingRuleService _pricingRuleService;

        public PricingRuleController(IPricingRuleService pricingRuleService)
        {
            _pricingRuleService = pricingRuleService;
        }

        /// <summary>
        /// Xem danh sách cấu hình bảng giá.
        /// Truyền <c>orderType=CONSIGNMENT</c> để lấy đúng bộ dịch vụ bổ sung của đơn ký gửi
        /// (đã loại phí mua hộ/VAT/thuế nhập khẩu), hoặc <c>orderType=PURCHASE</c> cho luồng mua hộ.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll([FromQuery] string? orderType = null)
        {
            var rules = await _pricingRuleService.GetAllAsync(orderType);
            return Ok(rules);
        }

        /// <summary>
        /// Lấy chi tiết cấu hình giá theo ID
        /// </summary>
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var rule = await _pricingRuleService.GetByIdAsync(id);
                return Ok(rule);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm cấu hình giá mới
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreatePricingRuleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _pricingRuleService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Cập nhật cấu hình giá
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePricingRuleRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _pricingRuleService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật cấu hình giá thành công." });
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
        /// Xóa cấu hình giá
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _pricingRuleService.DeleteAsync(id);
                return Ok(new { message = "Xóa cấu hình giá thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
