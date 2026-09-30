using System.Net;
using System.Text.Json;
using VCL_API.Infrastructure;
using VCL_BLL.Exceptions;

namespace VCL_API.Middlewares
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "Unhandled exception occurred at {Method} {Path}.",
                    context.Request.Method, context.Request.Path.Value);

                var response = context.Response;
                response.ContentType = "application/json";

                var statusCode = ex switch
                {
                    NotFoundException => HttpStatusCode.NotFound,
                    ArgumentException => HttpStatusCode.BadRequest,
                    _ => HttpStatusCode.InternalServerError
                };

                response.StatusCode = (int)statusCode;

                // Lỗi nghiệp vụ (câu tiếng Việt) giữ nguyên câu; lỗi ngoài dự kiến (DbUpdateException,
                // NullReferenceException…) không đưa câu tiếng Anh kỹ thuật ra màn hình — chi tiết đã
                // ở log phía trên. Giữ `error` + `status` như cũ, thêm `message` (web đọc trường này trước).
                var userMessage = UserFacingErrors.MessageFor(ex);
                var result = JsonSerializer.Serialize(new
                {
                    message = userMessage,
                    error = userMessage,
                    status = (int)statusCode
                });

                await response.WriteAsync(result);
            }

        }
    }
}
