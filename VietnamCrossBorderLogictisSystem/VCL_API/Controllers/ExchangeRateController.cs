using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/exchange-rates")]
    [ApiController]
    [Authorize]
    public class ExchangeRateController : ControllerBase
    {
        private readonly IExchangeRateService _exchangeRateService;

        public ExchangeRateController(IExchangeRateService exchangeRateService)
        {
            _exchangeRateService = exchangeRateService;
        }

        /// <summary>
        /// Danh sách tỷ giá quy đổi sang VND.
        /// Truyền <c>activeOnly=true</c> cho dropdown chọn ngoại tệ lúc Sale lập báo giá.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = false)
        {
            var rates = await _exchangeRateService.GetAllAsync(activeOnly);
            return Ok(rates);
        }

        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Sale,OperationsManager")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                return Ok(await _exchangeRateService.GetByIdAsync(id));
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Quy đổi thử một số tiền — Sale xem trước số VND trước khi chốt báo giá.</summary>
        [HttpGet("convert")]
        [Authorize(Roles = "Admin,Sale,OperationsManager")]
        public async Task<IActionResult> Convert([FromQuery] string currency, [FromQuery] decimal amount)
        {
            try
            {
                return Ok(await _exchangeRateService.ConvertToVndAsync(currency, amount));
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

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateExchangeRateRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var created = await _exchangeRateService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExchangeRateRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                return Ok(await _exchangeRateService.UpdateAsync(id, request));
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

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _exchangeRateService.DeleteAsync(id);
                return NoContent();
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
