using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IConsignmentPaymentService
    {
        Task<ConfirmQuotationPaymentResponse> ConfirmQuotationAndPayAsync(
            Guid quotationId, Guid userId, ConfirmQuotationPaymentRequest request);

        /// <summary>Xử lý webhook payOS cho thanh toán ký gửi. Trả true nếu đã ghi nhận khoản thanh toán thuộc bảng PAYMENTS.</summary>
        Task<bool> HandlePayOsWebhookAsync(string rawJsonBody);
    }
}
