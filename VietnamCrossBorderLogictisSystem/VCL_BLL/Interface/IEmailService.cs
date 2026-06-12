// Path: VCL_BLL/Interface/IEmailService.cs

namespace VCL_BLL.Interface
{
    public interface IEmailService
    {
    /// <summary>
    /// Gửi OTP đặt lại mật khẩu qua email.
    /// </summary>
    Task SendPasswordResetOtpAsync(string toEmail, string toName, string otp, int expiryMinutes);
    Task SendCustomerRegistrationOtpAsync(string toEmail, string toName, string otp, int expiryMinutes);
    }
}
