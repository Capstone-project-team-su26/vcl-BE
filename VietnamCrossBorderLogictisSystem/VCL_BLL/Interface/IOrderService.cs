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

        /// <summary>
        /// Hành trình vận chuyển quốc tế của một đơn: kiện đang đi lô nào, lô tới đâu, kho VN
        /// kiểm đếm ra sao. Dùng chung cho cả ký gửi lẫn mua hộ.
        /// </summary>
        Task<List<ConsignmentShipmentGroup>> GetOrderShipmentJourneyAsync(Guid orderId);

        /// <summary>
        /// Hàng đợi "Đơn hàng cần xử lý" của Sale: mỗi dòng một việc, tách theo nhóm kiện
        /// (giao thẳng / gửi lại kho).
        /// </summary>
        Task<OrderActionQueueResponse> GetActionQueueAsync(string userRole);

        /// <summary>Sale báo cho kho vào lập phiếu nhập kho. Chỉ gọi được sau khi khách tất toán.</summary>
        Task<NotifyWarehouseResponse> NotifyWarehouseAsync(
            Guid orderId, Guid userId, string userRole, NotifyWarehouseRequest request);

        /// <summary>
        /// Hàng đợi việc của Sale ở chặng cuối. <paramref name="settled"/> false là đơn kho đã
        /// kiểm xong đang chờ tất toán, true là đơn khách trả xong đang chờ lập phiếu xuất kho.
        /// </summary>
        Task<SettlementQueueResponse> GetSettlementQueueAsync(
            string userRole, bool settled, Guid? customerUserId = null);
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
