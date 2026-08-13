using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IAuthService
    {
        Task<AuthResponse> LoginAsync(LoginRequest request);

        /// <summary>
        /// Đăng nhập bằng Google. Chưa có tài khoản thì tự tạo mới (khỏi qua bước OTP vì Google
        /// đã xác minh email). Trả về đúng dạng kết quả như đăng nhập thường.
        /// </summary>
        Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request);
        Task RegisterCustomerAsync(CustomerRegisterRequest request);
        Task<Guid> VerifyCustomerOtpAsync(VerifyCustomerOtpRequest request);
        Task ResendCustomerOtpAsync(ResendCustomerOtpRequest request);

        /// <summary>
        /// Xử lý yêu cầu quên mật khẩu: sinh token và gửi email.
        /// </summary>
        Task ForgotPasswordAsync(ForgotPasswordRequestDto request);

        /// <summary>
        /// Xác thực token và đặt lại mật khẩu mới.
        /// </summary>
        Task ResetPasswordAsync(ResetPasswordRequestDto request);
    }
}
