using System.Net;
using System.Text.Json;
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

                _logger.LogError(ex, "Unhandled exception occurred.");

                var response = context.Response;
                response.ContentType = "application/json";

                var statusCode = ex switch
                {
                    NotFoundException => HttpStatusCode.NotFound,
                    ArgumentException => HttpStatusCode.BadRequest,
                    _ => HttpStatusCode.InternalServerError
                };

                response.StatusCode = (int)statusCode;

                var result = JsonSerializer.Serialize(new
                {
                    error = ex.Message,
                    status = (int)statusCode
                });

                await response.WriteAsync(result);
            }

        }
    }
}
