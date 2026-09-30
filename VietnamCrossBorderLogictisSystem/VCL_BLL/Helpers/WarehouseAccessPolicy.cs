using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_DLL.Enums;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Nhân viên nào được thao tác tại kho nào — nơi duy nhất quyết định, thay cho các bản kiểm
    /// "cùng vùng" rời rạc trước đây ở từng service.
    /// <list type="number">
    /// <item>Admin: không giới hạn.</item>
    /// <item>Tài khoản được Admin gán ≥1 kho (bảng USER_WAREHOUSES): chỉ đúng các kho đó.</item>
    /// <item>Chưa được gán kho nào: giữ cách cũ theo vùng (<see cref="User.Region"/>) — tài khoản
    /// đang dùng không gãy. Operations Manager không bao giờ có dòng gán nên hành vi giữ nguyên.</item>
    /// </list>
    /// Luôn đọc từ DB mỗi lần kiểm (không dựa vào claim trong token), nên Admin gán/bỏ gán có hiệu
    /// lực ngay, nhân viên không cần đăng nhập lại.
    /// </summary>
    public static class WarehouseAccessPolicy
    {
        /// <summary>Các kho tài khoản được gán. Rỗng = chưa gán (giới hạn theo vùng như cũ).</summary>
        public static async Task<List<Guid>> GetAssignedWarehouseIdsAsync(IUnitOfWork unitOfWork, Guid userId)
        {
            if (userId == Guid.Empty) return new List<Guid>();
            return (await unitOfWork.Repository<UserWarehouse>().FindAsync(a => a.UserId == userId))
                .Select(a => a.WarehouseId)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Phạm vi kho để LỌC DANH SÁCH: null = không giới hạn theo kho (Admin, hoặc chưa được gán —
        /// danh sách lọc theo vùng như cũ); ngược lại là tập kho được gán.
        /// </summary>
        public static async Task<IReadOnlyCollection<Guid>?> GetListScopeAsync(IUnitOfWork unitOfWork, Guid? userId, string? role)
        {
            if (WarehouseApprovalPolicy.IsAdmin(role) || !userId.HasValue) return null;
            var assigned = await GetAssignedWarehouseIdsAsync(unitOfWork, userId.Value);
            return assigned.Count > 0 ? assigned : null;
        }

        /// <summary>
        /// Kiểm đầy đủ — dùng ở các chỗ TRƯỚC ĐÂY đã chặn theo vùng (phiếu xuất kho, lô quốc tế):
        /// có kho được gán thì chỉ các kho đó; chưa gán thì phải khai vùng và đúng vùng của kho.
        /// </summary>
        /// <param name="unitOfWork">Unit of work của request.</param>
        /// <param name="userId">Người thao tác.</param>
        /// <param name="role">Vai trò trong token.</param>
        /// <param name="warehouseId">Kho nơi thao tác diễn ra. Null thì bỏ qua.</param>
        /// <param name="notFoundMessage">Thông báo khi không tìm thấy kho.</param>
        /// <param name="actionLabel">Mô tả việc làm trong câu lỗi vùng, ví dụ "lô của kho".</param>
        /// <param name="missingRegionHint">Câu hướng dẫn nối sau lỗi "chưa khai vùng".</param>
        public static async Task EnsureCanOperateAsync(
            IUnitOfWork unitOfWork,
            Guid userId,
            string? role,
            Guid? warehouseId,
            string notFoundMessage = "Không tìm thấy kho.",
            string actionLabel = "tại kho",
            string? missingRegionHint = null)
        {
            if (WarehouseApprovalPolicy.IsAdmin(role)) return;
            if (!warehouseId.HasValue) return;

            var warehouse = await unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId.Value)
                ?? throw new NotFoundException(notFoundMessage);

            var assigned = await GetAssignedWarehouseIdsAsync(unitOfWork, userId);
            if (assigned.Count > 0)
            {
                if (assigned.Contains(warehouse.Id)) return;
                throw new UnauthorizedAccessException(await DescribeNotAssignedAsync(unitOfWork, assigned, warehouse));
            }

            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user?.Region == null)
            {
                throw new UnauthorizedAccessException(
                    "Tài khoản chưa khai vùng phụ trách nên không thao tác được kho." + (missingRegionHint ?? string.Empty));
            }

            if (!SameRegion(warehouse.Region, user.Region))
            {
                throw new UnauthorizedAccessException(
                    $"Bạn phụ trách vùng {user.Region}, không thao tác được {actionLabel} {warehouse.WarehouseName} ({warehouse.Region}).");
            }
        }

        /// <summary>
        /// Kiểm theo kho được gán — dùng ở các chỗ TRƯỚC ĐÂY không chặn gì (nhận hàng, xếp kệ, điều
        /// chuyển, kiểm kê, nhận lô ở kho đích). Chỉ chặn khi tài khoản đã được gán kho; tài khoản chưa
        /// gán giữ nguyên hành vi cũ.
        /// </summary>
        public static async Task EnsureAssignedWarehouseAsync(IUnitOfWork unitOfWork, Guid? userId, string? role, Guid? warehouseId)
        {
            if (!userId.HasValue || !warehouseId.HasValue) return;
            if (WarehouseApprovalPolicy.IsAdmin(role)) return;

            var assigned = await GetAssignedWarehouseIdsAsync(unitOfWork, userId.Value);
            if (assigned.Count == 0 || assigned.Contains(warehouseId.Value)) return;

            var warehouse = await unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId.Value);
            throw new UnauthorizedAccessException(await DescribeNotAssignedAsync(unitOfWork, assigned, warehouse));
        }

        /// <summary>Cùng vùng, coi JP≡NB và CN≡TQ như các bộ lọc vùng cũ.</summary>
        public static bool SameRegion(RegionCode? a, RegionCode? b)
        {
            if (!a.HasValue || !b.HasValue) return false;
            return Canonical(a.Value) == Canonical(b.Value);
        }

        private static RegionCode Canonical(RegionCode region) => region switch
        {
            RegionCode.NB => RegionCode.JP,
            RegionCode.TQ => RegionCode.CN,
            _ => region
        };

        private static async Task<string> DescribeNotAssignedAsync(IUnitOfWork unitOfWork, IReadOnlyCollection<Guid> assigned, Warehouse? target)
        {
            var names = (await unitOfWork.Repository<Warehouse>().FindAsync(w => assigned.Contains(w.Id)))
                .Select(w => w.WarehouseName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n)
                .ToList();
            var assignedText = names.Count > 0 ? string.Join(", ", names) : "(kho đã bị xoá)";
            var targetText = target?.WarehouseName ?? "kho này";
            return $"Tài khoản của bạn chỉ được thao tác tại kho được gán: {assignedText}. Không thao tác được tại {targetText}.";
        }
    }
}
