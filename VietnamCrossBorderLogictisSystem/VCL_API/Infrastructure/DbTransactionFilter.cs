using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using VCL_DLL.Models;

namespace VCL_API.Infrastructure
{
    /// <summary>
    /// Mỗi lệnh ghi (POST/PUT/PATCH/DELETE) chạy trong một transaction cơ sở dữ liệu.
    /// </summary>
    /// <remarks>
    /// Service cũ gọi SaveChanges nhiều lần trong một thao tác, rồi làm tiếp các bước phụ và nuốt
    /// lỗi — hỏng giữa chừng là để lại dữ liệu dở (phiếu đã duyệt mà hàng chưa lên kệ, biên bản đã
    /// lưu mà kiện không có). Bọc cả request lại thì mọi lần SaveChanges bên trong cùng commit khi
    /// request thành công, hoặc cùng rollback khi request trả lỗi (4xx/5xx) hay ném ngoại lệ.
    /// </remarks>
    public sealed class DbTransactionFilter : IAsyncActionFilter
    {
        private static readonly string[] ReadOnlyMethods = { "GET", "HEAD", "OPTIONS" };

        private readonly VCLDBContext _db;
        private readonly ILogger<DbTransactionFilter> _logger;

        public DbTransactionFilter(VCLDBContext db, ILogger<DbTransactionFilter> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var method = context.HttpContext.Request.Method;
            if (ReadOnlyMethods.Contains(method, StringComparer.OrdinalIgnoreCase) || _db.Database.CurrentTransaction != null)
            {
                await next();
                return;
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var executed = await next();

            if (executed.Exception is DbUpdateConcurrencyException && !executed.ExceptionHandled)
            {
                // Hai người cùng sửa một bản ghi (duyệt trùng, giữ tồn trùng): người đến sau nhận 409
                // để tải lại dữ liệu mới nhất thay vì ghi đè.
                executed.ExceptionHandled = true;
                executed.Result = new ObjectResult(new
                {
                    message = "Dữ liệu vừa được người khác cập nhật. Vui lòng tải lại và thao tác lại."
                })
                { StatusCode = StatusCodes.Status409Conflict };
            }

            var succeeded = (executed.Exception == null || executed.ExceptionHandled) && !IsErrorResult(executed.Result)
                            && executed.Exception is not DbUpdateConcurrencyException;

            if (succeeded)
            {
                await transaction.CommitAsync();
                return;
            }

            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Rollback {Method} {Path}: {Reason}",
                method, context.HttpContext.Request.Path,
                executed.Exception?.GetType().Name ?? $"HTTP {StatusCodeOf(executed.Result)}");
        }

        private static bool IsErrorResult(IActionResult? result) => StatusCodeOf(result) >= 400;

        private static int StatusCodeOf(IActionResult? result) => result switch
        {
            IStatusCodeActionResult { StatusCode: int code } => code,
            _ => StatusCodes.Status200OK
        };
    }
}
