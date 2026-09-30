using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IOrderService
    {
        Task<CreateConsignmentResponse> CreateConsignmentAsync(CreateConsignmentRequest request, Guid userId);

        /// <summary>
        /// Ước tính chi phí đơn ký gửi TRƯỚC khi tạo — cùng phép tính với báo giá tạm tính
        /// sinh ra lúc tạo đơn, nhưng không ghi gì xuống DB.
        /// </summary>
        Task<EstimateQuotationResponse> PreviewConsignmentAsync(CreateConsignmentRequest request, Guid userId);

        /// <summary>Ước tính chi phí cho đơn Sale tạo hộ khách — không ghi gì xuống DB.</summary>
        Task<EstimateQuotationResponse> PreviewConsignmentByStaffAsync(
            CreateConsignmentByStaffRequest request, Guid staffUserId);
        Task<CreateConsignmentResponse> CreateConsignmentByStaffAsync(CreateConsignmentByStaffRequest request, Guid staffUserId);
        Task<CancelConsignmentResponse> CancelConsignmentAsync(Guid orderId, Guid userId, CancelConsignmentRequest request);
        Task<ConsignmentDetailResponse> GetConsignmentDetailAsync(Guid orderId, Guid? userId, string role);

        /// <summary>Lịch sử chuyển trạng thái của đơn.</summary>
        Task<List<OrderTimelineEntryDto>> GetOrderTimelineAsync(Guid orderId, Guid userId, string role);

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

        /// <summary>
        /// Dịch vụ khách chọn được cho từng kiện. Truyền cả tuyến và phương án vận chuyển thì bỏ các
        /// dịch vụ chỉ áp cho phương án khác.
        /// </summary>
        Task<List<ItemServiceDto>> GetItemServicesAsync(string? route, string? shippingOption);

        /// <summary>
        /// Quy đổi các cờ dịch vụ phụ của yêu cầu mua hộ (kiểm hàng / đóng gói / thùng gỗ / bảo hiểm) sang
        /// ID các PricingRule đang active tương ứng. Đơn ký gửi chọn dịch vụ theo từng kiện, không dùng cờ.
        /// </summary>
        Task<List<Guid>> ResolveServiceFlagRuleIdsAsync(bool requiresInspection, bool requiresPacking, bool requiresWoodenCrate, bool requiresInsurance);
        List<ConsignmentStatusDto> GetConsignmentStatuses();
        Task<PublicTrackingResponse> TrackConsignmentByCodeAsync(string code);

        /// <summary>Khách xác nhận đã nhận đủ hàng. Chỉ gọi được khi đơn ở DELIVERED.</summary>
        Task<OrderClosingResponseDto> ConfirmReceivedByCustomerAsync(Guid orderId, Guid userId);

        /// <summary>
        /// OM chốt đơn hoàn thành. Cần khách xác nhận trước, hoặc đã quá thời hạn chờ khách.
        /// </summary>
        /// <summary>Tự đóng đơn đã giao xong khi hết hạn khiếu nại và không còn vướng mắc.</summary>
        Task<int> AutoCompleteDeliveredOrdersAsync(int complaintWindowDays);

        Task<OrderClosingResponseDto> CompleteConsignmentAsync(
            Guid orderId, Guid userId, string userRole, CompleteConsignmentRequestDto? request);

        /// <summary>Sale từ chối đơn vì khách quá hạn không trả đợt cuối; tiền cọc không hoàn.</summary>
        Task<OrderClosingResponseDto> RejectUnpaidConsignmentAsync(
            Guid orderId, Guid userId, string userRole, RejectUnpaidConsignmentRequestDto? request);
    }
}
