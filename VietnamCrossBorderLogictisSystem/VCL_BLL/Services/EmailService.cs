// Path: VCL_BLL/Services/EmailService.cs

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

        public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetToken)
        {
            var mailSettings = _configuration.GetSection("MailSettings");

            var host = mailSettings["Host"]
                ?? throw new InvalidOperationException("MailSettings:Host is not configured.");
            var port = int.Parse(mailSettings["Port"] ?? "587");
            var senderEmail = mailSettings["SenderEmail"]
                ?? throw new InvalidOperationException("MailSettings:SenderEmail is not configured.");
            var senderName = mailSettings["SenderName"] ?? "VCL System";
            var username = mailSettings["Username"]
                ?? throw new InvalidOperationException("MailSettings:Username is not configured.");
            var password = mailSettings["Password"]
                ?? throw new InvalidOperationException("MailSettings:Password is not configured.");
            var useSsl = bool.Parse(mailSettings["UseSsl"] ?? "false");

            // Xây dựng nội dung email
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = "[VCL] Yêu cầu đặt lại mật khẩu";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = $@"
<div style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;"">
    <h2 style=""color: #2c3e50;"">Đặt lại mật khẩu của bạn</h2>
    <p>Xin chào <strong>{toName}</strong>,</p>
    <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản VCL của bạn.</p>
    <p>Sử dụng mã token sau để đặt lại mật khẩu (có hiệu lực trong <strong>1 giờ</strong>):</p>
    <div style=""background:#f4f4f4; border-left:4px solid #3498db; padding:12px 20px; margin:20px 0; font-size:16px; font-family:monospace; word-break:break-all;"">
        {resetToken}
    </div>
    <p>Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email này. Mật khẩu của bạn sẽ không thay đổi.</p>
    <hr style=""border:none; border-top:1px solid #eee; margin:30px 0;""/>
    <p style=""color:#999; font-size:12px;"">Vietnam Cross-Border Logistics System &mdash; Email tự động, vui lòng không trả lời.</p>
</div>",
                TextBody = $"Xin chào {toName},\n\nToken đặt lại mật khẩu của bạn: {resetToken}\n\nToken có hiệu lực trong 1 giờ.\n\nNếu bạn không yêu cầu, hãy bỏ qua email này."
            };

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            var secureSocketOptions = useSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

            await client.ConnectAsync(host, port, secureSocketOptions);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
