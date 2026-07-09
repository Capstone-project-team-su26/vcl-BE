using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.Interface;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Tích hợp payOS qua REST API (https://api-merchant.payos.vn) + ký HMAC-SHA256 bằng checksum key.
    /// </summary>
    public class PayOsService : IPayOsService
    {
        private const string CreatePaymentLinkUrl = "https://api-merchant.payos.vn/v2/payment-requests";

        // HttpClient dùng chung, tránh cạn socket
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly string _clientId;
        private readonly string _apiKey;
        private readonly string _checksumKey;

        public PayOsService(IConfiguration configuration)
        {
            _clientId = configuration["PayOS:ClientId"] ?? string.Empty;
            _apiKey = configuration["PayOS:ApiKey"] ?? string.Empty;
            _checksumKey = configuration["PayOS:ChecksumKey"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_clientId) ||
                string.IsNullOrWhiteSpace(_apiKey) ||
                string.IsNullOrWhiteSpace(_checksumKey))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình payOS. Vui lòng thiết lập PayOS:ClientId, PayOS:ApiKey, PayOS:ChecksumKey.");
            }
        }

        public async Task<PayOsPaymentLink> CreatePaymentLinkAsync(
            long orderCode,
            int amount,
            string description,
            string returnUrl,
            string cancelUrl,
            IEnumerable<PayOsItem> items)
        {
            // 1. Chữ ký: HMAC-SHA256 trên chuỗi các trường bắt buộc, sắp xếp theo alphabet
            var signatureRaw =
                $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";
            var signature = ComputeHmacSha256(signatureRaw, _checksumKey);

            var body = new
            {
                orderCode,
                amount,
                description,
                cancelUrl,
                returnUrl,
                signature,
                items = (items ?? Enumerable.Empty<PayOsItem>())
                    .Select(i => new { name = i.Name, quantity = i.Quantity, price = i.Price })
                    .ToList()
            };

            // 2. Gọi payOS
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, CreatePaymentLinkUrl)
            {
                Content = JsonContent.Create(body)
            };
            httpRequest.Headers.Add("x-client-id", _clientId);
            httpRequest.Headers.Add("x-api-key", _apiKey);

            var httpResponse = await _httpClient.SendAsync(httpRequest);
            var responseJson = await httpResponse.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            var code = GetString(root, "code");
            if (!string.Equals(code, "00", StringComparison.Ordinal))
            {
                var desc = GetString(root, "desc");
                throw new InvalidOperationException($"payOS tạo link thanh toán thất bại (code {code}): {desc}");
            }

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("payOS không trả về dữ liệu link thanh toán.");
            }

            var checkoutUrl = GetString(data, "checkoutUrl");
            var paymentLinkId = GetString(data, "paymentLinkId");
            var returnedOrderCode = data.TryGetProperty("orderCode", out var oc) && oc.TryGetInt64(out var ocVal)
                ? ocVal
                : orderCode;

            return new PayOsPaymentLink(returnedOrderCode, paymentLinkId, checkoutUrl);
        }

        public PayOsWebhookResult VerifyWebhook(string rawJsonBody)
        {
            if (string.IsNullOrWhiteSpace(rawJsonBody))
                throw new ArgumentException("Nội dung webhook payOS rỗng.");

            using var doc = JsonDocument.Parse(rawJsonBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Webhook payOS thiếu trường data.");

            var receivedSignature = GetString(root, "signature");
            if (string.IsNullOrWhiteSpace(receivedSignature))
                throw new ArgumentException("Webhook payOS thiếu chữ ký.");

            // 1. Dựng chuỗi ký từ các trường trong data, sắp xếp key theo alphabet
            var sortedPairs = data.EnumerateObject()
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={JsonValueToString(p.Value)}");
            var signatureRaw = string.Join("&", sortedPairs);

            var expectedSignature = ComputeHmacSha256(signatureRaw, _checksumKey);

            // 2. So khớp chữ ký (chống giả mạo)
            if (!string.Equals(expectedSignature, receivedSignature, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Chữ ký webhook payOS không hợp lệ.");

            var code = GetString(data, "code");
            bool success = string.Equals(code, "00", StringComparison.Ordinal);
            var orderCode = data.TryGetProperty("orderCode", out var oc) && oc.TryGetInt64(out var ocVal) ? ocVal : 0L;
            var reference = GetString(data, "reference");
            var description = GetString(data, "description");

            return new PayOsWebhookResult(success, orderCode, reference, description);
        }

        private static string ComputeHmacSha256(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value))
                return JsonValueToString(value);
            return string.Empty;
        }

        private static string JsonValueToString(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Undefined => string.Empty,
                _ => value.GetRawText()
            };
        }
    }
}
