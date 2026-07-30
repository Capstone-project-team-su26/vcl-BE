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
    [Route("api/ghn")]
    public class GHNController : ControllerBase
    {
        private readonly IGHNService _ghnService;
        private readonly IConfiguration _configuration;

        public GHNController(IGHNService ghnService, IConfiguration configuration)
        {
            _ghnService = ghnService;
            _configuration = configuration;
        }

        /// <summary>
        /// Đăng đơn tự động sang Giao Hàng Nhanh (GHN API v2).
        /// </summary>
        [HttpPost("orders")]
        [Authorize(Roles = "Admin,Sale,WarehouseStaff,OperationsManager")]
        public async Task<IActionResult> CreateOrder([FromBody] GHNOrderRequest request, [FromQuery] string? token = null, [FromQuery] int? shopId = null)
        {
            try
            {
                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GHNSettings:Token"];
                var effectiveShopId = shopId ?? int.Parse(_configuration["GHNSettings:ShopId"] ?? "6575712");

                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình GHN API Token trong hệ thống." });
                }

                var result = await _ghnService.CreateOrderAsync(request, effectiveToken, effectiveShopId);
                return Ok(new { message = "Đăng đơn sang Giao Hàng Nhanh thành công.", data = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Ước tính phí giao hàng qua Giao Hàng Nhanh (GHN).
        /// </summary>
        [HttpPost("calculate-fee")]
        [Authorize]
        public async Task<IActionResult> CalculateFee([FromBody] GHNCalculateFeeRequest request, [FromQuery] string? token = null, [FromQuery] int? shopId = null)
        {
            try
            {
                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GHNSettings:Token"];
                var effectiveShopId = shopId ?? int.Parse(_configuration["GHNSettings:ShopId"] ?? "6575712");

                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình GHN API Token trong hệ thống." });
                }

                var result = await _ghnService.CalculateFeeAsync(request, effectiveToken, effectiveShopId);
                return Ok(new { message = "Ước tính phí giao hàng GHN thành công.", data = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Tra cứu thông tin & trạng thái trực tiếp đơn hàng GHN (Tracking Order API).
        /// </summary>
        [HttpGet("orders/{orderCode}")]
        [Authorize]
        public async Task<IActionResult> GetOrderDetail(string orderCode, [FromQuery] string? token = null)
        {
            try
            {
                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GHNSettings:Token"];

                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình GHN API Token trong hệ thống." });
                }

                var result = await _ghnService.GetOrderDetailAsync(orderCode, effectiveToken);
                return Ok(new { message = "Lấy chi tiết vận đơn GHN thành công.", data = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Gửi yêu cầu đăng ký/cập nhật Webhook URL sang GHN API.
        /// </summary>
        [HttpPost("register-webhook")]
        [Authorize(Roles = "Admin,OperationsManager")]
        public async Task<IActionResult> RegisterWebhook([FromQuery] string? webhookUrl = null, [FromQuery] string? token = null)
        {
            try
            {
                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GHNSettings:Token"];
                var targetWebhookUrl = !string.IsNullOrEmpty(webhookUrl) ? webhookUrl : "https://api-vcl.zushin.io.vn/api/ghn/webhook";

                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình GHN API Token trong hệ thống." });
                }

                var success = await _ghnService.RegisterWebhookUrlAsync(targetWebhookUrl, effectiveToken);
                return Ok(new { message = "Yêu cầu đăng ký Webhook URL tới GHN đã được gửi thành công.", webhookUrl = targetWebhookUrl, success });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Webhook tiếp nhận tự động cập nhật trạng thái đơn từ Giao Hàng Nhanh (GHN Callback).
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> ProcessWebhook([FromBody] GHNWebhookPayload payload)
        {
            try
            {
                await _ghnService.ProcessWebhookAsync(payload);
                return Ok(new { code = 200, message = "Success" });
            }
            catch (Exception ex)
            {
                // Always return HTTP 200 OK so GHN won't retry 10 times unnecessarily
                return Ok(new { code = 200, message = "Success", warning = ex.Message });
            }
        }
    }
}
