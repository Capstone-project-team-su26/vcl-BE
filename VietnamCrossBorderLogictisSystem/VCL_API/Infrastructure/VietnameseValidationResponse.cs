using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace VCL_API.Infrastructure
{
    /// <summary>
    /// Thay body 400 mặc định của [ApiController] (ValidationProblemDetails tiếng Anh: "One or more validation
    /// errors occurred.", "The JSON value could not be converted to System.Int32...") bằng câu tiếng Việt.
    /// Giữ nguyên hình dạng cũ (title, status, errors) để web cũ vẫn đọc được, thêm <c>message</c> — web đọc
    /// trường này trước tiên (shared/utils/apiError.js).
    /// </summary>
    public static class VietnameseValidationResponse
    {
        private const string DefaultTitle = "Dữ liệu gửi lên không hợp lệ.";

        public static IActionResult Create(ActionContext context)
        {
            var entries = context.ModelState
                .Where(kv => kv.Value != null && kv.Value.Errors.Count > 0)
                .ToList();

            // Lỗi đọc JSON (sai kiểu, số vượt int...) nằm ở khoá "$..."; kèm theo đó ASP.NET luôn báo thêm
            // "The request field is required." cho tham số body — câu đó chỉ gây rối, bỏ khi đã có lỗi JSON.
            var hasJsonError = entries.Any(kv => kv.Key.StartsWith("$"));

            var errors = new Dictionary<string, string[]>();
            foreach (var (key, state) in entries
                         .OrderByDescending(kv => kv.Key.StartsWith("$")))
            {
                var messages = state!.Errors
                    .Select(e => Translate(key, e.ErrorMessage, e.Exception != null))
                    .Where(m => m != null)
                    .Select(m => m!)
                    .Distinct()
                    .ToArray();

                if (hasJsonError && !key.StartsWith("$") && messages.All(IsBodyRequiredMessage))
                    continue;
                if (messages.Length > 0)
                    errors[key] = messages;
            }

            var message = errors.Values.SelectMany(v => v).FirstOrDefault() ?? DefaultTitle;
            return new BadRequestObjectResult(new
            {
                message,
                title = DefaultTitle,
                status = 400,
                errors
            });
        }

        private const string BodyRequired = "Thiếu dữ liệu gửi lên hoặc dữ liệu không đúng định dạng JSON.";

        private static bool IsBodyRequiredMessage(string message) => message == BodyRequired;

        private static readonly Regex Required = new(@"^The (?<f>.+?) field is required\.?$", RegexOptions.Compiled);
        private static readonly Regex Range = new(@"^The field (?<f>.+?) must be between (?<a>\S+) and (?<b>\S+?)\.?$", RegexOptions.Compiled);
        private static readonly Regex StringMax = new(@"^The field (?<f>.+?) must be a string with a maximum length of (?<n>\d+)\.?$", RegexOptions.Compiled);
        private static readonly Regex ArrayMax = new(@"^The field (?<f>.+?) must be a string or array type with a maximum length of '(?<n>\d+)'\.?$", RegexOptions.Compiled);
        private static readonly Regex ArrayMin = new(@"^The field (?<f>.+?) must be a string or array type with a minimum length of '(?<n>\d+)'\.?$", RegexOptions.Compiled);

        /// <summary>Câu tiếng Việt cho một lỗi. Câu đã là tiếng Việt (ErrorMessage của DTO) thì giữ nguyên.</summary>
        internal static string? Translate(string key, string? raw, bool fromException)
        {
            var text = raw?.Trim() ?? string.Empty;
            var field = FieldLabel(key);

            if (fromException || text.Length == 0
                || text.Contains("could not be converted", System.StringComparison.OrdinalIgnoreCase)
                || text.Contains("JSON", System.StringComparison.Ordinal)
                || text.Contains("is invalid", System.StringComparison.OrdinalIgnoreCase))
            {
                if (key.Length == 0 || key == "$")
                    return BodyRequired;
                return IsQuantity(key)
                    ? $"Số lượng không hợp lệ hoặc vượt giới hạn cho phép ({field})."
                    : $"Giá trị của trường \"{field}\" không hợp lệ (sai định dạng hoặc vượt giới hạn cho phép).";
            }

            if (!IsAscii(text))
                return text;

            Match m;
            if ((m = Required.Match(text)).Success)
            {
                return IsBodyParameter(key, m.Groups["f"].Value)
                    ? BodyRequired
                    : $"Thiếu thông tin bắt buộc: {m.Groups["f"].Value}.";
            }
            if ((m = Range.Match(text)).Success)
                return $"Giá trị \"{m.Groups["f"].Value}\" phải từ {m.Groups["a"].Value} đến {m.Groups["b"].Value}.";
            if ((m = StringMax.Match(text)).Success)
                return $"\"{m.Groups["f"].Value}\" tối đa {m.Groups["n"].Value} ký tự.";
            if ((m = ArrayMax.Match(text)).Success)
                return $"\"{m.Groups["f"].Value}\" vượt quá giới hạn cho phép (tối đa {m.Groups["n"].Value}).";
            if ((m = ArrayMin.Match(text)).Success)
                return $"\"{m.Groups["f"].Value}\" phải có ít nhất {m.Groups["n"].Value} phần tử.";

            return $"Dữ liệu của trường \"{field}\" không hợp lệ.";
        }

        private static bool IsAscii(string text) => text.All(c => c < 128);

        private static bool IsQuantity(string key) =>
            key.EndsWith("quantity", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Khoá "request"/"dto"... = cả body bị thiếu/không đọc được, không phải một ô cụ thể.</summary>
        private static bool IsBodyParameter(string key, string field) =>
            !key.Contains('.') && !key.Contains('[') && string.Equals(key, field, System.StringComparison.OrdinalIgnoreCase)
            && char.IsLower(key.FirstOrDefault());

        private static string FieldLabel(string key)
        {
            var trimmed = key.TrimStart('$').TrimStart('.');
            return trimmed.Length == 0 ? "dữ liệu" : trimmed;
        }
    }
}
