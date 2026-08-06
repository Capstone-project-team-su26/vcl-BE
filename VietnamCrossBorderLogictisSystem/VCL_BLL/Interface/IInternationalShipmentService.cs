using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IInternationalShipmentService
    {
        Task<InternationalShipmentResponseDto> CreateAsync(Guid userId, string userRole, CreateInternationalShipmentDto request);

        /// <summary>Danh sách lô vận chuyển quốc tế, lọc theo tab/trạng thái/kho.</summary>
        Task<PaginatedResponse<InternationalShipmentResponseDto>> GetListAsync(
            int pageNumber, int pageSize, string? statusTab, string? status, Guid? originWarehouseId, string? search);

        /// <summary>Chi tiết một lô vận chuyển quốc tế.</summary>
        Task<InternationalShipmentResponseDto> GetByIdAsync(Guid shipmentId);
        Task<UpdateInternationalShipmentStatusResponseDto> UpdateStatusAsync(Guid shipmentId, Guid userId, string userRole, UpdateInternationalShipmentStatusDto request);
        Task<ConfirmShipmentArrivalResponseDto> ConfirmArrivalAsync(Guid shipmentId, Guid userId, string userRole, ConfirmShipmentArrivalDto request);
        Task<DestinationReceivingResponseDto> ConfirmDestinationReceivingAsync(Guid shipmentId, Guid userId, string userRole, DestinationReceivingDto request);
    }
}
