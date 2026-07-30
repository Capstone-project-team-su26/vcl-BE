using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IGoshipService
    {
        Task<List<GoshipRateOption>> GetRatesAsync(GoshipRateRequest request, string token);
        Task<GoshipOrderResponseData> CreateOrderAsync(GoshipOrderRequest request, string token);
        Task<bool> ProcessWebhookAsync(GoshipWebhookPayload payload);
    }
}
