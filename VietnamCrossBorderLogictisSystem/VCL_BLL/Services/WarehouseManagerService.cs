using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class WarehouseManagerDto
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public Guid? ManagerId { get; set; }
        public string ManagerName { get; set; } = string.Empty;
        public List<WarehouseManagerHistoryDto> History { get; set; } = new();
    }

    public class WarehouseManagerHistoryDto
    {
        public Guid? ManagerId { get; set; }
        public string ManagerName { get; set; } = string.Empty;
        public Guid? PreviousManagerId { get; set; }
        public string PreviousManagerName { get; set; } = string.Empty;
        public string ChangedByName { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    public class AssignWarehouseManagerRequest
    {
        /// <summary>Tài khoản quản lý mới. Null để gỡ quản lý (kho quay về duyệt theo vai trò).</summary>
        public Guid? ManagerId { get; set; }

        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Gán quản lý cho kho. Phiếu đang chờ duyệt tự chuyển quyền duyệt sang quản lý mới vì chốt
    /// quyền đọc quản lý hiện tại lúc quyết định; quyết định đã có không đổi.
    /// </summary>
    public class WarehouseManagerService
    {
        private static readonly string[] ManagerRoles =
            { "OPERATIONSMANAGER", "OPERATIONS", "MANAGER", "WAREHOUSEMANAGER" };

        private readonly IUnitOfWork _unitOfWork;

        public WarehouseManagerService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<WarehouseManagerDto> GetAsync(Guid warehouseId)
        {
            var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId)
                ?? throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");

            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);
            string NameOf(Guid? id) => id.HasValue && users.TryGetValue(id.Value, out var u) ? u.FullName ?? string.Empty : string.Empty;

            return new WarehouseManagerDto
            {
                WarehouseId = warehouse.Id,
                WarehouseName = warehouse.WarehouseName ?? string.Empty,
                ManagerId = warehouse.ManagerId,
                ManagerName = NameOf(warehouse.ManagerId),
                History = (await _unitOfWork.Repository<WarehouseManagerHistory>().GetAllAsync())
                    .Where(h => h.WarehouseId == warehouseId)
                    .OrderByDescending(h => h.ChangedAt)
                    .Select(h => new WarehouseManagerHistoryDto
                    {
                        ManagerId = h.ManagerId,
                        ManagerName = NameOf(h.ManagerId),
                        PreviousManagerId = h.PreviousManagerId,
                        PreviousManagerName = NameOf(h.PreviousManagerId),
                        ChangedByName = NameOf(h.ChangedById),
                        Reason = h.Reason,
                        ChangedAt = h.ChangedAt
                    })
                    .ToList()
            };
        }

        public async Task<WarehouseManagerDto> AssignAsync(Guid warehouseId, AssignWarehouseManagerRequest request, Guid actorId)
        {
            if (string.IsNullOrWhiteSpace(request?.Reason))
                throw new ArgumentException("Đổi quản lý kho phải ghi lý do.");

            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(warehouseId)
                ?? throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");

            if (request.ManagerId.HasValue)
            {
                var manager = await _unitOfWork.Repository<User>().GetByIdAsync(request.ManagerId.Value)
                    ?? throw new NotFoundException($"Không tìm thấy tài khoản với ID: {request.ManagerId}");

                if (!string.Equals(manager.Status, "Active", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Tài khoản {manager.FullName} không ở trạng thái hoạt động.");

                if (!ManagerRoles.Contains(manager.Role?.Trim().ToUpperInvariant()))
                    throw new ArgumentException($"Tài khoản {manager.FullName} không có vai trò quản lý kho.");
            }

            if (warehouse.ManagerId == request.ManagerId)
                return await GetAsync(warehouseId);

            await _unitOfWork.Repository<WarehouseManagerHistory>().AddAsync(new WarehouseManagerHistory
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouse.Id,
                ManagerId = request.ManagerId,
                PreviousManagerId = warehouse.ManagerId,
                ChangedById = actorId,
                Reason = request.Reason.Trim().Length > 500 ? request.Reason.Trim()[..500] : request.Reason.Trim(),
                ChangedAt = DateTime.UtcNow
            });

            warehouse.ManagerId = request.ManagerId;
            warehouseRepo.Update(warehouse);
            await _unitOfWork.SaveChangesAsync();

            return await GetAsync(warehouseId);
        }
    }
}
