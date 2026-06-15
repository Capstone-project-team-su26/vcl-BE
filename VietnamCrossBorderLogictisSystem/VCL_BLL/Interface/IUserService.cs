using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IUserService
    {
        Task LockUserAsync(Guid userId);
        Task UnlockUserAsync(Guid userId);
        Task<Guid> CreateEmployeeAsync(RegisterRequest request);
        Task<IEnumerable<UserResponse>> GetAllUsersAsync();
        Task<UserResponse> GetUserByIdAsync(Guid userId);
        Task UpdateUserRoleAsync(Guid userId, string role);
    }
}
