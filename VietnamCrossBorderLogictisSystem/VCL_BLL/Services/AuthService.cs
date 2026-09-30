// Path: VCL_BLL/Services/AuthService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth;
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
                throw new InvalidOperationException("Email đã được sử dụng.");

            if (user is not null &&
                !string.Equals(user.UserType, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Email đã được sử dụng.");
            }

            if (user?.LastOtpSentAt is DateTime lastSentAt &&
                now - lastSentAt < TimeSpan.FromMinutes(1))
            {
                throw new AuthException("Vui lòng chờ 60 giây trước khi gửi lại OTP.");
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
                    ?? throw new InvalidOperationException("Không tìm thấy hồ sơ khách hàng.");
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
                throw new AuthException("Không tìm thấy yêu cầu đăng ký.");

            if (user.EmailVerificationOtpExpiry is null ||
                user.EmailVerificationOtpExpiry < DateTime.UtcNow)
            {
                throw new AuthException("Mã OTP đã hết hạn. Vui lòng yêu cầu gửi lại.");
            }

            if (user.EmailVerificationFailedAttempts >= maxFailedAttempts)
                throw new AuthException("Bạn đã nhập sai OTP quá số lần cho phép. Vui lòng yêu cầu gửi lại OTP.");

            if (string.IsNullOrEmpty(user.EmailVerificationOtpHash))
                throw new AuthException("Mã OTP không hợp lệ.");

            var suppliedHash = HashOtp(email, request.Otp);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(user.EmailVerificationOtpHash),
                    Encoding.UTF8.GetBytes(suppliedHash)))
            {
                user.EmailVerificationFailedAttempts++;
                userRepo.Update(user);
                await _unitOfWork.SaveChangesAsync();
                throw new AuthException("Mã OTP không chính xác.");
            }

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customers = await customerRepo.GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == user.Id)
                ?? throw new InvalidOperationException("Không tìm thấy hồ sơ khách hàng.");

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
                throw new AuthException("Không tìm thấy yêu cầu đăng ký.");

            var now = DateTime.UtcNow;
            if (user.LastOtpSentAt.HasValue &&
                now - user.LastOtpSentAt.Value < TimeSpan.FromMinutes(1))
            {
                throw new AuthException("Vui lòng chờ 60 giây trước khi gửi lại OTP.");
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
                ?? throw new InvalidOperationException("Hệ thống xác thực chưa được cấu hình. Vui lòng liên hệ VCL để được hỗ trợ.");
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{email}:{otp}")));
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase));

            if (user == null || !PasswordHelper.VerifyPassword(request.Password, user.Password))
                throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");

            if (user.Status == "Locked")
                throw new UnauthorizedAccessException("Tài khoản của bạn đã bị khóa.");

            if (!string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(user.UserType, "Customer", StringComparison.OrdinalIgnoreCase) &&
                 !user.IsEmailVerified))
            {
                throw new UnauthorizedAccessException("Tài khoản chưa được xác thực.");
            }

            var role = user.Role ?? string.Empty;
            var region = user.Region?.ToString() ?? string.Empty;

            // Hỗ trợ tương thích ngược (Backward compatibility) cho các tài khoản cũ
            if (string.Equals(role, "WarehouseVN", StringComparison.OrdinalIgnoreCase))
            {
                role = "WarehouseStaff";
                region = "VN";
            }
            else if (string.Equals(role, "WarehouseTQ", StringComparison.OrdinalIgnoreCase))
            {
                role = "WarehouseStaff";
                region = "TQ";
            }

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, role),
                new Claim("region", region)
            };

            var token = JwtHelperWrapper.CreateToken(authClaims, _configuration);
            var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);

            return new AuthResponse
            {
                Token = tokenString,
                ExpiresAt = token.ValidTo,
                UserId = user.Id,
                FullName = user.FullName,
                Role = role,
                Region = region
            };
        }

        /// <inheritdoc/>
        public async Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.IdToken))
                throw new ArgumentException("Thiếu Google ID token.");

            var payload = await VerifyGoogleTokenAsync(request.IdToken);

            var email = NormalizeEmail(payload.Email);
            if (string.IsNullOrWhiteSpace(email))
                throw new UnauthorizedAccessException("Tài khoản Google này không có email.");

            // Google có trả về email chưa xác minh (tài khoản doanh nghiệp tự quản lý chẳng hạn).
            // Nhận email đó là mở đường chiếm tài khoản: kẻ xấu tạo Google account mang email của
            // người khác rồi đăng nhập vào đúng tài khoản người ta.
            if (payload.EmailVerified != true)
                throw new UnauthorizedAccessException("Email của tài khoản Google này chưa được xác minh.");

            var userRepo = _unitOfWork.Repository<User>();
            var customerRepo = _unitOfWork.Repository<Customer>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

            var now = DateTime.UtcNow;

            if (user == null)
            {
                // Chưa có tài khoản thì tạo mới luôn, khỏi bắt khách đăng ký lại từ đầu. Bỏ được
                // bước OTP vì Google đã xác minh email hộ rồi. Tạo kèm Customer y như luồng đăng
                // ký thường, thiếu dòng này là khách đăng nhập được nhưng không đặt đơn được.
                var userId = Guid.NewGuid();
                var customerId = Guid.NewGuid();

                user = new User
                {
                    Id = userId,
                    Email = email,
                    FullName = string.IsNullOrWhiteSpace(payload.Name) ? email : payload.Name,
                    Role = "Customer",
                    UserType = "Customer",
                    Status = "Active",
                    CreatedAt = now,
                    IsEmailVerified = true
                };

                await userRepo.AddAsync(user);
                await customerRepo.AddAsync(new Customer
                {
                    Id = customerId,
                    UserId = userId,
                    CustomerCode = $"CUS-{now:yyyyMMddHHmmss}-{customerId.ToString("N")[..6].ToUpperInvariant()}",
                    CustomerType = "Individual",
                    Status = "Active",
                    CreatedAt = now
                });

                await _unitOfWork.SaveChangesAsync();
            }
            else
            {
                if (string.Equals(user.Status, "Locked", StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("Tài khoản của bạn đã bị khóa.");

                // Tài khoản đăng ký bằng mật khẩu nhưng chưa bấm link xác minh, nay đăng nhập
                // bằng chính email đó qua Google: Google đã xác minh nên mở khoá luôn, không bắt
                // quay lại nhập OTP nữa.
                var needsActivation = !user.IsEmailVerified
                    || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase);

                if (needsActivation)
                {
                    user.IsEmailVerified = true;
                    user.Status = "Active";
                    userRepo.Update(user);

                    var customer = (await customerRepo.GetAllAsync()).FirstOrDefault(c => c.UserId == user.Id);
                    if (customer != null)
                    {
                        customer.Status = "Active";
                        customerRepo.Update(customer);
                    }

                    await _unitOfWork.SaveChangesAsync();
                }
            }

            return BuildAuthResponse(user);
        }

        /// <summary>
        /// Kiểm ID token với Google: chữ ký phải đúng, và <c>aud</c> phải khớp ClientId của mình.
        /// Bỏ kiểm ClientId thì token do bất kỳ ứng dụng Google nào cấp cũng đăng nhập được vào
        /// hệ thống này.
        /// </summary>
        private async Task<GoogleJsonWebSignature.Payload> VerifyGoogleTokenAsync(string idToken)
        {
            var clientId = _configuration["GoogleAuth:ClientId"]?.Trim();
            if (string.IsNullOrWhiteSpace(clientId) || clientId.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Chưa cấu hình GoogleAuth:ClientId nên không xác thực được token Google. " +
                    "Tạo OAuth Client ID trong Google Cloud Console rồi điền vào cấu hình.");
            }

            try
            {
                return await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                });
            }
            catch (InvalidJwtException ex)
            {
                throw new UnauthorizedAccessException($"Token Google không hợp lệ: {ex.Message}");
            }
        }

        /// <summary>Dựng JWT của hệ thống cho một user đã xác thực xong.</summary>
        private AuthResponse BuildAuthResponse(User user)
        {
            var role = user.Role ?? string.Empty;
            var region = user.Region?.ToString() ?? string.Empty;

            if (string.Equals(role, "WarehouseVN", StringComparison.OrdinalIgnoreCase))
            {
                role = "WarehouseStaff";
                region = "VN";
            }
            else if (string.Equals(role, "WarehouseTQ", StringComparison.OrdinalIgnoreCase))
            {
                role = "WarehouseStaff";
                region = "TQ";
            }

            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, role),
                new Claim("region", region)
            };

            var token = JwtHelperWrapper.CreateToken(authClaims, _configuration);
            var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);

            return new AuthResponse
            {
                Token = tokenString,
                ExpiresAt = token.ValidTo,
                UserId = user.Id,
                FullName = user.FullName,
                Role = role,
                Region = region
            };
        }

        /// <inheritdoc/>
        public async Task ForgotPasswordAsync(ForgotPasswordRequestDto request)
        {
            const int otpExpiryMinutes = 5;
            const int rateLimitSeconds = 60;

            var email = NormalizeEmail(request.Email);
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

            if (user is null)
                throw new AuthException("Email không tồn tại trong hệ thống.");

            // Rate-limit: chờ 60 giây trước khi gửi lại
            var now = DateTime.UtcNow;
            if (user.LastResetOtpSentAt.HasValue &&
                now - user.LastResetOtpSentAt.Value < TimeSpan.FromSeconds(rateLimitSeconds))
            {
                throw new AuthException($"Vui lòng chờ {rateLimitSeconds} giây trước khi gửi lại mã OTP.");
            }

            // Sinh OTP 6 số và hash bằng HMAC-SHA256
            var otp = GenerateOtp();
            user.ResetPasswordOtpHash = HashOtp(email, otp);
            user.ResetPasswordOtpExpiry = now.AddMinutes(otpExpiryMinutes);
            user.ResetPasswordFailedAttempts = 0;
            user.LastResetOtpSentAt = now;

            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();

            await _emailService.SendPasswordResetOtpAsync(
                toEmail: user.Email,
                toName: user.FullName ?? user.Email,
                otp: otp,
                expiryMinutes: otpExpiryMinutes);
        }

        /// <inheritdoc/>
        public async Task ResetPasswordAsync(ResetPasswordRequestDto request)
        {
            const int maxFailedAttempts = 5;

            var email = NormalizeEmail(request.Email);
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();
            var user = users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

            if (user is null)
                throw new AuthException("Email không tồn tại trong hệ thống.");

            // Kiểm tra OTP hết hạn
            if (user.ResetPasswordOtpExpiry is null ||
                user.ResetPasswordOtpExpiry < DateTime.UtcNow)
                throw new AuthException("Mã OTP đã hết hạn. Vui lòng yêu cầu gửi lại.");

            // Brute-force protection
            if (user.ResetPasswordFailedAttempts >= maxFailedAttempts)
                throw new AuthException("Bạn đã nhập sai OTP quá số lần cho phép. Vui lòng yêu cầu gửi lại mã mới.");

            if (string.IsNullOrEmpty(user.ResetPasswordOtpHash))
                throw new AuthException("Mã OTP không hợp lệ.");

            // So sánh hash theo thời gian hằng định (timing-safe)
            var suppliedHash = HashOtp(email, request.Otp);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(user.ResetPasswordOtpHash),
                    Encoding.UTF8.GetBytes(suppliedHash)))
            {
                user.ResetPasswordFailedAttempts++;
                userRepo.Update(user);
                await _unitOfWork.SaveChangesAsync();
                throw new AuthException("Mã OTP không chính xác.");
            }

            // OTP hợp lệ — cập nhật mật khẩu mới
            user.Password = PasswordHelper.HashPassword(request.NewPassword);

            // Xóa dấu vết OTP sau khi dùng
            user.ResetPasswordOtpHash = null;
            user.ResetPasswordOtpExpiry = null;
            user.ResetPasswordFailedAttempts = 0;
            user.LastResetOtpSentAt = null;

            userRepo.Update(user);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
