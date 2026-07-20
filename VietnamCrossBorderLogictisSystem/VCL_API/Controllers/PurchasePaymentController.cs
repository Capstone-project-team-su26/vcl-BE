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

        public PurchasePaymentController(
            IPurchasePaymentService purchasePaymentService,
            IConsignmentPaymentService consignmentPaymentService)
        {
            _purchasePaymentService = purchasePaymentService;
            _consignmentPaymentService = consignmentPaymentService;
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
        [HttpPost("api/payments/payos-webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> PayOsWebhook()
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            // payOS chỉ chấp nhận đăng ký webhook khi URL trả 2xx. Vì vậy endpoint này
            // LUÔN trả 200 — kèm cờ handled/success để biết có ghi nhận thanh toán hay không.
            // (Body rỗng = payOS/Swagger ping thử; chữ ký sai = bỏ qua, không ghi nhận.)

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return Ok(new { success = true, handled = false, message = "Empty body - acknowledged." });
            }

            try
            {
                using (var doc = System.Text.Json.JsonDocument.Parse(rawBody))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (dataProp.TryGetProperty("description", out var descProp))
                        {
                            var descStr = descProp.GetString();
                            if (descStr != null && descStr.Contains("confirm webhook", StringComparison.OrdinalIgnoreCase))
                            {
                                return Ok(new { success = true, message = "Webhook confirmed" });
                            }
                        }
                    }
                }
            }
            catch
            {
                // JSON không hợp lệ -> vẫn ack 200 ở dưới, không làm payOS retry
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
                return Ok(new { success = true, handled });
            }
            catch (Exception ex)
            {
                // Chữ ký sai / dữ liệu lỗi: KHÔNG ghi nhận, nhưng vẫn trả 200 để payOS
                // không đánh dấu webhook URL là hỏng. Ghi lý do vào response để debug.
                return Ok(new { success = false, handled = false, message = ex.Message });
            }
        }
    }
}