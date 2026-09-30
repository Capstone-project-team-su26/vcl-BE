using VCL_BLL.Interface;
using VCL_BLL.Services;

namespace VCL_API.Workers
{
    /// <summary>
    /// Tự đóng đơn đã giao xong khi hết hạn khiếu nại. Phần lớn khách nhận hàng xong không vào web
    /// bấm xác nhận, nên nếu chỉ chờ khách hoặc chờ OM chốt tay thì đơn nằm ở "đã giao" mãi.
    /// </summary>
    public class OrderAutoCompleteWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OrderAutoCompleteWorker> _logger;

        public OrderAutoCompleteWorker(
            IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OrderAutoCompleteWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var minutes = int.TryParse(_configuration["Closing:AutoCompleteIntervalMinutes"], out var m) && m > 0 ? m : 60;

            // Chờ ứng dụng khởi động xong rồi mới chạy lần đầu, để không tranh DB với migration/seed.
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
                    var window = ParcelIncidentService.ComplaintWindowDays(_configuration);
                    var closed = await orders.AutoCompleteDeliveredOrdersAsync(window);
                    if (closed > 0)
                        _logger.LogInformation("Tự hoàn thành {Count} đơn đã giao quá {Days} ngày.", closed, window);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi tự hoàn thành đơn đã giao.");
                }

                try { await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }
    }
}
