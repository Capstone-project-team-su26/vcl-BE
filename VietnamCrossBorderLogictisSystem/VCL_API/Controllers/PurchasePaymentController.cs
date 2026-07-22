using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security.Claims;
using System.Text;
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
        private readonly ILogger<PurchasePaymentController> _logger;

        public PurchasePaymentController(
            IPurchasePaymentService purchasePaymentService,
            IConsignmentPaymentService consignmentPaymentService,
            ILogger<PurchasePaymentController> logger)
        {
            _purchasePaymentService = purchasePaymentService;
            _consignmentPaymentService = consignmentPaymentService;
            _logger = logger;
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
        /// Đồng bộ trạng thái thanh toán trực tiếp từ payOS (không cần chờ webhook).
        /// FE gọi sau khi khách thanh toán xong và được payOS chuyển về ReturnUrl.
        /// Tự động thử cả thanh toán mua hộ lẫn thanh toán ký gửi theo orderCode.
        /// </summary>
        [HttpGet("api/payments/{orderCode}/sync-status")]
        [Authorize]
        public async Task<IActionResult> SyncPaymentStatus(long orderCode)
        {
            try
            {
                string status;
                try
                {
                    status = await _purchasePaymentService.SyncPaymentStatusAsync(orderCode);
                }
                catch (NotFoundException)
                {
                    // Không thuộc thanh toán mua hộ -> thử thanh toán ký gửi
                    status = await _consignmentPaymentService.SyncPaymentStatusAsync(orderCode);
                }

                return Ok(new
                {
                    message = "Đồng bộ trạng thái thanh toán thành công.",
                    data = new { orderCode, status, isPaid = string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase) }
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Nhận diện request ping xác nhận webhook của payOS qua trường data.description.
        /// Bỏ dấu tiếng Việt trước khi so khớp để bắt được cả "mã xác nhận webhook",
        /// "ma xac nhan webhook" lẫn "confirm webhook".
        /// </summary>
        private static bool IsWebhookConfirmationPing(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;

            var normalized = new string(description
                .Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                            != System.Globalization.UnicodeCategory.NonSpacingMark)
                .ToArray())
                .Replace('đ', 'd').Replace('Đ', 'D')
                .ToLowerInvariant();

            return normalized.Contains("confirm-webhook")
                || normalized.Contains("confirm webhook")
                || normalized.Contains("xac-nhan-webhook")
                || normalized.Contains("xac nhan webhook")
                || normalized.Contains("ma-xac-nhan")
                || normalized.Contains("ma xac nhan");
        }

        /// <summary>
        /// Webhook nhận callback thanh toán từ payOS. payOS gọi trực tiếp, không yêu cầu đăng nhập.
        /// </summary>
        [HttpPost("api/payments/payos/webhook")]
        [HttpPost("api/payments/payos-webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> PayOsWebhook()
        {
            var headers = new StringBuilder();
            foreach (var header in Request.Headers)
            {
                headers.AppendLine($"{header.Key}: {header.Value}");
            }

            _logger.LogInformation(
                "PayOS webhook hit. Time={Time} Method={Method} Path={Path} Content-Type={ContentType} Content-Length={ContentLength}\nHeaders:\n{Headers}",
                DateTime.Now, Request.Method, Request.Path, Request.ContentType, Request.ContentLength, headers.ToString());

            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            _logger.LogInformation("PayOS webhook body: {RawBody}", rawBody);

            // Body rỗng -> ACK ngay
            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogInformation("PayOS webhook body is empty.");

                return Ok(new
                {
                    success = true,
                    handled = false,
                    message = "Empty body - acknowledged."
                });
            }

            // Kiểm tra nhanh confirm webhook
            if (rawBody.Contains("confirm-webhook", StringComparison.OrdinalIgnoreCase)
                || rawBody.Contains("confirm webhook", StringComparison.OrdinalIgnoreCase)
                || rawBody.Contains("xac-nhan-webhook", StringComparison.OrdinalIgnoreCase)
                || rawBody.Contains("xac nhan webhook", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("PayOS webhook confirmation detected (raw body match).");

                return Ok(new
                {
                    success = true,
                    message = "Webhook confirmed"
                });
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rawBody);

                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var dataProp))
                {
                    if (dataProp.TryGetProperty("description", out var descProp))
                    {
                        var description = descProp.GetString();

                        _logger.LogInformation("PayOS webhook description: {Description}", description);

                        if (IsWebhookConfirmationPing(description))
                        {
                            _logger.LogInformation("PayOS webhook confirmation detected (description match).");

                            return Ok(new
                            {
                                success = true,
                                message = "Webhook confirmed"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PayOS webhook body is not valid JSON.");
            }

            try
            {
                _logger.LogInformation("Processing PayOS webhook as purchase payment...");

                var handled = await _purchasePaymentService.HandlePayOsWebhookAsync(rawBody);

                if (!handled)
                {
                    _logger.LogInformation("Not a purchase payment. Trying consignment payment...");

                    handled = await _consignmentPaymentService.HandlePayOsWebhookAsync(rawBody);
                }

                _logger.LogInformation("PayOS webhook handled = {Handled}", handled);

                return Ok(new
                {
                    success = true,
                    handled
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling PayOS webhook.");

                return Ok(new
                {
                    success = false,
                    handled = false,
                    message = ex.Message
                });
            }
        }
    }
}