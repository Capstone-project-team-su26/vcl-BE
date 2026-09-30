using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>Phía khách ở chặng Việt Nam: chọn hướng xử lý từng kiện, xem trước tiền tất toán.</summary>
    public interface IVnArrivalService
    {
        Task<List<ParcelHandlingDto>> GetHandlingAsync(Guid userId, string role, Guid orderId);
        Task<List<ParcelHandlingDto>> SetHandlingAsync(Guid userId, string role, Guid orderId, SetDestinationHandlingRequest request);
        Task<SettlementPreviewDto> GetSettlementPreviewAsync(Guid userId, string role, Guid orderId);
    }
}
