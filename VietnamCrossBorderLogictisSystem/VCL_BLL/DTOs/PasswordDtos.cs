// Path: VCL_BLL/DTOs/PasswordDtos.cs

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// DTO cho API POST /api/auth/forgot-password
    /// </summary>
    public record ForgotPasswordRequestDto(string Email);

    /// <summary>
    /// DTO cho API POST /api/auth/reset-password (dùng OTP thay token)
    /// </summary>
    public record ResetPasswordRequestDto(string Email, string Otp, string NewPassword);
}
