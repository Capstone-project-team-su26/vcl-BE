using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VCL_BLL.Exceptions;

namespace VCL_API.Infrastructure
{
    /// <summary>
    /// Câu lỗi được phép đưa ra màn hình.
    ///
    /// Lỗi nghiệp vụ (service ném ArgumentException / NotFoundException / InvalidOperationException…
    /// với câu tiếng Việt) giữ NGUYÊN câu — đó là câu nói đúng vướng mắc cho người dùng. Lỗi ngoài
    /// dự kiến của framework / hạ tầng (DbUpdateException, NullReferenceException, SqlException,
    /// "Sequence contains no elements"…) mang câu tiếng Anh kỹ thuật: không đưa ra màn hình, thay bằng
    /// câu tiếng Việt chung và ghi đầy đủ chi tiết vào log.
    ///
    /// Chỉ đổi CHUỖI trả về — mã HTTP và hình dạng body của từng endpoint giữ như cũ.
    /// </summary>
    public static class UserFacingErrors
    {
        public const string Unexpected = "Hệ thống gặp lỗi ngoài dự kiến. Vui lòng thử lại sau.";
        public const string InvalidInput = "Dữ liệu gửi lên không hợp lệ. Vui lòng kiểm tra lại.";
        public const string NoPermission = "Bạn không có quyền thực hiện thao tác này.";
        public const string NotFound = "Không tìm thấy dữ liệu yêu cầu.";

        /// <summary>Câu có chữ tiếng Việt có dấu → câu nghiệp vụ viết cho người dùng.</summary>
        public static bool IsVietnameseMessage(string? message) =>
            !string.IsNullOrWhiteSpace(message) && message.Any(c => c > 127 && char.IsLetter(c));

        /// <summary>True khi câu của exception được đưa thẳng ra màn hình.</summary>
        public static bool IsUserFacing(Exception ex) => IsVietnameseMessage(ex.Message);

        /// <summary>Câu cho người dùng; câu kỹ thuật tiếng Anh được thay theo loại lỗi.</summary>
        public static string MessageFor(Exception ex)
        {
            if (IsUserFacing(ex)) return ex.Message;
            return ex switch
            {
                NotFoundException => NotFound,
                ArgumentException => InvalidInput,
                UnauthorizedAccessException or ForbiddenException => NoPermission,
                _ => Unexpected
            };
        }

        /// <summary>
        /// Dùng trong catch của controller thay cho <c>ex.Message</c>. Câu bị thay thì ghi log kèm
        /// đường dẫn request để người vận hành vẫn thấy chi tiết thật.
        /// </summary>
        public static string UserError(this ControllerBase controller, Exception ex)
        {
            var message = MessageFor(ex);
            if (!IsUserFacing(ex)) Log(controller.HttpContext, ex);
            return message;
        }

        public static void Log(HttpContext? context, Exception ex)
        {
            var factory = context?.RequestServices?.GetService<ILoggerFactory>();
            factory?.CreateLogger("VCL_API.UserFacingErrors")
                .LogError(ex, "Lỗi ngoài dự kiến (đã thay bằng câu chung cho người dùng) tại {Method} {Path}",
                    context?.Request?.Method, context?.Request?.Path.Value);
        }
    }
}
