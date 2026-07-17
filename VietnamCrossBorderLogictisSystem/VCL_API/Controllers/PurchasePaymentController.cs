using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    public class PurchasePaymentController : ControllerBase
    {
        private readonly IPurchasePaymentService _purchasePaymentService;
        private readonly IConsignmentPaymentService _consignmentPaymentService;
        private readonly IPayOsService _payOsService;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        public PurchasePaymentController(
            IPurchasePaymentService purchasePaymentService,
            IConsignmentPaymentService consignmentPaymentService,
            IPayOsService payOsService,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _purchasePaymentService = purchasePaymentService;
            _consignmentPaymentService = consignmentPaymentService;
            _payOsService = payOsService;
            _configuration = configuration;
        }

        /// <summary>
        /// Đăng ký (confirm) webhook URL với payOS — chạy MỘT LẦN sau khi deploy
        /// (hoặc khi đổi domain). Không truyền body thì dùng PayOS:WebhookUrl trong config.
        /// payOS sẽ gọi thử URL ngay lúc đăng ký nên URL phải public + HTTPS.
        /// </summary>
        [HttpPost("api/payments/payos/confirm-webhook")]
        [Authorize(Roles = "ADMIN,Admin")]
        public async Task<IActionResult> ConfirmPayOsWebhook([FromBody] ConfirmWebhookRequestDto request)
        {
            var webhookUrl = request?.WebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl))
                webhookUrl = _configuration["PayOS:WebhookUrl"];

            if (string.IsNullOrWhiteSpace(webhookUrl))
                return BadRequest(new { message = "Thiếu webhook URL. Truyền webhookUrl trong body hoặc cấu hình PayOS:WebhookUrl." });

            try
            {
                await _payOsService.ConfirmWebhookAsync(webhookUrl);
                return Ok(new { message = "Đăng ký webhook với payOS thành công.", webhookUrl });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Customer xác nhận báo giá mua hộ và tạo thanh toán (payOS). Trả về link thanh toán cho FE.
        /// </summary>
        [HttpPut("api/purchase-requests/{requestId}/quotation/confirm-and-pay")]
        [Authorize]
        public async Task<IActionResult> ConfirmQuotationAndPay(Guid requestId, [FromBody] ConfirmQuotationAndPayRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchasePaymentService.ConfirmQuotationAndPayAsync(requestId, userId, userRole, request);
                return Ok(new { message = "Xác nhận báo giá và tạo thanh toán thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Webhook nhận callback thanh toán từ payOS. payOS gọi trực tiếp, không yêu cầu đăng nhập.
        /// </summary>
        [HttpPost("api/payments/payos/webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> PayOsWebhook()
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            try
            {
                // payOS chỉ có một webhook URL cho cả 2 loại thanh toán:
                // thử khớp thanh toán mua hộ trước, nếu không thuộc thì thử thanh toán ký gửi.
                var handled = await _purchasePaymentService.HandlePayOsWebhookAsync(rawBody);
                if (!handled)
                {
                    handled = await _consignmentPaymentService.HandlePayOsWebhookAsync(rawBody);
                }
                // Luôn trả 200 để payOS ngừng gửi lại; kèm cờ đã ghi nhận hay chưa
                return Ok(new { success = true, handled });
            }
            catch (Exception ex)
            {
                // Chữ ký không hợp lệ hoặc dữ liệu sai -> báo lỗi để payOS biết
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}
