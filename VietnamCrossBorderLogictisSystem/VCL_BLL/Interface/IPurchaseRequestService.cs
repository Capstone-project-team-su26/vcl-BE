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
        /// <summary>Xác nhận mua hộ &amp; cập nhật tiến độ (PURCHASED / SELLER_SHIPPED / ARRIVED_ORIGIN_WAREHOUSE).</summary>
        Task<ConfirmPurchaseResponseDto> ConfirmPurchaseAsync(Guid requestId, Guid userId, string userRole, ConfirmPurchaseRequestDto request);

        /// <summary>Nhật ký đổi trạng thái của một yêu cầu mua hộ, mới nhất trước.</summary>
        Task<List<PurchaseRequestHistoryItemDto>> GetPurchaseRequestHistoryAsync(Guid requestId, Guid? userId, string userRole);

        Task<PurchaseQuotationResponseDto> CreatePurchaseQuotationAsync(Guid requestId, Guid userId, string userRole, CreatePurchaseQuotationDto request);
        Task<PurchaseQuotationResponseDto> GetPurchaseQuotationByRequestIdAsync(Guid requestId);
        Task<CalculatePurchaseQuotationFeesResponseDto> CalculatePurchaseQuotationFeesAsync(Guid requestId, CalculatePurchaseQuotationFeesRequestDto request);
    }
}
