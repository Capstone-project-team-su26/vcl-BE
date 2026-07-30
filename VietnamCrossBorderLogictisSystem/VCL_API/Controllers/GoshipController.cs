using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GoshipController : ControllerBase
    {
        private readonly IGoshipService _goshipService;
        private readonly IConfiguration _configuration;

        public GoshipController(IGoshipService goshipService, IConfiguration configuration)
        {
            _goshipService = goshipService;
            _configuration = configuration;
        }

        /// <summary>
        /// Ước tính và so sánh phí giao hàng của tất cả các hãng vận chuyển qua GoShip Express.
        /// </summary>
        [HttpPost("rates")]
        public async Task<IActionResult> GetRates([FromBody] GoshipRateRequest request, [FromQuery] string? token = null)
        {
            try
            {
                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GoshipSettings:Token"];
                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
                }

                var rates = await _goshipService.GetRatesAsync(request, effectiveToken);
                return Ok(new { message = "Lấy danh sách bảng giá cước GoShip thành công.", data = rates });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Đăng đơn tự động sang GoShip Express.
        /// </summary>
        [HttpPost("orders")]
        [Authorize(Roles = "Admin,OperationsManager,WarehouseStaff")]
        public async Task<IActionResult> CreateOrder([FromBody] GoshipOrderRequest request, [FromQuery] string? token = null)
        {
            try
            {
                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GoshipSettings:Token"];
                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
                }

                var order = await _goshipService.CreateOrderAsync(request, effectiveToken);
                return Ok(new { message = "Tạo đơn hàng GoShip thành công.", data = order });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Webhook tiếp nhận tự động cập nhật trạng thái đơn từ GoShip Express.
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> ProcessWebhook([FromBody] GoshipWebhookPayload payload)
        {
            try
            {
                await _goshipService.ProcessWebhookAsync(payload);
                return Ok(new { code = 200, status = "success", message = "Webhook processed successfully" });
            }
            catch (Exception ex)
            {
                return Ok(new { code = 200, status = "success", warning = ex.Message });
            }
        }
    }
}
