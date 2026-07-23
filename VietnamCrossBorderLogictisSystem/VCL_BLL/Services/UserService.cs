using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Enums;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class UserService : IUserService
    {
        private readonly IUnitOfWork _unitOfWork;

        public UserService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task LockUserAsync(Guid userId)
        {
            var user = await GetUserOrThrowAsync(userId);
            user.Status = "Locked";
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task UnlockUserAsync(Guid userId)
        {
            var user = await GetUserOrThrowAsync(userId);
            user.Status = "Active";
            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<Guid> CreateEmployeeAsync(RegisterRequest request)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();

            // Không cho phép tạo tài khoản role Admin
            if (string.Equals(request.Role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Không được phép tạo tài khoản với quyền Admin.");

            if (users.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Email đã được sử dụng.");

            RegionCode? parsedRegion = null;
            if (!string.IsNullOrWhiteSpace(request.Region))
            {
                if (Enum.TryParse<RegionCode>(request.Region.Trim(), true, out var r))
                {
                    parsedRegion = r;
                }
                else
                {
                    throw new ArgumentException($"Mã vùng/quốc gia '{request.Region}' không hợp lệ.");
                }
            }

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
                Region = parsedRegion,
                CreatedAt = DateTime.UtcNow
            };

            await userRepo.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();
            return user.Id;
        }

        public async Task<IEnumerable<UserResponse>> GetAllUsersAsync()
        {
            var users = await _unitOfWork.Repository<User>().GetAllAsync();
            return users.Select(MapToResponse);
        }

        public async Task<UserResponse> GetUserByIdAsync(Guid userId)
        {
            var user = await GetUserOrThrowAsync(userId);
            return MapToResponse(user);
        }

        public async Task UpdateUserRoleAsync(Guid userId, string role, string? region = null)
        {
            // Không cho phép nâng quyền lên Admin
            if (string.Equals(role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Không được phép gán quyền Admin cho tài khoản.");

            var user = await GetUserOrThrowAsync(userId);
            user.Role = role;

            if (!string.IsNullOrWhiteSpace(region))
            {
                if (Enum.TryParse<RegionCode>(region.Trim(), true, out var parsedRegion))
                {
                    user.Region = parsedRegion;
                }
                else
                {
                    throw new ArgumentException($"Mã vùng/quốc gia '{region}' không hợp lệ.");
                }
            }

            _unitOfWork.Repository<User>().Update(user);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<UserProfileResponse> GetProfileAsync(Guid userId)
        {
            var user = await GetUserOrThrowAsync(userId);
            
            var response = new UserProfileResponse
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Role = user.Role,
                UserType = user.UserType,
                Status = user.Status,
                Region = user.Region?.ToString(),
                CreatedAt = user.CreatedAt
            };

            if (string.Equals(user.UserType, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var customers = await customerRepo.GetAllAsync();
                var customer = customers.FirstOrDefault(c => c.UserId == user.Id);
                if (customer != null)
                {
                    response.CustomerCode = customer.CustomerCode;
                    response.CustomerType = customer.CustomerType;
                    response.Country = customer.Country;
                    response.Address = customer.Address;
                }
            }

            return response;
        }

        public async Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
        {
            var user = await GetUserOrThrowAsync(userId);
            
            user.FullName = request.FullName;
            user.Phone = request.Phone;
            _unitOfWork.Repository<User>().Update(user);

            if (string.Equals(user.UserType, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var customers = await customerRepo.GetAllAsync();
                var customer = customers.FirstOrDefault(c => c.UserId == user.Id);
                if (customer != null)
                {
                    customer.FullName = request.FullName;
                    customer.Phone = request.Phone;
                    customer.Country = request.Country;
                    customer.Address = request.Address;
                    customerRepo.Update(customer);
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }

        private async Task<User> GetUserOrThrowAsync(Guid userId)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
                throw new Exception("Người dùng không tồn tại.");
            return user;
        }

        private static UserResponse MapToResponse(User u) => new UserResponse
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            Phone = u.Phone,
            Role = u.Role,
            UserType = u.UserType,
            Status = u.Status,
            Region = u.Region?.ToString(),
            CreatedAt = u.CreatedAt
        };
    }
}
