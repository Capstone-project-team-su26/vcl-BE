// Path: VCL_BLL/Services/AuthService.cs

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
                Role = request.Role,
                UserType = "Employee",
                Status = "Active",
                CreatedAt = DateTime.UtcNow
            };

            await userRepo.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return user.Id;
        }

        public async Task RegisterCustomerAsync(CustomerRegisterRequest request)
        {
            const int otpExpiryMinutes = 5;
            var email = NormalizeEmail(request.Email);
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
            var otp = GenerateOtp();
            var now = DateTime.UtcNow;

            if (user is not null && user.IsEmailVerified)
                throw new InvalidOperationException("Email da duoc su dung.");

            if (user is not null &&
                !string.Equals(user.UserType, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Email da duoc su dung.");
            }

            if (user?.LastOtpSentAt is DateTime lastSentAt &&
                now - lastSentAt < TimeSpan.FromMinutes(1))
            {
                throw new AuthException("Vui long cho 60 giay truoc khi gui lai OTP.");
            }

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customers = await customerRepo.GetAllAsync();
            Customer customer;

            if (user is null)
            {
                var userId = Guid.NewGuid();
                var customerId = Guid.NewGuid();
                user = new User
                {
                    Id = userId,
                    Email = email,
                    Role = "Customer",
                    UserType = "Customer",
                    Status = "PendingVerification",
                    CreatedAt = now,
                    IsEmailVerified = false
                };
                customer = new Customer
                {
                    Id = customerId,
                    UserId = userId,
                    CustomerCode = $"CUS-{now:yyyyMMddHHmmss}-{customerId.ToString("N")[..6].ToUpperInvariant()}",
                    CustomerType = "Individual",
                    Status = "PendingVerification",
                    CreatedAt = now
                };

                await userRepo.AddAsync(user);
                await customerRepo.AddAsync(customer);
            }
            else
            {
                customer = customers.FirstOrDefault(c => c.UserId == user.Id)
                    ?? throw new InvalidOperationException("Khong tim thay ho so khach hang.");
            }

            user.FullName = request.FullName.Trim();
            user.Phone = request.Phone.Trim();
            user.Password = PasswordHelper.HashPassword(request.Password);
            user.Status = "PendingVerification";
            user.IsEmailVerified = false;
            user.EmailVerificationOtpHash = HashOtp(email, otp);
            user.EmailVerificationOtpExpiry = now.AddMinutes(otpExpiryMinutes);
            user.LastOtpSentAt = now;
            user.EmailVerificationFailedAttempts = 0;

            customer.FullName = user.FullName;
            customer.Email = email;
            customer.Phone = user.Phone;
            customer.Country = request.Country.Trim();
            customer.Address = request.Address.Trim();
            customer.Status = "PendingVerification";

            await _unitOfWork.SaveChangesAsync();
            await _emailService.SendCustomerRegistrationOtpAsync(
                email,
                user.FullName,
                otp,
                otpExpiryMinutes);
        }

        public async Task<Guid> VerifyCustomerOtpAsync(VerifyCustomerOtpRequest request)
        {
            const int maxFailedAttempts = 5;
            var email = NormalizeEmail(request.Email);
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(u.UserType, "Customer", StringComparison.OrdinalIgnoreCase));

            if (user is null || user.IsEmailVerified)
                throw new AuthException("Khong tim thay yeu cau dang ky.");

            if (user.EmailVerificationOtpExpiry is null ||
                user.EmailVerificationOtpExpiry < DateTime.UtcNow)
            {
                throw new AuthException("Ma OTP da het han. Vui long yeu cau gui lai.");
            }

            if (user.EmailVerificationFailedAttempts >= maxFailedAttempts)
                throw new AuthException("Ban da nhap sai OTP qua so lan cho phep. Vui long gui lai OTP.");

            if (string.IsNullOrEmpty(user.EmailVerificationOtpHash))
                throw new AuthException("Ma OTP khong hop le.");

            var suppliedHash = HashOtp(email, request.Otp);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(user.EmailVerificationOtpHash),
                    Encoding.UTF8.GetBytes(suppliedHash)))
            {
                user.EmailVerificationFailedAttempts++;
                userRepo.Update(user);
                await _unitOfWork.SaveChangesAsync();
                throw new AuthException("Ma OTP khong chinh xac.");
            }

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customers = await customerRepo.GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == user.Id)
                ?? throw new InvalidOperationException("Khong tim thay ho so khach hang.");

            user.IsEmailVerified = true;
            user.Status = "Active";
            user.EmailVerificationOtpHash = null;
            user.EmailVerificationOtpExpiry = null;
            user.LastOtpSentAt = null;
            user.EmailVerificationFailedAttempts = 0;
            customer.Status = "Active";

            userRepo.Update(user);
            customerRepo.Update(customer);
            await _unitOfWork.SaveChangesAsync();

            return customer.Id;
        }

        public async Task ResendCustomerOtpAsync(ResendCustomerOtpRequest request)
        {
            const int otpExpiryMinutes = 5;
            var email = NormalizeEmail(request.Email);
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(u.UserType, "Customer", StringComparison.OrdinalIgnoreCase));

            if (user is null || user.IsEmailVerified)
                throw new AuthException("Khong tim thay yeu cau dang ky.");

            var now = DateTime.UtcNow;
            if (user.LastOtpSentAt.HasValue &&
                now - user.LastOtpSentAt.Value < TimeSpan.FromMinutes(1))
            {
                throw new AuthException("Vui long cho 60 giay truoc khi gui lai OTP.");
            }

            var otp = GenerateOtp();
            user.EmailVerificationOtpHash = HashOtp(email, otp);
            user.EmailVerificationOtpExpiry = now.AddMinutes(otpExpiryMinutes);
            user.LastOtpSentAt = now;
            user.EmailVerificationFailedAttempts = 0;
            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();

            await _emailService.SendCustomerRegistrationOtpAsync(
                email,
                user.FullName,
                otp,
                otpExpiryMinutes);
        }

        private static string NormalizeEmail(string email) =>
            email.Trim().ToLowerInvariant();

        private static string GenerateOtp() =>
            RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        private string HashOtp(string email, string otp)
        {
            var secret = _configuration["JWT:SecretKey"]
                ?? throw new InvalidOperationException("JWT:SecretKey is not configured.");
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{email}:{otp}")));
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

            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(user.UserType, "Customer", StringComparison.OrdinalIgnoreCase) &&
                 !user.IsEmailVerified))
            {
                throw new UnauthorizedAccessException("Tai khoan chua duoc xac thuc.");
            }

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
