using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IInternationalShipmentService
    {
        Task<InternationalShipmentResponseDto> CreateAsync(Guid userId, string userRole, CreateInternationalShipmentDto request);
        Task<UpdateInternationalShipmentStatusResponseDto> UpdateStatusAsync(Guid shipmentId, Guid userId, string userRole, UpdateInternationalShipmentStatusDto request);
    }
}
