using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public record RegisterRequest(string FullName, string Email, string Password, string Phone, string Role);
    public record LoginRequest(string Email, string Password);

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
    }
}
