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

        /// <summary>Sale ghi đã giao kèm ảnh ký nhận khi hãng không báo về.</summary>
        Task<DeliveryRequestDto> RecordProofAsync(Guid deliveryRequestId, Guid userId, string userRole, DeliveryProofRequest request);

        /// <summary>Kho nhận lại hàng hoàn sau giao thất bại — kiện về khu nhận, giao lại được.</summary>
        Task<DeliveryRequestDto> ReceiveReturnAsync(Guid deliveryRequestId, Guid userId, string userRole, ReceiveReturnedDeliveryRequest request);

        Task<DeliveryRequestDto> GetByIdAsync(Guid deliveryRequestId);

        /// <summary>Như trên nhưng khách chỉ xem được yêu cầu giao của đơn mình.</summary>
        Task<DeliveryRequestDto> GetByIdAsync(Guid deliveryRequestId, Guid userId, string userRole);

        /// <summary>
        /// Tiến trình giao hàng của MỘT đơn, dành cho khách theo dõi.
        /// <para>
        /// Khách chỉ xem được đơn của chính mình; nhân viên xem được mọi đơn. Endpoint danh sách
        /// phiếu giao không lọc theo khách nên không thể mở thẳng cho khách dùng.
        /// </para>
        /// </summary>
        Task<OrderDeliveryTrackingDto> GetOrderTrackingAsync(Guid orderId, Guid userId, string userRole);

        Task<List<DeliveryRequestDto>> GetListAsync(string? status, Guid? orderId);
    }
}
