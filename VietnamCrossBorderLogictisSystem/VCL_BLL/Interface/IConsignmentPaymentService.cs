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
    }
}
