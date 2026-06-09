// Path: VCL_BLL/DTOs/PasswordDtos.cs

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// DTO cho API POST /api/auth/forgot-password
    /// </summary>
    public record ForgotPasswordRequestDto(string Email);

    /// <summary>
    /// DTO cho API POST /api/auth/reset-password
    /// </summary>
    public record ResetPasswordRequestDto(string Token, string NewPassword);
}
