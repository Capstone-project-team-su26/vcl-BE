using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchaseRequestService
    {
        Task<PurchaseRequestResponseDto> CreatePurchaseRequestAsync(Guid userId, string userRole, CreatePurchaseRequestDto request);
        Task<PurchaseRequestResponseDto> CreatePurchaseRequestByStaffAsync(Guid staffUserId, CreatePurchaseRequestByStaffDto request);
        Task<PaginatedResponse<PurchaseRequestListItemDto>> GetPurchaseRequestsAsync(Guid? userId, string userRole, int pageNumber, int pageSize, string? status, string? searchKeyword);
        Task<PurchaseRequestDetailResponseDto> GetPurchaseRequestDetailAsync(Guid requestId, Guid? userId, string userRole);
        Task<UpdatePurchaseRequestStatusResponseDto> UpdatePurchaseRequestStatusAsync(Guid requestId, Guid userId, string userRole, UpdatePurchaseRequestStatusRequestDto request);
        Task<PurchaseQuotationResponseDto> CreatePurchaseQuotationAsync(Guid requestId, Guid userId, string userRole, CreatePurchaseQuotationDto request);
        Task<PurchaseQuotationResponseDto> GetPurchaseQuotationByRequestIdAsync(Guid requestId);
        Task<CalculatePurchaseQuotationFeesResponseDto> CalculatePurchaseQuotationFeesAsync(Guid requestId, CalculatePurchaseQuotationFeesRequestDto request);
    }
}
