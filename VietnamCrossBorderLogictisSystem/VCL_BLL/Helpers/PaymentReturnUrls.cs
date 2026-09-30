using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// URL đưa khách quay về web sau khi trả tiền (payOS / trang QR SePay) — một nguồn cho mọi khoản thu.
    /// <list type="bullet">
    /// <item>Tôn trọng <c>returnUrl</c>/<c>cancelUrl</c> web gửi lên, NHƯNG chỉ khi là URL http(s) tuyệt đối có
    ///       origin nằm trong danh sách cho phép (chống open-redirect): <c>Cors:AllowedOrigins</c>,
    ///       <c>Payment:AllowedReturnOrigins</c>, origin của <c>PayOS:ReturnUrl/CancelUrl</c>, <c>SePay:ReturnUrl/CancelUrl</c>,
    ///       và localhost/127.0.0.1 (web chạy thử, mọi cổng).</item>
    /// <item>Không hợp lệ / không gửi → URL cũ <c>{origin}/history/buy-on-behalf</c> (khoản của yêu cầu mua hộ)
    ///       hoặc <c>{origin}/history/consignment</c> (khoản của đơn kho) — web cũ vẫn chạy.</item>
    /// <item>Luôn gắn <c>?orderCode=&lt;mã&gt;&amp;status=success|cancelled</c> để web biết đúng giao dịch vừa trả / huỷ,
    ///       không phải đoán qua bản ghi localStorage.</item>
    /// </list>
    /// </summary>
    public static class PaymentReturnUrls
    {
        public const string PurchaseHistoryPath = "/history/buy-on-behalf";
        public const string ConsignmentHistoryPath = "/history/consignment";

        public const string StatusSuccess = "success";
        public const string StatusCancelled = "cancelled";

        private const string DefaultFrontendOrigin = "https://logictic.site";

        /// <summary>Origin được phép quay về (dạng "https://host[:port]", chữ thường).</summary>
        public static HashSet<string> AllowedOrigins(IConfiguration configuration)
        {
            var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddUrl(string? value)
            {
                if (TryParseHttp(value, out var uri))
                    origins.Add(OriginOf(uri));
            }

            void AddSection(string key)
            {
                foreach (var child in configuration.GetSection(key).GetChildren())
                    AddUrl(child.Value);
                // Biến môi trường dạng "a,b;c" cũng nhận.
                var flat = configuration[key];
                if (!string.IsNullOrWhiteSpace(flat))
                    foreach (var part in flat.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        AddUrl(part);
            }

            AddSection("Cors:AllowedOrigins");
            AddSection("Payment:AllowedReturnOrigins");
            AddUrl(configuration["PayOS:ReturnUrl"]);
            AddUrl(configuration["PayOS:CancelUrl"]);
            AddUrl(configuration["SePay:ReturnUrl"]);
            AddUrl(configuration["SePay:CancelUrl"]);
            origins.Add(DefaultFrontendOrigin);
            return origins;
        }

        /// <summary>
        /// URL web gửi lên nếu hợp lệ và thuộc origin cho phép (đã chuẩn hoá, bỏ orderCode/status cũ); ngược lại null.
        /// </summary>
        public static string? Sanitize(IConfiguration configuration, string? requested)
        {
            if (!TryParseHttp(requested, out var uri))
                return null;
            if (!IsLoopback(uri) && !AllowedOrigins(configuration).Contains(OriginOf(uri)))
                return null;
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return null;
            return StripResultParams(uri.AbsoluteUri);
        }

        /// <summary>Origin web mặc định: origin của PayOS/SePay ReturnUrl trong cấu hình, không có thì logictic.site.</summary>
        public static string DefaultOrigin(IConfiguration configuration)
        {
            foreach (var key in new[] { "SePay:ReturnUrl", "PayOS:ReturnUrl" })
                if (TryParseHttp(configuration[key], out var uri))
                    return OriginOf(uri);
            return DefaultFrontendOrigin;
        }

        /// <summary>
        /// Cặp URL cuối cùng đưa cho cổng thanh toán. <paramref name="fallbackOrigin"/> dùng khi web không gửi URL hợp lệ
        /// (VD origin lấy từ Referer của trang QR SePay); null thì lấy <see cref="DefaultOrigin"/>.
        /// </summary>
        public static (string ReturnUrl, string CancelUrl) Build(
            IConfiguration configuration, string? requestedReturnUrl, string? requestedCancelUrl,
            string legacyPath, long orderCode, string? fallbackOrigin = null)
        {
            var (baseReturn, baseCancel) = ResolveBase(configuration, requestedReturnUrl, requestedCancelUrl, legacyPath, fallbackOrigin);
            return (AppendResult(baseReturn, orderCode, StatusSuccess), AppendResult(baseCancel, orderCode, StatusCancelled));
        }

        /// <summary>URL gốc (chưa gắn kết quả) — dùng khi truyền sang trang QR SePay qua query.</summary>
        public static (string ReturnUrl, string CancelUrl) ResolveBase(
            IConfiguration configuration, string? requestedReturnUrl, string? requestedCancelUrl,
            string legacyPath, string? fallbackOrigin = null)
        {
            var baseReturn = Sanitize(configuration, requestedReturnUrl)
                ?? $"{(fallbackOrigin ?? DefaultOrigin(configuration)).TrimEnd('/')}{legacyPath}";
            var baseCancel = Sanitize(configuration, requestedCancelUrl) ?? baseReturn;
            return (baseReturn, baseCancel);
        }

        /// <summary>
        /// Link trang QR SePay kèm URL quay về (đã lọc) — trang QR lọc lại lần nữa vì ai cũng tự gõ được query.
        /// </summary>
        public static string SePayCheckoutUrl(
            IConfiguration configuration, long orderCode, string? requestedReturnUrl, string? requestedCancelUrl)
        {
            var baseUrl = (configuration["SePay:BaseUrl"] ?? string.Empty).TrimEnd('/');
            var url = $"{baseUrl}/api/payments/sepay/checkout/{orderCode}";
            var query = new List<string>();
            var returnUrl = Sanitize(configuration, requestedReturnUrl);
            var cancelUrl = Sanitize(configuration, requestedCancelUrl);
            if (returnUrl != null) query.Add("returnUrl=" + Uri.EscapeDataString(returnUrl));
            if (cancelUrl != null) query.Add("cancelUrl=" + Uri.EscapeDataString(cancelUrl));
            return query.Count == 0 ? url : $"{url}?{string.Join("&", query)}";
        }

        /// <summary>Gắn orderCode + status vào URL (thay giá trị cũ nếu có), giữ nguyên query khác và #hash.</summary>
        public static string AppendResult(string url, long orderCode, string status)
        {
            var hashIndex = url.IndexOf('#');
            var hash = hashIndex >= 0 ? url[hashIndex..] : string.Empty;
            var withoutHash = StripResultParams(hashIndex >= 0 ? url[..hashIndex] : url);
            var separator = withoutHash.Contains('?') ? (withoutHash.EndsWith("?") || withoutHash.EndsWith("&") ? "" : "&") : "?";
            return $"{withoutHash}{separator}orderCode={orderCode}&status={Uri.EscapeDataString(status)}{hash}";
        }

        private static string StripResultParams(string url)
        {
            var hashIndex = url.IndexOf('#');
            var hash = hashIndex >= 0 ? url[hashIndex..] : string.Empty;
            var body = hashIndex >= 0 ? url[..hashIndex] : url;
            var q = body.IndexOf('?');
            if (q < 0) return url;

            var kept = body[(q + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p =>
                {
                    var name = p.Split('=')[0];
                    return !string.Equals(name, "orderCode", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(name, "status", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            var path = body[..q];
            return (kept.Count == 0 ? path : $"{path}?{string.Join("&", kept)}") + hash;
        }

        private static bool TryParseHttp(string? value, out Uri uri)
        {
            uri = null!;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrWhiteSpace(parsed.Host)) return false;
            uri = parsed;
            return true;
        }

        private static bool IsLoopback(Uri uri) =>
            uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        private static string OriginOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}
