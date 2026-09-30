using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchaseOrderService
    {
        Task<PurchaseOrderResponseDto> CreateAsync(Guid requestId, Guid userId, string userRole, SavePurchaseOrderDto dto);
        Task<PurchaseOrderResponseDto> UpdateAsync(Guid purchaseOrderId, Guid userId, string userRole, SavePurchaseOrderDto dto);
        Task<PurchaseOrderResponseDto> SubmitAsync(Guid purchaseOrderId, Guid userId, string userRole);
        Task<PurchaseOrderResponseDto> CustomerDecisionAsync(Guid purchaseOrderId, Guid userId, string userRole, PurchaseOrderCustomerDecisionDto dto);
        Task<PurchaseOrderResponseDto> DecideAsync(Guid purchaseOrderId, Guid userId, string userRole, PurchaseOrderDecisionDto dto);
        Task<PurchaseOrderResponseDto> PlaceAsync(Guid purchaseOrderId, Guid userId, string userRole, PlacePurchaseOrderDto dto);
        Task<PurchaseOrderResponseDto> UpdateProgressAsync(Guid purchaseOrderId, Guid userId, string userRole, PurchaseOrderProgressDto dto);
        Task<PurchaseOrderResponseDto> CancelAsync(Guid purchaseOrderId, Guid userId, string userRole, CancelPurchaseOrderDto dto);

        /// <summary>
        /// Xác nhận đã hoàn tiền cho khách (giá thực thấp hơn giá báo, hoặc huỷ sau khi đặt NCC).
        /// Chỉ đóng sổ khoản hoàn — không đổi trạng thái đơn mua.
        /// </summary>
        Task<PurchaseOrderResponseDto> CompleteRefundAsync(
            Guid purchaseOrderId, Guid userId, string userRole, CompletePurchaseRefundDto dto);
        /// <summary>
        /// Sale / Admin đóng phần KHÔNG mua được của yêu cầu (chưa vào đơn mua nào) và phần NCC giao
        /// thiếu của đơn đã đặt: tạo khoản hoàn REFUND_UNFULFILLED = tiền hàng + phí mua hộ + VAT phí
        /// tương ứng, chi tiết theo từng sản phẩm.
        /// </summary>
        Task<CloseUnfulfilledResponseDto> CloseUnfulfilledAsync(Guid requestId, Guid userId, string userRole, CloseUnfulfilledDto dto);

        /// <summary>Mọi khoản hoàn của yêu cầu kèm chi tiết + tổng đã thu / đã hoàn / đang chờ.</summary>
        Task<PurchaseRefundSummaryDto> GetRefundsAsync(Guid requestId, Guid userId, string userRole);

        /// <summary>Kế toán đóng đúng một khoản hoàn của yêu cầu (đối chiếu số tiền nếu gửi).</summary>
        Task<PurchaseRefundSummaryDto> CompleteRequestRefundAsync(
            Guid requestId, Guid refundId, Guid userId, string userRole, CompletePurchaseRefundDto dto);

        Task<PurchaseOrderResponseDto> GetAsync(Guid purchaseOrderId, Guid userId, string userRole);
        Task<List<PurchaseOrderResponseDto>> ListByRequestAsync(Guid requestId, Guid userId, string userRole);
        Task<PaginatedResponse<PurchaseOrderResponseDto>> ListAsync(string userRole, string? status, string? search, int pageNumber, int pageSize);
    }
}
