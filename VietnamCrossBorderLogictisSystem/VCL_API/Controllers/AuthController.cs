// Path: VCL_API/Controllers/AuthController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

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
        /// Register a new employee account (Admin only)
        /// </summary>
        [HttpPost("admin/register")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            try
            {
                var userId = await _authService.RegisterAsync(request);
                return CreatedAtAction(nameof(Register), new { id = userId });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
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
            catch (UnauthorizedAccessException)
            {
                return Unauthorized(new { message = "Invalid credentials" });
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
