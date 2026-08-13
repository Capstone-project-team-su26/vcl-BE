using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Yêu cầu giao hàng chặng cuối: sale lập, OM duyệt, kho gom hàng rồi đặt đơn vị giao.
    /// Điểm hội tụ của cả kiện giao thẳng lẫn kiện khách gửi lại kho VN.
    /// </summary>
    public interface IDeliveryRequestService
    {
        Task<DeliveryRequestDto> CreateAsync(Guid userId, string userRole, CreateDeliveryRequestDto request);

        Task<DeliveryRequestDto> UpdateStatusAsync(Guid deliveryRequestId, Guid userId, string userRole, UpdateDeliveryRequestStatusDto request);

        Task<DeliveryRequestDto> DispatchAsync(Guid deliveryRequestId, Guid userId, string userRole, DispatchDeliveryRequestDto request);

        Task<DeliveryRequestDto> GetByIdAsync(Guid deliveryRequestId);

        Task<List<DeliveryRequestDto>> GetListAsync(string? status, Guid? orderId);
    }
}
