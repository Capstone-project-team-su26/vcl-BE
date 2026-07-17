using System.Collections.Generic;
using System.Threading.Tasks;

namespace VCL_BLL.Interface
{
    /// <summary>Một dòng hàng gửi sang payOS khi tạo link thanh toán.</summary>
    public record PayOsItem(string Name, int Quantity, int Price);

    /// <summary>Kết quả tạo link thanh toán payOS.</summary>
    public record PayOsPaymentLink(long OrderCode, string PaymentLinkId, string CheckoutUrl);

    /// <summary>Dữ liệu thanh toán đã xác thực từ webhook payOS.</summary>
    public record PayOsWebhookResult(bool Success, long OrderCode, string Reference, string Description);

    public interface IPayOsService
    {
        Task<PayOsPaymentLink> CreatePaymentLinkAsync(
            long orderCode,
            int amount,
            string description,
            string returnUrl,
            string cancelUrl,
            IEnumerable<PayOsItem> items);

        /// <summary>Xác thực chữ ký + parse dữ liệu webhook payOS từ JSON thô. Ném lỗi nếu chữ ký không hợp lệ.</summary>
        PayOsWebhookResult VerifyWebhook(string rawJsonBody);

        /// <summary>
        /// Đăng ký (confirm) webhook URL với payOS. payOS sẽ gọi thử URL này ngay khi đăng ký,
        /// và từ đó về sau gửi thông báo thanh toán vào URL đó. Ném lỗi nếu payOS từ chối.
        /// </summary>
        Task ConfirmWebhookAsync(string webhookUrl);
    }
}
