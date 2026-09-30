using VCL_DLL.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchasePaymentService
    {
        Task<ConfirmQuotationAndPayResponseDto> ConfirmQuotationAndPayAsync(
            Guid requestId, Guid userId, string userRole, ConfirmQuotationAndPayRequestDto request);


        /// <summary>
        /// Tạo khoản thu phần chênh giá của một đơn mua NCC (chưa SaveChanges — người gọi tự lưu).
        /// </summary>
        Task<PurchasePayment> CreatePriceDifferencePaymentAsync(
            PurchaseRequest purchaseRequest, PurchaseOrder purchaseOrder, decimal amount, string? paymentMethod, string? returnUrl,
            string? cancelUrl = null);

        /// <summary>
        /// Ghi nhận một khoản PHẢI HOÀN cho khách (chờ kế toán chuyển) kèm chi tiết từng dòng; số
        /// tiền = tổng các dòng. Gắn với đơn mua + cùng lý do và <paramref name="mergeWithPending"/>
        /// thì CẬP NHẬT khoản đang chờ, không đẻ thêm dòng. Chặn tổng hoàn vượt tổng đã thu.
        /// Chưa SaveChanges — người gọi tự lưu.
        /// </summary>
        Task<PurchasePayment> UpsertRefundAsync(
            PurchaseRequest purchaseRequest, PurchaseOrder? purchaseOrder, string refundType,
            List<PurchaseRefundLine> lines, Guid? actorId, bool mergeWithPending = true);

        /// <summary>
        /// Kế toán đã chuyển trả khách: đóng đúng khoản hoàn (theo đơn mua hoặc theo yêu cầu),
        /// đối chiếu số tiền nếu có, lưu mã giao dịch.
        /// </summary>
        Task<PurchasePayment> MarkRefundDoneAsync(
            Guid? purchaseOrderId, Guid? purchaseRequestId, Guid? refundId, string? transactionCode, decimal? amount);

        /// <summary>DTO các khoản hoàn kèm chi tiết.</summary>
        Task<List<PurchaseRefundDto>> BuildRefundDtosAsync(IEnumerable<PurchasePayment> refunds);

        /// <summary>Tổng hợp tiền hoàn của yêu cầu (không kiểm quyền).</summary>
        Task<PurchaseRefundSummaryDto> BuildRefundSummaryAsync(PurchaseRequest purchaseRequest);

        /// <summary>Tổng hợp tiền hoàn của yêu cầu — khách xem yêu cầu của mình, nhân viên xem tất cả.</summary>
        Task<PurchaseRefundSummaryDto> GetRefundSummaryAsync(Guid requestId, Guid userId, string userRole);

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
