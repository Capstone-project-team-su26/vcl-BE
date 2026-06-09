// Path: VCL_BLL/Interface/IEmailService.cs

namespace VCL_BLL.Interface
{
    public interface IEmailService
    {
        /// <summary>
        /// Gửi email đặt lại mật khẩu chứa token cho người dùng.
        /// </summary>
        /// <param name="toEmail">Địa chỉ email nhận.</param>
        /// <param name="toName">Tên hiển thị của người nhận.</param>
        /// <param name="resetToken">Token đặt lại mật khẩu.</param>
        Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetToken);
    }
}
