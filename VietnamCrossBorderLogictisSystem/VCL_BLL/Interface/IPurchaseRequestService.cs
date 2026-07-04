using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchaseRequestService
    {
        Task<PurchaseRequestResponseDto> CreatePurchaseRequestAsync(Guid userId, string userRole, CreatePurchaseRequestDto request);
        Task<PaginatedResponse<PurchaseRequestListItemDto>> GetPurchaseRequestsAsync(Guid? userId, string userRole, int pageNumber, int pageSize, string? status, string? searchKeyword);
    }
}
