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
        Task<UpdateConsignmentStatusResponse> UpdateConsignmentStatusAsync(Guid orderId, UpdateConsignmentStatusRequest request, Guid? actorUserId = null);
        Task<PaginatedResponse<ConsignmentResponseDto>> GetConsignmentsAsync(Guid? userId, string role, int pageNumber, int pageSize, string? status, string? searchCode, string? orderType = null);
        Task<List<string>> GetConsignmentRoutesAsync();
        Task<List<string>> GetShippingOptionsAsync(string? route);
        List<ConsignmentStatusDto> GetConsignmentStatuses();
        Task<PublicTrackingResponse> TrackConsignmentByCodeAsync(string code);

        /// <summary>Khách xác nhận đã nhận đủ hàng. Chỉ gọi được khi đơn ở DELIVERED.</summary>
        Task<OrderClosingResponseDto> ConfirmReceivedByCustomerAsync(Guid orderId, Guid userId);

        /// <summary>
        /// OM chốt đơn hoàn thành. Cần khách xác nhận trước, hoặc đã quá thời hạn chờ khách.
        /// </summary>
        Task<OrderClosingResponseDto> CompleteConsignmentAsync(
            Guid orderId, Guid userId, string userRole, CompleteConsignmentRequestDto? request);

        /// <summary>Sale từ chối đơn vì khách quá hạn không trả đợt cuối; tiền cọc không hoàn.</summary>
        Task<OrderClosingResponseDto> RejectUnpaidConsignmentAsync(
            Guid orderId, Guid userId, string userRole, RejectUnpaidConsignmentRequestDto? request);

        /// <summary>
        /// Quy đổi các cờ dịch vụ phụ (kiểm hàng / đóng gói / thùng gỗ / bảo hiểm) sang ID các PricingRule
        /// đang active tương ứng. Dùng chung cho ký gửi và mua hộ để một cờ luôn khớp cùng một quy định giá.
        /// </summary>
        Task<List<Guid>> ResolveServiceFlagRuleIdsAsync(bool requiresInspection, bool requiresPacking, bool requiresWoodenCrate, bool requiresInsurance);
    }
}
