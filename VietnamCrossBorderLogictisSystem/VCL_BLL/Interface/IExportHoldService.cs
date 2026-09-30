using System;
using System.Threading.Tasks;

namespace VCL_BLL.Interface
{
    /// <summary>Bật/tắt giữ hàng của một đơn tại kho nguồn.</summary>
    public interface IExportHoldService
    {
        Task<object> SetHoldAsync(Guid userId, string role, Guid orderId, bool hold, string reason);
    }
}
