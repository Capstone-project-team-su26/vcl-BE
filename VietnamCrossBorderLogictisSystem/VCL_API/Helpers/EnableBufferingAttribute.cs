using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace VCL_API.Helpers
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class EnableBufferingAttribute : Attribute, IAsyncResourceFilter
    {
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            var request = context.HttpContext.Request;
            request.EnableBuffering();

            // Read the body now before ASP.NET Core's model binder reads and disposes the stream
            using (var reader = new StreamReader(request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            {
                var rawBody = await reader.ReadToEndAsync();
                context.HttpContext.Items["RawRequestBody"] = rawBody;
            }

            // Reset stream position so that model binding can read it from the beginning
            request.Body.Position = 0;

            await next();
        }
    }
}
