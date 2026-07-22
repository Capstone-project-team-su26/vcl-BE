using System.Threading.Tasks;

namespace VCL_BLL.Interface
{
    public interface ISePayService
    {
        /// <summary>
        /// Tạo URL ảnh QR VietQR sử dụng API của SePay/vietqr.app.
        /// </summary>
        string GenerateQrUrl(int amount, string description);

        /// <summary>
        /// Xác thực webhook gửi từ SePay (qua chữ ký HMAC hoặc qua API Key nếu có cấu hình).
        /// </summary>
        bool VerifyWebhook(string signatureHeader, string timestampHeader, string authorizationHeader, string rawJsonBody);
    }
}
