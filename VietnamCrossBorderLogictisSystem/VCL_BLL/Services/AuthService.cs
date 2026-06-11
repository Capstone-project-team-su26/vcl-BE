// Path: VCL_BLL/Services/AuthService.cs

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;
using Microsoft.Extensions.Configuration;

namespace VCL_BLL.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;

        public AuthService(
            IUnitOfWork unitOfWork,
            IConfiguration configuration,
            IEmailService emailService)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _emailService = emailService;
        }

        public async Task<Guid> RegisterAsync(RegisterRequest request)
        {
            var userRepo = _unitOfWork.Repository<User>();

            // Check if email already exists
            var users = await userRepo.GetAllAsync();
            if (users.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Email already in use");

            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName,
                Email = request.Email,
                Phone = request.Phone,
                Password = PasswordHelper.HashPassword(request.Password),
                Role = "User",
                UserType = "Customer",
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            await userRepo.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return user.Id;
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase));

            if (user == null || !PasswordHelper.VerifyPassword(request.Password, user.Password))
                throw new UnauthorizedAccessException("Invalid credentials");

            if (user.Status == "Locked")
                throw new UnauthorizedAccessException("Tài khoản của bạn đã bị khóa.");

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, user.Role ?? string.Empty)
            };

            var token = JwtHelperWrapper.CreateToken(authClaims, _configuration);
            var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);

            return new AuthResponse
            {
                Token = tokenString,
                ExpiresAt = token.ValidTo,
                UserId = user.Id,
                FullName = user.FullName,
                Role = user.Role
            };
        }

        /// <inheritdoc/>
        public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();

            // Tìm user theo email (case-insensitive)
            var user = users.FirstOrDefault(u =>
                u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase));

            if (user is null)
                throw new AuthException("Email không tồn tại trong hệ thống.");

            // Sinh token ngẫu nhiên dùng Base64 an toàn (32 bytes = 256 bits entropy)
            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            var resetToken = Convert.ToBase64String(tokenBytes)
                                    .Replace("+", "-")
                                    .Replace("/", "_")
                                    .TrimEnd('=');   // URL-safe Base64

            // Cập nhật token và thời gian hết hạn (1 giờ kể từ UTC hiện tại)
            user.ResetPasswordToken = resetToken;
            user.ResetPasswordExpiry = DateTime.UtcNow.AddHours(1);

            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();

            // Gửi email chứa token cho người dùng
            await _emailService.SendPasswordResetEmailAsync(
                toEmail: user.Email,
                toName: user.FullName ?? user.Email,
                resetToken: resetToken);
        }

        /// <inheritdoc/>
        public async Task ResetPasswordAsync(ResetPasswordRequestDto request)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();

            // Tìm user có token khớp
            var user = users.FirstOrDefault(u =>
                u.ResetPasswordToken == request.Token);

            // Kiểm tra token không tồn tại HOẶC đã hết hạn
            if (user is null || user.ResetPasswordExpiry < DateTime.UtcNow)
                throw new AuthException("Token không hợp lệ hoặc đã hết hạn.");

            // Hash mật khẩu mới bằng BCrypt
            user.Password = PasswordHelper.HashPassword(request.NewPassword);

            // Xóa dấu vết token sau khi sử dụng
            user.ResetPasswordToken = null;
            user.ResetPasswordExpiry = null;

            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
