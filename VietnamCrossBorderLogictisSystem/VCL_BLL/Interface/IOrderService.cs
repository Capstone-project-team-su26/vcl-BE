using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IOrderService
    {
        Task<CreateConsignmentResponse> CreateConsignmentAsync(CreateConsignmentRequest request, Guid userId);
        Task<CreateConsignmentResponse> CreateConsignmentByStaffAsync(CreateConsignmentByStaffRequest request, Guid staffUserId);
        Task<CancelConsignmentResponse> CancelConsignmentAsync(Guid orderId, Guid userId, CancelConsignmentRequest request);
        Task<ConsignmentDetailResponse> GetConsignmentDetailAsync(Guid orderId, Guid? userId, string role);
        Task<ValidateItemsResponse> ValidateConsignmentItemsAsync(ValidateItemsRequest request);
        Task<UpdateConsignmentStatusResponse> UpdateConsignmentStatusAsync(Guid orderId, UpdateConsignmentStatusRequest request);
        Task<PaginatedResponse<ConsignmentResponseDto>> GetConsignmentsAsync(Guid? userId, string role, int pageNumber, int pageSize, string? status, string? searchCode);
        Task<List<string>> GetConsignmentRoutesAsync();
        Task<List<string>> GetShippingOptionsAsync(string? route);
        List<ConsignmentStatusDto> GetConsignmentStatuses();
        Task<PublicTrackingResponse> TrackConsignmentByCodeAsync(string code);
    }
}
