using System;

namespace VCL_BLL.DTOs
{
    public record RegisterRequest(string FullName, string Email, string Password, string Phone, string Role);
    public record LoginRequest(string Email, string Password);

    public class AuthResponse
    {
        public string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
        public Guid UserId { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
    }
}
