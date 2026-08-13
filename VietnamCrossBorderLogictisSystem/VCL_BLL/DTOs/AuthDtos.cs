using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public record LoginRequest(string Email, string Password);

    /// <summary>
    /// Đăng nhập bằng Google. FE lấy ID token từ Google rồi gửi lên đây.
    /// <para>
    /// Chỉ nhận ID token chứ không nhận email — email do client gửi thì ai cũng khai được bất kỳ
    /// địa chỉ nào và chiếm tài khoản người khác. Backend tự đọc email ra từ token sau khi đã
    /// kiểm chữ ký của Google.
    /// </para>
    /// </summary>
    public class GoogleLoginRequest
    {
        [Required(ErrorMessage = "Thiếu Google ID token.")]
        public string IdToken { get; set; } = string.Empty;
    }

    public class CustomerRegisterRequest
    {
        [Required, StringLength(255)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8), StringLength(100)]
        public string Password { get; set; } = string.Empty;

        [Required, Phone, StringLength(50)]
        public string Phone { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string Country { get; set; } = string.Empty;

        [Required]
        public string Address { get; set; } = string.Empty;
    }

    public class VerifyCustomerOtpRequest
    {
        [Required, EmailAddress, StringLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, RegularExpression(@"^\d{6}$")]
        public string Otp { get; set; } = string.Empty;
    }

    public class ResendCustomerOtpRequest
    {
        [Required, EmailAddress, StringLength(255)]
        public string Email { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
        public Guid UserId { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string Region { get; set; } = string.Empty;
    }
}
