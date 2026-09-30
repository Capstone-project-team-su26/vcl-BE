using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchaseRequestService
    {
        Task<PurchaseRequestResponseDto> CreatePurchaseRequestAsync(Guid userId, string userRole, CreatePurchaseRequestDto request);
        Task<PurchaseRequestResponseDto> CreatePurchaseRequestByStaffAsync(Guid staffUserId, CreatePurchaseRequestByStaffDto request);
        Task<PaginatedResponse<PurchaseRequestListItemDto>> GetPurchaseRequestsAsync(Guid? userId, string userRole, int pageNumber, int pageSize, string? status, string? searchKeyword, string? stage = null);
        Task<PurchaseRequestDetailResponseDto> GetPurchaseRequestDetailAsync(Guid requestId, Guid? userId, string userRole);
        Task<UpdatePurchaseRequestStatusResponseDto> UpdatePurchaseRequestStatusAsync(Guid requestId, Guid userId, string userRole, UpdatePurchaseRequestStatusRequestDto request);
        /// <summary>
        /// Khách từ chối báo giá mua hộ (đối xứng với <c>PUT /api/quotations/{id}/reject</c> bên ký gửi).
        /// </summary>
        Task<UpdatePurchaseRequestStatusResponseDto> RejectPurchaseQuotationAsync(Guid requestId, Guid? userId, string userRole, string? rejectionReason);



        /// <summary>Nhật ký đổi trạng thái của một yêu cầu mua hộ, mới nhất trước.</summary>
        Task<List<PurchaseRequestHistoryItemDto>> GetPurchaseRequestHistoryAsync(Guid requestId, Guid? userId, string userRole);

        Task<PurchaseQuotationResponseDto> CreatePurchaseQuotationAsync(Guid requestId, Guid userId, string userRole, CreatePurchaseQuotationDto request);
        Task<PurchaseQuotationResponseDto> GetPurchaseQuotationByRequestIdAsync(Guid requestId);
        Task<CalculatePurchaseQuotationFeesResponseDto> CalculatePurchaseQuotationFeesAsync(Guid requestId, CalculatePurchaseQuotationFeesRequestDto request);
    }
}
