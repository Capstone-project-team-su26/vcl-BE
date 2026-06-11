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

        public async Task SendPasswordResetEmailAsync(
            string toEmail,
            string toName,
            string resetToken)
        {
            var settings = GetSmtpSettings();
            var safeName = System.Net.WebUtility.HtmlEncode(toName);
            var safeToken = System.Net.WebUtility.HtmlEncode(resetToken);
            var message = CreateMessage(settings, toEmail, toName);
            message.Subject = "[VCL] Password reset request";
            message.Body = new BodyBuilder
            {
                HtmlBody = $@"
<div style=""font-family:Arial,sans-serif;max-width:600px;margin:0 auto"">
    <h2 style=""color:#2c3e50"">Reset your VCL password</h2>
    <p>Hello <strong>{safeName}</strong>,</p>
    <p>Use the token below to reset your password. It is valid for one hour.</p>
    <div style=""background:#f4f4f4;padding:16px;word-break:break-all;font-family:monospace"">
        {safeToken}
    </div>
    <p style=""color:#777;font-size:12px"">Ignore this email if you did not request a password reset.</p>
</div>",
                TextBody = $"Hello {toName},\n\nYour VCL password reset token is: {resetToken}\nThe token is valid for one hour."
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
