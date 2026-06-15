using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
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

            if (users.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Email đã được sử dụng.");

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

        public async Task UpdateUserRoleAsync(Guid userId, string role)
        {
            var user = await GetUserOrThrowAsync(userId);
            user.Role = role;
            _unitOfWork.Repository<User>().Update(user);
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
            CreatedAt = u.CreatedAt
        };
    }
}
