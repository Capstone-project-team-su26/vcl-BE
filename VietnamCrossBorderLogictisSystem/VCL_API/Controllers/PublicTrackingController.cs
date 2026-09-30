using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [ApiController]
    [AllowAnonymous]
    public class PublicTrackingController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public PublicTrackingController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        /// <summary>
        /// Tra cứu công khai trạng thái đơn ký gửi theo mã vận đơn (không cần đăng nhập).
        /// </summary>
        [HttpGet("api/public/parcels/tracking")]
        public async Task<IActionResult> TrackByCode([FromQuery] string code)
        {
            try
            {
                var result = await _orderService.TrackConsignmentByCodeAsync(code);
                return Ok(new { message = "Tra cứu mã vận đơn thành công.", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }
    }
}
