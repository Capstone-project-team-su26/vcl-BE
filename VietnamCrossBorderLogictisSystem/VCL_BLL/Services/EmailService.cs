using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using VCL_BLL.Interface;

namespace VCL_BLL.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendPasswordResetOtpAsync(
            string toEmail,
            string toName,
            string otp,
            int expiryMinutes)
        {
            var settings = GetSmtpSettings();
            var safeName = System.Net.WebUtility.HtmlEncode(toName);
            var message = CreateMessage(settings, toEmail, toName);
            message.Subject = "[VCL] Mã OTP đặt lại mật khẩu";
            message.Body = new BodyBuilder
            {
                HtmlBody = $@"
<div style=""font-family:Arial,sans-serif;max-width:600px;margin:0 auto"">
    <h2 style=""color:#2c3e50"">Đặt lại mật khẩu VCL</h2>
    <p>Xin chào <strong>{safeName}</strong>,</p>
    <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>
    <p>Mã OTP của bạn là:</p>
    <div style=""background:#f4f4f4;padding:16px;text-align:center;font-size:30px;font-weight:bold;letter-spacing:8px"">
        {otp}
    </div>
    <p>Mã này có hiệu lực trong <strong>{expiryMinutes} phút</strong>. Không chia sẻ mã này với bất kỳ ai.</p>
    <p style=""color:#777;font-size:12px"">Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email này.</p>
</div>",
                TextBody = $"Xin chào {toName},\n\nMã OTP đặt lại mật khẩu VCL của bạn: {otp}\nMã có hiệu lực trong {expiryMinutes} phút."
            }.ToMessageBody();

            await SendAsync(message, settings);
        }

        public async Task SendCustomerRegistrationOtpAsync(
            string toEmail,
            string toName,
            string otp,
            int expiryMinutes)
        {
            var settings = GetSmtpSettings();
            var safeName = System.Net.WebUtility.HtmlEncode(toName);
            var message = CreateMessage(settings, toEmail, toName);
            message.Subject = "[VCL] Account registration verification";
            message.Body = new BodyBuilder
            {
                HtmlBody = $@"
<div style=""font-family:Arial,sans-serif;max-width:600px;margin:0 auto"">
    <h2 style=""color:#2c3e50"">Verify your VCL account</h2>
    <p>Hello <strong>{safeName}</strong>,</p>
    <p>Your account registration OTP is:</p>
    <div style=""background:#f4f4f4;padding:16px;text-align:center;font-size:30px;font-weight:bold;letter-spacing:8px"">
        {otp}
    </div>
    <p>This code is valid for <strong>{expiryMinutes} minutes</strong>. Do not share it with anyone.</p>
    <p style=""color:#777;font-size:12px"">Ignore this email if you did not make this request.</p>
</div>",
                TextBody = $"Hello {toName},\n\nYour VCL registration OTP is: {otp}\nThe code is valid for {expiryMinutes} minutes."
            }.ToMessageBody();

            await SendAsync(message, settings);
        }

        private static MimeMessage CreateMessage(
            SmtpConfiguration settings,
            string toEmail,
            string toName)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("VCL System", settings.Username));
            message.To.Add(new MailboxAddress(toName, toEmail));
            return message;
        }

        private SmtpConfiguration GetSmtpSettings()
        {
            var section = _configuration.GetSection("SmtpSettings");
            var host = section["Host"]
                ?? throw new InvalidOperationException("SmtpSettings:Host is not configured.");
            var username = section["Username"]
                ?? throw new InvalidOperationException("SmtpSettings:Username is not configured.");
            var password = section["Password"]
                ?? throw new InvalidOperationException("SmtpSettings:Password is not configured.");

            return new SmtpConfiguration(
                host,
                int.Parse(section["Port"] ?? "587"),
                username,
                password,
                bool.Parse(section["EnableSsl"] ?? "true"));
        }

        private static async Task SendAsync(MimeMessage message, SmtpConfiguration settings)
        {
            using var client = new SmtpClient();
            var socketOptions = settings.EnableSsl
                ? settings.Port == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(settings.Host, settings.Port, socketOptions);
            await client.AuthenticateAsync(settings.Username, settings.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private sealed record SmtpConfiguration(
            string Host,
            int Port,
            string Username,
            string Password,
            bool EnableSsl);
    }
}
