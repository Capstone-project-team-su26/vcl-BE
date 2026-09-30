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

        private static string NormalizeRole(string? rawRole)
        {
            if (string.IsNullOrWhiteSpace(rawRole)) return string.Empty;
            var trimmed = rawRole.Trim();
            if (string.Equals(trimmed, "Warehouse Staff", StringComparison.OrdinalIgnoreCase)) return "WarehouseStaff";
            if (string.Equals(trimmed, "Operations Manager", StringComparison.OrdinalIgnoreCase)) return "OperationsManager";
            return trimmed;
        }

        public async Task<Guid> CreateEmployeeAsync(RegisterRequest request)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var users = await userRepo.GetAllAsync();

            var normalizedRole = NormalizeRole(request.Role);

            // Không cho phép tạo tài khoản role Admin
            if (string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
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
                Role = normalizedRole,
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
            var assignments = await new UserWarehouseService(_unitOfWork).GetAllAssignmentsAsync();
            return users.Select(u => WithAssignments(MapToResponse(u), assignments)).ToList();
        }

        public async Task<UserResponse> GetUserByIdAsync(Guid userId)
        {
            var user = await GetUserOrThrowAsync(userId);
            var assignments = await new UserWarehouseService(_unitOfWork).GetAllAssignmentsAsync();
            return WithAssignments(MapToResponse(user), assignments);
        }

        private static UserResponse WithAssignments(UserResponse response, Dictionary<Guid, List<AssignedWarehouseBriefDto>> assignments)
        {
            if (assignments.TryGetValue(response.Id, out var list))
                response.AssignedWarehouses = list;
            return response;
        }

        public async Task UpdateUserRoleAsync(Guid userId, string role, string? region = null)
        {
            var normalizedRole = NormalizeRole(role);

            // Không cho phép nâng quyền lên Admin
            if (string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Không được phép gán quyền Admin cho tài khoản.");

            var user = await GetUserOrThrowAsync(userId);

            RegionCode? newRegion = user.Region;
            if (!string.IsNullOrWhiteSpace(region))
            {
                if (Enum.TryParse<RegionCode>(region.Trim(), true, out var parsedRegion))
                {
                    newRegion = parsedRegion;
                }
                else
                {
                    throw new ArgumentException($"Mã vùng/quốc gia '{region}' không hợp lệ.");
                }
            }

            // Tài khoản đang được gán kho: vai trò và vùng phải còn khớp với các kho đó, nếu không
            // nhân viên sẽ bị giới hạn theo những kho mà hồ sơ đã nói là không phụ trách nữa.
            await EnsureConsistentWithAssignmentsAsync(user, normalizedRole, newRegion);

            user.Role = normalizedRole;
            user.Region = newRegion;

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

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customers = await customerRepo.GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == user.Id);
            if (customer != null)
            {
                response.CustomerCode = customer.CustomerCode;
                response.CustomerType = customer.CustomerType;
                response.Country = customer.Country;
                response.Address = customer.Address;

                if (string.IsNullOrWhiteSpace(response.Address))
                {
                    var deliveryAddresses = await _unitOfWork.Repository<DeliveryAddress>().GetAllAsync();
                    var addr = deliveryAddresses
                        .Where(a => a.CustomerId == customer.Id)
                        .OrderByDescending(a => a.IsDefault)
                        .ThenByDescending(a => a.CreatedAt)
                        .FirstOrDefault();
                    if (addr != null)
                    {
                        response.Address = addr.Address;
                    }
                }
            }

            return response;
        }

        public async Task UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
        {
            var user = await GetUserOrThrowAsync(userId);

            user.FullName = request.FullName;
            user.Phone = request.Phone;

            if (!string.IsNullOrWhiteSpace(request.Region))
            {
                if (Enum.TryParse<VCL_DLL.Enums.RegionCode>(request.Region, true, out var parsedRegion))
                {
                    // Nhân viên đã được Admin gán kho không tự đổi vùng được — vùng đi theo kho được gán.
                    if (user.Region != parsedRegion)
                        await EnsureConsistentWithAssignmentsAsync(user, user.Role, parsedRegion);
                    user.Region = parsedRegion;
                }
            }

            _unitOfWork.Repository<User>().Update(user);

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customers = await customerRepo.GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == user.Id);
            if (customer != null)
            {
                customer.FullName = request.FullName;
                customer.Phone = request.Phone;
                if (!string.IsNullOrWhiteSpace(request.Country)) customer.Country = request.Country;
                if (!string.IsNullOrWhiteSpace(request.Address)) customer.Address = request.Address;
                customerRepo.Update(customer);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        private async Task EnsureConsistentWithAssignmentsAsync(User user, string newRole, RegionCode? newRegion)
        {
            var assignedIds = await WarehouseAccessPolicy.GetAssignedWarehouseIdsAsync(_unitOfWork, user.Id);
            if (assignedIds.Count == 0) return;

            if (!StaffRoleHelper.IsWarehouseStaffRole(newRole))
            {
                throw new ArgumentException(
                    $"Tài khoản {user.FullName} đang được gán kho. Bỏ gán kho trước khi đổi sang vai trò {newRole}.");
            }

            var warehouses = (await _unitOfWork.Repository<Warehouse>().FindAsync(w => assignedIds.Contains(w.Id))).ToList();
            var mismatch = warehouses.Where(w => !WarehouseAccessPolicy.SameRegion(w.Region, newRegion)).ToList();
            if (mismatch.Count > 0)
            {
                throw new ArgumentException(
                    $"Tài khoản {user.FullName} đang được gán kho {string.Join(", ", mismatch.Select(w => $"{w.WarehouseName} ({w.Region})"))}; " +
                    $"không đổi vùng sang {(newRegion?.ToString() ?? "trống")} được. Bỏ gán hoặc gán lại kho trước.");
            }
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
