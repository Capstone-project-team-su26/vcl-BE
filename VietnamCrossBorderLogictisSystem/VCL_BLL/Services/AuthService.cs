using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_DLL.Interface;
using VCL_DLL.Models;
using VCL_BLL.Helpers;
using Microsoft.Extensions.Configuration;
using VCL_BLL.Interface;

namespace VCL_BLL.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;

        public AuthService(IUnitOfWork unitOfWork, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
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
    }
}
