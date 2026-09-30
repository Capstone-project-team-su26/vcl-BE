using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;
using VCL_API.Helpers;
using VCL_BLL.Helpers;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [ApiController]
    public class PurchasePaymentController : ControllerBase
    {
        private readonly IPurchasePaymentService _purchasePaymentService;
        private readonly IConsignmentPaymentService _consignmentPaymentService;
        private readonly ISePayService _sePayService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PurchasePaymentController> _logger;

        public PurchasePaymentController(
            IPurchasePaymentService purchasePaymentService,
            IConsignmentPaymentService consignmentPaymentService,
            ISePayService sePayService,
            IUnitOfWork unitOfWork,
            IConfiguration configuration,
            ILogger<PurchasePaymentController> logger)
        {
            _purchasePaymentService = purchasePaymentService;
            _consignmentPaymentService = consignmentPaymentService;
            _sePayService = sePayService;
            _unitOfWork = unitOfWork;
            _configuration = configuration;
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
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Lịch sử thanh toán của một đơn mua hộ (Customer xem đơn của mình, Staff/Sales xem tất cả).
        /// </summary>
        [HttpGet("api/purchase-requests/{requestId}/payments")]
        [Authorize]
        public async Task<IActionResult> GetPurchasePayments(Guid requestId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchasePaymentService.GetPaymentHistoryAsync(requestId, userId, userRole);
                return Ok(new { message = "Lấy lịch sử thanh toán mua hộ thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
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
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
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

        /// <summary>
        /// Webhook nhận callback thanh toán từ SePay.
        /// </summary>
        [HttpPost("api/payments/sepay-webhook")]
        [AllowAnonymous]
        [EnableBuffering]
        public async Task<IActionResult> SePayWebhook([FromBody] SePayWebhookDto dto)
        {
            var signatureHeader = Request.Headers["X-SePay-Signature"].ToString();
            var timestampHeader = Request.Headers["X-SePay-Timestamp"].ToString();
            var authorizationHeader = Request.Headers["Authorization"].ToString();

            // Get pre-read raw request body from HttpContext.Items
            var rawBody = HttpContext.Items["RawRequestBody"] as string ?? string.Empty;

            _logger.LogInformation("SePay webhook hit. Signature={Sig} Timestamp={Time} Auth={Auth} Body={Body}", 
                signatureHeader, timestampHeader, authorizationHeader, rawBody);

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return BadRequest(new { success = false, message = "Body rỗng" });
            }

            // 1. Xác thực webhook
            var isVerified = _sePayService.VerifyWebhook(signatureHeader, timestampHeader, authorizationHeader, rawBody);
            if (!isVerified)
            {
                _logger.LogWarning("SePay webhook signature/key validation failed.");
                return Unauthorized(new { success = false, message = "Chữ ký hoặc API Key không hợp lệ." });
            }

            // 2. Parse/Validate DTO if model state is invalid or null
            if (dto == null)
            {
                try
                {
                    dto = System.Text.Json.JsonSerializer.Deserialize<SePayWebhookDto>(rawBody, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? throw new Exception("Không thể parse dữ liệu giao dịch.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi deserialize SePay webhook body.");
                    return BadRequest(new { success = false, message = "Dữ liệu JSON không hợp lệ." });
                }
            }

            // 3. Xử lý thanh toán
            try
            {
                _logger.LogInformation("Processing SePay webhook as purchase payment...");
                var handled = await _purchasePaymentService.HandleSePayWebhookAsync(dto);

                if (!handled)
                {
                    _logger.LogInformation("Not a purchase payment. Trying consignment payment...");
                    handled = await _consignmentPaymentService.HandleSePayWebhookAsync(dto);
                }

                _logger.LogInformation("SePay webhook handled = {Handled}", handled);

                return Ok(new { success = true, handled });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý SePay webhook.");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// API kiểm tra trạng thái thanh toán của đơn hàng qua orderCode (dùng cho polling).
        /// </summary>
        [HttpGet("api/payments/status/{orderCode}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPaymentStatusByOrderCode(long orderCode)
        {
            var paymentRepo = _unitOfWork.Repository<Payment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode == orderCode);
            if (payment != null)
            {
                return Ok(new
                {
                    orderCode = payment.OrderCode,
                    amount = payment.Amount,
                    status = payment.PaymentStatus,
                    paymentMethod = payment.PaymentMethod
                });
            }

            var purchasePaymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var purchasePayment = await purchasePaymentRepo.FirstOrDefaultAsync(p => p.OrderCode == orderCode);
            if (purchasePayment != null)
            {
                return Ok(new
                {
                    orderCode = purchasePayment.OrderCode,
                    amount = purchasePayment.Amount,
                    status = purchasePayment.Status,
                    paymentMethod = purchasePayment.PaymentMethod
                });
            }

            return NotFound(new { message = "Không tìm thấy thông tin giao dịch." });
        }

        /// <summary>
        /// Trang hiển thị QR thanh toán SePay.
        /// </summary>
        [HttpGet("api/payments/sepay/checkout/{orderCode}")]
        [AllowAnonymous]
        public async Task<IActionResult> SePayCheckout(long orderCode, [FromQuery] string? returnUrl = null, [FromQuery] string? cancelUrl = null)
        {
            decimal amount = 0m;
            string paymentMethod = "";
            string status = "";
            bool isConsignment = false;

            var paymentRepo = _unitOfWork.Repository<Payment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode == orderCode);
            if (payment != null)
            {
                amount = payment.Amount ?? 0m;
                paymentMethod = payment.PaymentMethod;
                status = payment.PaymentStatus;
                isConsignment = true;
            }
            else
            {
                var purchasePaymentRepo = _unitOfWork.Repository<PurchasePayment>();
                var purchasePayment = await purchasePaymentRepo.FirstOrDefaultAsync(p => p.OrderCode == orderCode);
                if (purchasePayment != null)
                {
                    amount = purchasePayment.Amount;
                    paymentMethod = purchasePayment.PaymentMethod;
                    status = purchasePayment.Status;
                    isConsignment = false;
                }
            }

            if (amount <= 0 || !string.Equals(paymentMethod, "SEPAY", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound("Không tìm thấy thông tin thanh toán SePay hợp lệ.");
            }

            // URL quay về: ưu tiên returnUrl/cancelUrl web gửi lúc tạo khoản thu (nằm trên query của link này) —
            // lọc lại theo danh sách origin cho phép vì ai cũng tự gõ được query (chống open-redirect).
            // Không có thì như cũ: origin lấy từ Referer (localhost hoặc đúng tên miền web khách cấu hình) +
            // trang lịch sử cũ. Luôn gắn ?orderCode=&status=success|cancelled để web biết giao dịch nào.
            var frontendBaseUrl = ResolveFrontendOrigin(Request.Headers["Referer"].ToString());
            var legacyPath = isConsignment ? PaymentReturnUrls.ConsignmentHistoryPath : PaymentReturnUrls.PurchaseHistoryPath;
            var (successUrl, cancelledUrl) = PaymentReturnUrls.Build(
                _configuration, returnUrl, cancelUrl, legacyPath, orderCode, frontendBaseUrl);

            if (string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(successUrl);
            }

            var accNumber = _configuration["SePay:AccountNumber"] ?? string.Empty;
            var bankName = _configuration["SePay:BankName"] ?? string.Empty;
            var accName = _configuration["SePay:AccountName"] ?? "CONG TY CO PHAN LOGISTICS VCL";

            var qrUrl = _sePayService.GenerateQrUrl((int)Math.Round(amount, MidpointRounding.AwayFromZero), $"VCL{orderCode}");

            // URL chèn vào HTML/JS phải mã hoá (dấu ' trong query hợp lệ theo URI nhưng phá chuỗi JS).
            var html = GetSePayCheckoutHtml(accName, bankName, accNumber, amount, orderCode, qrUrl,
                System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(successUrl),
                System.Net.WebUtility.HtmlEncode(cancelledUrl));
            return Content(html, "text/html", Encoding.UTF8);
        }

        /// <summary>
        /// Origin web khách đã mở trang QR (Referer) nếu thuộc danh sách cho phép (localhost, CORS, cấu hình
        /// SePay/PayOS ReturnUrl, Payment:AllowedReturnOrigins); không thì origin mặc định trong cấu hình.
        /// </summary>
        private string ResolveFrontendOrigin(string referer)
        {
            var allowed = PaymentReturnUrls.Sanitize(_configuration, referer);
            return allowed != null && Uri.TryCreate(allowed, UriKind.Absolute, out var refUri)
                ? refUri.GetLeftPart(UriPartial.Authority)
                : PaymentReturnUrls.DefaultOrigin(_configuration);
        }

        private static string GetSePayCheckoutHtml(string accountName, string bankName, string accountNumber, decimal amount, long orderCode, string qrUrl, string returnUrl, string cancelUrl)
        {
            var amountFormatted = amount.ToString("N0");
            var amountStr = ((int)Math.Round(amount, MidpointRounding.AwayFromZero)).ToString();

            return $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Cổng Thanh Toán VCL - SePay</title>
    <link href=""https://fonts.googleapis.com/css2?family=Outfit:wght@300;400;500;600;700&display=swap"" rel=""stylesheet"">
    <style>
        :root {{
            --bg-color: #0d1117;
            --card-bg: rgba(22, 27, 34, 0.7);
            --border-color: rgba(48, 54, 61, 0.8);
            --primary-color: #58a6ff;
            --accent-color: #1f6feb;
            --text-main: #c9d1d9;
            --text-muted: #8b949e;
            --success-color: #2ea44f;
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
            font-family: 'Outfit', sans-serif;
        }}

        body {{
            background-color: var(--bg-color);
            color: var(--text-main);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
            background-image: radial-gradient(circle at 50% 50%, #161b22 0%, #0d1117 100%);
        }}

        .container {{
            width: 100%;
            max-width: 900px;
            display: grid;
            grid-template-columns: 1.2fr 1fr;
            gap: 24px;
        }}

        @media (max-width: 768px) {{
            .container {{
                grid-template-columns: 1fr;
            }}
        }}

        .card {{
            background: var(--card-bg);
            border: 1px solid var(--border-color);
            border-radius: 20px;
            padding: 32px;
            backdrop-filter: blur(12px);
            box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.37);
            display: flex;
            flex-direction: column;
            justify-content: space-between;
        }}

        .qr-card {{
            align-items: center;
            text-align: center;
        }}

        .header {{
            margin-bottom: 24px;
        }}

        .logo {{
            font-weight: 700;
            font-size: 24px;
            color: #ffffff;
            letter-spacing: 1px;
            margin-bottom: 8px;
            display: flex;
            align-items: center;
            gap: 8px;
        }}

        .logo span {{
            color: var(--primary-color);
        }}

        .subtitle {{
            font-size: 14px;
            color: var(--text-muted);
        }}

        .qr-image-wrapper {{
            background: #ffffff;
            padding: 16px;
            border-radius: 16px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.15);
            margin: 20px 0;
            display: flex;
            align-items: center;
            justify-content: center;
            position: relative;
            overflow: hidden;
            width: 250px;
            height: 250px;
        }}

        .qr-image {{
            max-width: 100%;
            max-height: 100%;
            object-fit: contain;
        }}

        .status-container {{
            display: flex;
            align-items: center;
            gap: 10px;
            margin-top: 15px;
            font-size: 14px;
            font-weight: 500;
            color: var(--primary-color);
        }}

        .pulse-loader {{
            width: 10px;
            height: 10px;
            background-color: var(--primary-color);
            border-radius: 50%;
            animation: pulse 1.5s infinite ease-in-out;
        }}

        @keyframes pulse {{
            0% {{ transform: scale(0.8); opacity: 0.5; }}
            50% {{ transform: scale(1.2); opacity: 1; }}
            100% {{ transform: scale(0.8); opacity: 0.5; }}
        }}

        .info-row {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: 14px 0;
            border-bottom: 1px solid rgba(240, 246, 252, 0.1);
        }}

        .info-row:last-child {{
            border-bottom: none;
        }}

        .label {{
            font-size: 14px;
            color: var(--text-muted);
        }}

        .value {{
            font-size: 15px;
            font-weight: 600;
            color: #ffffff;
            text-align: right;
            display: flex;
            align-items: center;
            gap: 8px;
        }}

        .copy-btn {{
            background: rgba(88, 166, 255, 0.15);
            border: 1px solid rgba(88, 166, 255, 0.3);
            color: var(--primary-color);
            padding: 4px 8px;
            border-radius: 6px;
            font-size: 12px;
            cursor: pointer;
            transition: all 0.2s ease;
        }}

        .copy-btn:hover {{
            background: var(--primary-color);
            color: #ffffff;
        }}

        .instructions {{
            margin-top: 24px;
            font-size: 13px;
            color: var(--text-muted);
            line-height: 1.6;
        }}

        .instructions ol {{
            padding-left: 20px;
            margin-top: 8px;
        }}

        .toast {{
            position: fixed;
            bottom: 24px;
            left: 50%;
            transform: translateX(-50%);
            background: var(--success-color);
            color: white;
            padding: 12px 24px;
            border-radius: 10px;
            font-size: 14px;
            font-weight: 500;
            opacity: 0;
            transition: opacity 0.3s ease;
            box-shadow: 0 4px 12px rgba(0,0,0,0.25);
            pointer-events: none;
            z-index: 1000;
        }}

        .toast.show {{
            opacity: 1;
        }}
    </style>
</head>
<body>

<div class=""container"">
    <div class=""card"">
        <div>
            <div class=""header"">
                <div class=""logo"">VCL <span>Logistics</span></div>
                <div class=""subtitle"">Thanh toán đơn hàng ký gửi & mua hộ liên quốc gia</div>
            </div>
            
            <div class=""info-row"">
                <div class=""label"">Chủ tài khoản</div>
                <div class=""value"">{accountName}</div>
            </div>
            <div class=""info-row"">
                <div class=""label"">Ngân hàng</div>
                <div class=""value"">{bankName}</div>
            </div>
            <div class=""info-row"">
                <div class=""label"">Số tài khoản</div>
                <div class=""value"">
                    <span>{accountNumber}</span>
                    <button class=""copy-btn"" onclick=""copyText('{accountNumber}')"">Sao chép</button>
                </div>
            </div>
            <div class=""info-row"">
                <div class=""label"">Số tiền chuyển khoản</div>
                <div class=""value"" style=""color: #ff7b72; font-size: 18px;"">
                    <span>{amountFormatted} VND</span>
                    <button class=""copy-btn"" onclick=""copyText('{amountStr}')"">Sao chép</button>
                </div>
            </div>
            <div class=""info-row"">
                <div class=""label"">Nội dung chuyển khoản</div>
                <div class=""value"" style=""color: var(--primary-color);"">
                    <span>VCL{orderCode}</span>
                    <button class=""copy-btn"" onclick=""copyText('VCL{orderCode}')"">Sao chép</button>
                </div>
            </div>
        </div>

        <div class=""instructions"">
            <strong>Lưu ý quan trọng:</strong>
            <ol>
                <li>Vui lòng chuyển khoản đúng số tiền và nội dung chuyển khoản để hệ thống tự động ghi nhận thanh toán sau 1-3 phút.</li>
                <li>Hệ thống tự động quét giao dịch và hoàn tất hóa đơn ngay khi nhận được tín hiệu từ ngân hàng.</li>
            </ol>
            <div style=""margin-top: 20px; text-align: center;"">
                <a href=""{cancelUrl}"" class=""cancel-link"" style=""color: var(--text-muted); font-size: 13px; text-decoration: none; display: inline-flex; align-items: center; gap: 4px; transition: color 0.2s;"" onmouseover=""this.style.color='#ff7b72'"" onmouseout=""this.style.color='var(--text-muted)'"">
                    ✕ Hủy giao dịch thanh toán
                </a>
            </div>
        </div>
    </div>

    <div class=""card qr-card"">
        <div class=""subtitle"" style=""font-weight: 500;"">Quét mã VietQR để thanh toán nhanh</div>
        
        <div class=""qr-image-wrapper"">
            <img class=""qr-image"" src=""{qrUrl}"" alt=""VietQR SePay"">
        </div>

        <div class=""status-container"">
            <div class=""pulse-loader""></div>
            <span>Đang chờ chuyển khoản...</span>
        </div>
    </div>
</div>

<div id=""toast"" class=""toast"">Đã sao chép vào bộ nhớ tạm!</div>

<script>
    function copyText(text) {{
        navigator.clipboard.writeText(text).then(() => {{
            const toast = document.getElementById('toast');
            toast.classList.add('show');
            setTimeout(() => {{
                toast.classList.remove('show');
            }}, 2000);
        }});
    }}

    const orderCode = '{orderCode}';
    const returnUrl = '{returnUrl}';
    
    function checkStatus() {{
        fetch(`/api/payments/status/${{orderCode}}`)
            .then(res => res.json())
            .then(data => {{
                if (data.status === 'PAID' || data.status === 'SUCCESS') {{
                    document.querySelector('.status-container span').innerText = 'Thanh toán thành công! Đang chuyển hướng...';
                    document.querySelector('.status-container span').style.color = '#2ea44f';
                    document.querySelector('.pulse-loader').style.backgroundColor = '#2ea44f';
                    setTimeout(() => {{
                        window.location.href = returnUrl;
                    }}, 1500);
                }} else {{
                    setTimeout(checkStatus, 3000);
                }}
            }})
            .catch(err => {{
                console.error('Lỗi kiểm tra trạng thái:', err);
                setTimeout(checkStatus, 5000);
            }});
    }}

    setTimeout(checkStatus, 3000);
</script>

</body>
</html>";
        }
    }
}