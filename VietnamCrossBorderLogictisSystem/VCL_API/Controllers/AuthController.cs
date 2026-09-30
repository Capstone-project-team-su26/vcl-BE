// Path: VCL_API/Controllers/AuthController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>
        /// Start customer registration and send a six-digit OTP by email.
        /// </summary>
        [HttpPost("customer/register")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterCustomer([FromBody] CustomerRegisterRequest request)
        {
            try
            {
                await _authService.RegisterCustomerAsync(request);
                return Ok(new { message = "OTP đã được gửi đến email của bạn." });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = this.UserError(ex) });
            }
            catch (AuthException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Verify the registration OTP and create the customer account.
        /// </summary>
        [HttpPost("customer/verify-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyCustomerOtp([FromBody] VerifyCustomerOtpRequest request)
        {
            try
            {
                var customerId = await _authService.VerifyCustomerOtpAsync(request);
                return Ok(new { message = "Đăng ký tài khoản thành công.", customerId });
            }
            catch (AuthException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Send a new registration OTP after the cooldown period.
        /// </summary>
        [HttpPost("customer/resend-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> ResendCustomerOtp([FromBody] ResendCustomerOtpRequest request)
        {
            try
            {
                await _authService.ResendCustomerOtpAsync(request);
                return Ok(new { message = "OTP mới đã được gửi đến email của bạn." });
            }
            catch (AuthException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Login and get JWT token
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            try
            {
                var response = await _authService.LoginAsync(request);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                // AuthService.LoginAsync chỉ ném câu tiếng Việt (sai email/mật khẩu, bị khoá, chưa xác
                // thực) — trả đúng câu đó thay vì một câu tiếng Anh chung; vẫn 401 như cũ.
                return Unauthorized(new { message = UserFacingErrors.IsUserFacing(ex)
                    ? ex.Message
                    : "Email hoặc mật khẩu không đúng." });
            }
        }

        /// <summary>
        /// Đăng nhập bằng Google. FE lấy ID token từ Google rồi gửi lên đây.
        /// Email chưa có tài khoản thì hệ thống tự tạo tài khoản khách hàng, không cần OTP.
        /// </summary>
        [HttpPost("google")]
        [AllowAnonymous]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var response = await _authService.GoogleLoginAsync(request);
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
            catch (InvalidOperationException ex)
            {
                // Chưa cấu hình ClientId — lỗi phía mình, không phải người dùng gõ sai.
                return StatusCode(500, new { message = this.UserError(ex) });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Yêu cầu quên mật khẩu — gửi token đặt lại mật khẩu qua email.
        /// </summary>
        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
        {
            try
            {
                await _authService.ForgotPasswordAsync(request);
                return Ok(new { message = "Email đặt lại mật khẩu đã được gửi. Vui lòng kiểm tra hộp thư của bạn." });
            }
            catch (AuthException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Đặt lại mật khẩu bằng token nhận được qua email.
        /// </summary>
        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request)
        {
            try
            {
                await _authService.ResetPasswordAsync(request);
                return Ok(new { message = "Mật khẩu đã được đặt lại thành công." });
            }
            catch (AuthException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
