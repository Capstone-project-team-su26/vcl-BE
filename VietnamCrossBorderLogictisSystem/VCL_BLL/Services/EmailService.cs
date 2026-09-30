using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using VCL_BLL.Interface;

namespace VCL_BLL.Services
{
    public class EmailService : IEmailService
    {
        /// <summary>
        /// Câu trả cho người dùng khi thiếu cấu hình SMTP: không lộ tên khoá cấu hình tiếng Anh ra
        /// màn hình; chi tiết (khoá nào thiếu) chỉ ghi log cho người vận hành.
        /// </summary>
        private const string SmtpNotConfiguredMessage =
            "Hệ thống gửi email chưa được cấu hình nên chưa gửi được mã OTP. Vui lòng liên hệ VCL để được hỗ trợ.";

        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
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
            message.Subject = "[VCL] Mã OTP xác thực đăng ký tài khoản";
            message.Body = new BodyBuilder
            {
                HtmlBody = $@"
<div style=""font-family:Arial,sans-serif;max-width:600px;margin:0 auto"">
    <h2 style=""color:#2c3e50"">Xác thực tài khoản VCL</h2>
    <p>Xin chào <strong>{safeName}</strong>,</p>
    <p>Mã OTP đăng ký tài khoản của bạn là:</p>
    <div style=""background:#f4f4f4;padding:16px;text-align:center;font-size:30px;font-weight:bold;letter-spacing:8px"">
        {otp}
    </div>
    <p>Mã này có hiệu lực trong <strong>{expiryMinutes} phút</strong>. Không chia sẻ mã này với bất kỳ ai.</p>
    <p style=""color:#777;font-size:12px"">Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email này.</p>
</div>",
                TextBody = $"Xin chào {toName},\n\nMã OTP đăng ký tài khoản VCL của bạn: {otp}\nMã có hiệu lực trong {expiryMinutes} phút."
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
            var host = section["Host"] ?? throw SmtpNotConfigured("SmtpSettings:Host");
            var username = section["Username"] ?? throw SmtpNotConfigured("SmtpSettings:Username");
            var password = section["Password"] ?? throw SmtpNotConfigured("SmtpSettings:Password");

            return new SmtpConfiguration(
                host,
                int.Parse(section["Port"] ?? "587"),
                username,
                password,
                bool.Parse(section["EnableSsl"] ?? "true"));
        }

        private InvalidOperationException SmtpNotConfigured(string key)
        {
            _logger.LogError("Thiếu cấu hình {ConfigKey} — không gửi được email.", key);
            return new InvalidOperationException(SmtpNotConfiguredMessage);
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
