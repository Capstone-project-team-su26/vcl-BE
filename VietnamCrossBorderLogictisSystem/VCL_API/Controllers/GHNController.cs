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
        /// Webhook tiếp nhận tự động cập nhật trạng thái đơn từ Giao Hàng Nhanh (GHN Callback).
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> ProcessWebhook([FromBody] GHNWebhookPayload payload)
        {
            try
            {
                var success = await _ghnService.ProcessWebhookAsync(payload);
                if (success)
                {
                    return Ok(new { message = "Đã xử lý webhook đồng bộ trạng thái GHN thành công." });
                }
                return Ok(new { message = "Đã nhận webhook GHN (Không có vận đơn tương ứng cần cập nhật)." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
