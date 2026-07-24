using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchasePaymentService
    {
        Task<ConfirmQuotationAndPayResponseDto> ConfirmQuotationAndPayAsync(
            Guid requestId, Guid userId, string userRole, ConfirmQuotationAndPayRequestDto request);

        /// <summary>
        /// Staff chốt chi phí phát sinh sau khi hàng về và tạo đợt thanh toán cuối.
        /// Không phát sinh gì thì đơn chuyển COMPLETED luôn, không bắt khách thanh toán thêm.
        /// </summary>
        Task<CreatePurchaseFinalPaymentResponseDto> CreateFinalPaymentAsync(
            Guid requestId, Guid userId, string userRole, CreatePurchaseFinalPaymentRequestDto request);

        /// <summary>Lịch sử thanh toán của một đơn mua hộ (Customer xem đơn của mình, Staff xem tất cả).</summary>
        Task<PurchasePaymentHistoryResponseDto> GetPaymentHistoryAsync(Guid requestId, Guid userId, string userRole);

        /// <summary>Xử lý webhook payOS: xác thực + cập nhật trạng thái thanh toán. Trả true nếu đã ghi nhận thành công.</summary>
        Task<bool> HandlePayOsWebhookAsync(string rawJsonBody);

        /// <summary>Xử lý webhook SePay: xác thực + cập nhật trạng thái thanh toán. Trả true nếu đã ghi nhận thành công.</summary>
        Task<bool> HandleSePayWebhookAsync(SePayWebhookDto dto);

        /// <summary>
        /// Hỏi trực tiếp payOS trạng thái thanh toán rồi cập nhật DB (thay cho webhook).
        /// Trả về trạng thái payOS: PENDING | PAID | CANCELLED | EXPIRED.
        /// </summary>
        Task<string> SyncPaymentStatusAsync(long orderCode);
    }
}
