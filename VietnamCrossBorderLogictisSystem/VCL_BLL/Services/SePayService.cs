using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using VCL_BLL.Interface;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Triển khai dịch vụ thanh toán SePay.
    /// </summary>
    public class SePayService : ISePayService
    {
        private readonly string _accountNumber;
        private readonly string _bankName;
        private readonly string _apiKey;
        private readonly string _webhookSecret;

        public SePayService(IConfiguration configuration)
        {
            _accountNumber = configuration["SePay:AccountNumber"] ?? string.Empty;
            _bankName = configuration["SePay:BankName"] ?? string.Empty;
            _apiKey = configuration["SePay:ApiKey"] ?? string.Empty;
            _webhookSecret = configuration["SePay:WebhookSecret"] ?? string.Empty;
        }

        public string GenerateQrUrl(int amount, string description)
        {
            if (string.IsNullOrWhiteSpace(_accountNumber) || string.IsNullOrWhiteSpace(_bankName))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình SePay:AccountNumber hoặc SePay:BankName trong appsettings.json.");
            }

            // URL định dạng: https://qr.sepay.vn/img?acc={acc}&bank={bank}&amount={amount}&des={des}
            var encodedDes = Uri.EscapeDataString(description);
            return $"https://qr.sepay.vn/img?acc={_accountNumber}&bank={_bankName}&amount={amount}&des={encodedDes}";
        }

        public bool VerifyWebhook(string signatureHeader, string timestampHeader, string authorizationHeader, string rawJsonBody)
        {
            // 1. Kiểm tra API Key gửi qua header Authorization (nếu có cấu hình ApiKey)
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                var authClean = authorizationHeader?.Replace("Apikey ", "", StringComparison.OrdinalIgnoreCase)?.Trim() ?? string.Empty;
                if (string.Equals(_apiKey, authClean, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // 2. Kiểm tra HMAC-SHA256 chữ ký (nếu có cấu hình WebhookSecret và đầy đủ header)
            if (!string.IsNullOrWhiteSpace(_webhookSecret) && !string.IsNullOrWhiteSpace(signatureHeader) && !string.IsNullOrWhiteSpace(timestampHeader))
            {
                try
                {
                    var message = $"{timestampHeader}.{rawJsonBody}";
                    
                    var keyBytes = Encoding.UTF8.GetBytes(_webhookSecret);
                    var messageBytes = Encoding.UTF8.GetBytes(message);

                    using var hmac = new HMACSHA256(keyBytes);
                    var hashBytes = hmac.ComputeHash(messageBytes);
                    var computedSignature = Convert.ToHexString(hashBytes).ToLowerInvariant();

                    var expectedSignature = signatureHeader.Replace("sha256=", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();

                    var computedBytes = Encoding.UTF8.GetBytes(computedSignature);
                    var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

                    if (computedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(computedBytes, expectedBytes))
                    {
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            // 3. Nếu không cấu hình cả ApiKey và WebhookSecret (môi trường dev/test) -> bỏ qua xác thực
            if (string.IsNullOrWhiteSpace(_apiKey) && string.IsNullOrWhiteSpace(_webhookSecret))
            {
                return true;
            }

            return false;
        }
    }
}
