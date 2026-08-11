using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IWroService
    {
        Task<WroResponseDto> CreateSingleWroRequestAsync(Guid userId, string userRole, CreateSingleWroRequestDto request);
        Task<WroResponseDto> CreateBatchWroRequestAsync(Guid userId, string userRole, CreateBatchWroRequestDto request);
        Task<WroResponseDto> CreateWroRequestAsync(Guid userId, string userRole, CreateWroRequestDto request);
        Task<PagedWroListResponseDto> GetWroRequestsAsync(Guid userId, string role, GetWroListRequestDto request);
        Task<WroDetailResponseDto> GetWroRequestDetailAsync(Guid requestId, Guid userId, string role);
        Task<UpdateWroStatusResponseDto> UpdateWroStatusAsync(Guid requestId, Guid userId, string role, UpdateWroStatusRequestDto request);
        Task<AssignShippingRouteResponseDto> AssignShippingRouteAsync(Guid requestId, Guid userId, string role, AssignShippingRouteRequestDto request);
        Task<PickingListResponseDto> CreatePickingListAsync(Guid requestId, Guid userId, string role);
        Task<UpdatePackingDetailsResponseDto> UpdatePackingDetailsAsync(Guid requestId, Guid userId, string role, UpdatePackingDetailsRequestDto request);
        Task<CompleteWroResponseDto> CompleteWroRequestAsync(Guid requestId, Guid userId, string role, CompleteWroRequestDto request);
        Task<ReleaseHandoverResponseDto> HandoverToCarrierAsync(Guid requestId, Guid userId, string role, ReleaseHandoverRequestDto request);
    }
}

