using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IGHNService
    {
        Task<GHNOrderResponseData> CreateOrderAsync(GHNOrderRequest request, string token, int shopId);
        Task<GHNFeeResponseData> CalculateFeeAsync(GHNCalculateFeeRequest request, string token, int shopId);
        Task<GHNOrderDetailData> GetOrderDetailAsync(string orderCode, string token);
        Task<bool> ProcessWebhookAsync(GHNWebhookPayload payload);
    }
}
