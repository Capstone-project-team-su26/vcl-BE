using System;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Ai được quyết định một chứng từ gắn với kho: đúng quản lý của kho đó, và không phải người
    /// đã lập chứng từ — kể cả Admin.
    /// </summary>
    public static class WarehouseApprovalPolicy
    {
        public static bool IsAdmin(string? role) =>
            role?.Trim().ToUpperInvariant() is "ADMIN" or "ADMINISTRATOR";

        /// <summary>Kiểm quyền quyết định và trả về kho của chứng từ.</summary>
        /// <param name="unitOfWork">Unit of work của request.</param>
        /// <param name="warehouseId">Kho của chứng từ.</param>
        /// <param name="actorId">Người đang quyết định.</param>
        /// <param name="actorRole">Vai trò trong token.</param>
        /// <param name="creatorId">Người lập chứng từ (hoặc người kiểm đếm khi duyệt biên bản).</param>
        /// <param name="reason">Lý do; bắt buộc khi Admin quyết định thay quản lý kho.</param>
        /// <param name="documentLabel">Tên chứng từ dùng trong thông báo lỗi.</param>
        public static async Task<Warehouse> EnsureCanDecideAsync(
            IUnitOfWork unitOfWork,
            Guid warehouseId,
            Guid actorId,
            string? actorRole,
            Guid? creatorId,
            string? reason,
            string documentLabel)
        {
            if (creatorId.HasValue && creatorId.Value == actorId)
            {
                throw new UnauthorizedAccessException(
                    $"Người lập {documentLabel} không được tự duyệt. Cần quản lý kho khác người lập quyết định.");
            }

            var warehouse = await unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId)
                ?? throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");

            if (warehouse.ManagerId.HasValue)
            {
                if (warehouse.ManagerId.Value == actorId) return warehouse;

                if (IsAdmin(actorRole))
                {
                    if (string.IsNullOrWhiteSpace(reason))
                    {
                        throw new ArgumentException(
                            $"Admin quyết định {documentLabel} thay quản lý kho {warehouse.WarehouseName} phải ghi lý do.");
                    }

                    return warehouse;
                }

                throw new UnauthorizedAccessException(
                    $"Chỉ quản lý kho {warehouse.WarehouseName} được quyết định {documentLabel}.");
            }

            // Kho chưa gán quản lý riêng: Operations Manager / Admin duyệt.
            if (!StaffRoleHelper.CanApproveWarehouseStore(actorRole))
            {
                throw new UnauthorizedAccessException(
                    $"Chỉ Operations Manager hoặc Admin được quyết định {documentLabel}.");
            }

            return warehouse;
        }
    }
}
