using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchasePaymentService
    {
        Task<ConfirmQuotationAndPayResponseDto> ConfirmQuotationAndPayAsync(
            Guid requestId, Guid userId, string userRole, ConfirmQuotationAndPayRequestDto request);

        /// <summary>Xử lý webhook payOS: xác thực + cập nhật trạng thái thanh toán. Trả true nếu đã ghi nhận thành công.</summary>
        Task<bool> HandlePayOsWebhookAsync(string rawJsonBody);

        /// <summary>Xử lý webhook SePay: xác thực + cập nhật trạng thái thanh toán. Trả true nếu đã ghi nhận thành công.</summary>
        Task<bool> HandleSePayWebhookAsync(SePayWebhookDto dto);
    }
}
