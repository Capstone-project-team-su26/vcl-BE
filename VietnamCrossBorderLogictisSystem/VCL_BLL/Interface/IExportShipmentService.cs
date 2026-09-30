using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Lô vận chuyển quốc tế theo luồng xuất mới: gom phiếu đã bốc xong → bàn giao cho hãng vận
    /// chuyển (hàng rời kho, trừ tồn) → cập nhật hành trình tới khi về kho VN.
    /// </summary>
    public interface IExportShipmentService
    {
        Task<List<ReadyWroForShipmentDto>> GetReadyWroRequestsAsync(Guid userId, string role, Guid? originWarehouseId, string? search);

        Task<InternationalShipmentResponseDto> CreateAsync(Guid userId, string role, CreateExportShipmentRequest request);

        Task<InternationalShipmentResponseDto> AddWroRequestsAsync(Guid userId, string role, Guid shipmentId, AddWroRequestsToShipmentRequest request);

        Task<InternationalShipmentResponseDto> RemoveWroRequestAsync(Guid userId, string role, Guid shipmentId, Guid wroRequestId);

        Task<InternationalShipmentResponseDto> CancelAsync(Guid userId, string role, Guid shipmentId);

        Task<InternationalShipmentResponseDto> HandoverAsync(Guid userId, string role, Guid shipmentId, ShipmentHandoverRequest request);

        Task<InternationalShipmentResponseDto> UpdateMilestoneAsync(Guid userId, string role, Guid shipmentId, ShipmentMilestoneRequest request);
    }
}
