using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Đọc hành trình hàng: khách theo dõi đơn của mình, Sale theo dõi lô cần cập nhật.
    /// Phần GHI mốc nằm ở <see cref="IExportShipmentService"/>.
    /// </summary>
    public interface IShipmentTrackingService
    {
        Task<OrderTrackingDto> GetOrderTrackingAsync(Guid userId, string role, Guid orderId);

        Task<PagedTrackedOrdersDto> GetTrackedOrdersAsync(Guid userId, string role, TrackedOrderQuery query);

        Task<ShipmentTimelineDto> GetShipmentTimelineAsync(Guid userId, string role, Guid shipmentId);

        Task<List<ShipmentTrackingSummaryDto>> GetInTransitQueueAsync(Guid userId, string role, Guid? originWarehouseId, bool attentionOnly);
    }
}
