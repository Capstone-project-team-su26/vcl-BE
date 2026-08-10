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

        /// <summary>
        /// Kho bấm đồng bộ: hỏi thẳng GoShip trạng thái vận đơn rồi cập nhật vào hệ thống.
        /// Sandbox GoShip không bắn webhook nên đây là đường duy nhất để biết hàng đã tới đâu.
        /// </summary>
        Task<GoshipSyncResultDto> SyncShipmentStatusAsync(string trackingCode, string token);
    }
}
