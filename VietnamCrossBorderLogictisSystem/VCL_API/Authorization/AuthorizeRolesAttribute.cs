using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using VCL_BLL.Helpers;

namespace VCL_API.Authorization
{
    /// <summary>
    /// Thay cho <c>[Authorize(Roles = "...")]</c> ở mọi endpoint có nhân viên kho.
    /// <para>
    /// <c>Roles = "..."</c> so khớp CHÍNH XÁC từng chữ, trong khi cột <c>USERS.role</c> là chữ tự do và
    /// tài khoản kho trên production có nhiều cách viết (<c>Warehouse</c>, <c>Warehouse Staff</c>,
    /// <c>WarehouseTQ</c>, <c>Warehouse Staff VN</c>…). Các tài khoản đó bị 403 ở mọi endpoint kho.
    /// </para>
    /// <para>
    /// Attribute này giữ NGUYÊN tập vai trò của từng endpoint (vẫn so khớp chính xác như cũ), chỉ
    /// thêm một điều: nếu danh sách có nhân viên kho (<c>WarehouseStaff</c>) thì mọi biến thể tên vai
    /// trò nhân viên kho (<see cref="StaffRoleHelper.IsWarehouseStaffRole"/>) cũng được nhận.
    /// Quản lý kho (<c>WarehouseManager</c>) không phải nhân viên kho nên không được nhận thêm.
    /// Vùng/kho được gán vẫn do service kiểm (<see cref="WarehouseAccessPolicy"/>).
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class AuthorizeRolesAttribute : AuthorizeAttribute, IAuthorizationRequirementData
    {
        /// <param name="roles">Danh sách vai trò phân tách bằng dấu phẩy, y như <c>Roles = "..."</c> cũ.</param>
        public AuthorizeRolesAttribute(string roles)
        {
            AllowedRoles = (roles ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>Tập vai trò của endpoint (chưa kể biến thể nhân viên kho).</summary>
        public IReadOnlyList<string> AllowedRoles { get; }

        public IEnumerable<IAuthorizationRequirement> GetRequirements()
        {
            yield return new RolesOrWarehouseStaffRequirement(AllowedRoles);
        }
    }

    /// <summary>
    /// Policy dùng chung của <see cref="AuthorizeRolesAttribute"/>. Tự là handler của chính nó
    /// (PassThroughAuthorizationHandler gọi), nên không cần đăng ký DI.
    /// </summary>
    public sealed class RolesOrWarehouseStaffRequirement
        : AuthorizationHandler<RolesOrWarehouseStaffRequirement>, IAuthorizationRequirement
    {
        public RolesOrWarehouseStaffRequirement(IReadOnlyList<string> allowedRoles)
        {
            AllowedRoles = allowedRoles;
            AllowsWarehouseStaff = allowedRoles.Any(StaffRoleHelper.IsWarehouseStaffRole);
        }

        public IReadOnlyList<string> AllowedRoles { get; }

        /// <summary>Danh sách có nhân viên kho → nhận cả biến thể tên vai trò nhân viên kho.</summary>
        public bool AllowsWarehouseStaff { get; }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, RolesOrWarehouseStaffRequirement requirement)
        {
            if (IsAllowed(context.User, requirement))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }

        private static bool IsAllowed(ClaimsPrincipal user, RolesOrWarehouseStaffRequirement requirement)
        {
            // Giữ đúng ngữ nghĩa [Authorize(Roles=...)] cũ: IsInRole so khớp chính xác.
            if (requirement.AllowedRoles.Any(user.IsInRole)) return true;
            if (!requirement.AllowsWarehouseStaff) return false;

            return user.Identities
                .SelectMany(identity => identity.FindAll(identity.RoleClaimType))
                .Any(claim => StaffRoleHelper.IsWarehouseStaffRole(claim.Value));
        }
    }
}
