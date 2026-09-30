using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IConsignmentPaymentService
    {
        Task<ConfirmQuotationPaymentResponse> ConfirmQuotationAndPayAsync(
            Guid quotationId, Guid userId, ConfirmQuotationPaymentRequest request);

        /// <summary>Sale tạo thanh toán cuối (phần còn lại + phí phát sinh) khi kiện đã về kho đích.</summary>
        Task<CreateFinalPaymentResponse> CreateFinalPaymentAsync(
            Guid orderId, Guid userId, string userRole, CreateFinalPaymentRequest request);

        /// <summary>Lấy danh sách các đợt thanh toán của một đơn ký gửi.</summary>
        Task<OrderPaymentsResponse> GetOrderPaymentsAsync(Guid orderId, Guid userId, string userRole);

        /// <summary>Lấy lịch sử thanh toán chi tiết (kèm Customer + báo giá) của một đơn ký gửi.</summary>
        Task<PaymentHistoryResponse> GetPaymentHistoryAsync(Guid orderId, Guid userId, string userRole);

        /// <summary>Xử lý webhook payOS cho thanh toán ký gửi. Trả true nếu đã ghi nhận khoản thanh toán thuộc bảng PAYMENTS.</summary>
        Task<bool> HandlePayOsWebhookAsync(string rawJsonBody);

        /// <summary>Xử lý webhook SePay cho thanh toán ký gửi. Trả true nếu đã ghi nhận khoản thanh toán thành công.</summary>
        Task<bool> HandleSePayWebhookAsync(SePayWebhookDto dto);

        /// <summary>
        /// Hỏi trực tiếp payOS trạng thái thanh toán rồi cập nhật DB (thay cho webhook).
        /// Trả về trạng thái payOS: PENDING | PAID | CANCELLED | EXPIRED.
        /// </summary>
        Task<string> SyncPaymentStatusAsync(long orderCode);

        /// <summary>
        /// Chốt phí lưu kho và phát hành mã thanh toán để thu một lần lúc khách đến lấy hàng.
        /// <para>
        /// Phí lưu kho chạy theo ngày nên không thể gộp vào đợt tất toán trước đó: khách trả nốt
        /// hôm nay mà mười ngày sau mới lấy thì mười ngày ấy chưa ai thu. Chốt tại đúng thời điểm
        /// xuất hàng là cách duy nhất ra đúng số, và cũng khỏi phải sinh giao dịch lặt vặt mỗi ngày.
        /// </para>
        /// <para>
        /// Gọi lại khi khoản cũ chưa trả thì khoản cũ bị huỷ và phát hành lại theo số ngày mới —
        /// tiền phải khớp thời điểm quét mã, không phải thời điểm bấm nút lần đầu.
        /// </para>
        /// </summary>
        /// <summary>Phí giao lại sau giao thất bại: ghi lên hoá đơn và phát hành khoản thu riêng.</summary>
        Task<VCL_DLL.Models.Payment> IssueRedeliveryFeePaymentAsync(Guid orderId, Guid deliveryRequestId, string deliveryCode, decimal amount);

        Task<CreateStorageFeePaymentResponse> CreateStorageFeePaymentAsync(
            Guid orderId, Guid userId, string userRole, CreateStorageFeePaymentRequest request);
    }
}
