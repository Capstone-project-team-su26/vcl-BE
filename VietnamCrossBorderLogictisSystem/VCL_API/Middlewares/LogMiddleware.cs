namespace VCL_API.Middlewares
{
    public class LogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _message;

        public LogMiddleware(RequestDelegate next, string message)
        {
            _next = next;
            _message = message;
        }

        public async Task Invoke(HttpContext context)
        {
            System.Diagnostics.Debug.WriteLine(_message, "Traceing: ");
            await _next(context);
        }
    }
}
