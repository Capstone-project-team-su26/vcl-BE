using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class UserWarehouseItemDto
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseCode { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string? Region { get; set; }
        public bool IsActive { get; set; }
        public string? Note { get; set; }
        public DateTime AssignedAt { get; set; }
        public Guid? AssignedById { get; set; }
        public string AssignedByName { get; set; } = string.Empty;
    }

    public class UserWarehousesDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? Region { get; set; }
        public bool IsWarehouseRole { get; set; }

        /// <summary>True khi có ≥1 kho được gán: tài khoản chỉ thao tác được ở các kho đó.</summary>
        public bool Restricted => Warehouses.Count > 0;

        public List<UserWarehouseItemDto> Warehouses { get; set; } = new();

        /// <summary>Chỉ có ý nghĩa trong phản hồi PUT: vùng tài khoản vừa được tự đặt theo kho.</summary>
        public bool RegionChanged { get; set; }
        public string? PreviousRegion { get; set; }
    }

    public class AssignUserWarehousesRequest
    {
        /// <summary>Danh sách kho mới (thay toàn bộ). Rỗng = bỏ gán hết, quay về giới hạn theo vùng.</summary>
        public List<Guid> WarehouseIds { get; set; } = new();

        public string? Note { get; set; }
    }

    public class WarehouseStaffMemberDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
        public string AssignedByName { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    public class WarehouseStaffDto
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseCode { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string? Region { get; set; }
        public List<WarehouseStaffMemberDto> Staff { get; set; } = new();
    }

    /// <summary>
    /// Admin gán nhân viên kho vào từng kho cụ thể. Quy tắc thao tác theo kho được gán nằm ở
    /// <see cref="WarehouseAccessPolicy"/>; service này chỉ quản lý dữ liệu gán.
    /// </summary>
    public class UserWarehouseService
    {
        private readonly IUnitOfWork _unitOfWork;

        public UserWarehouseService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<UserWarehousesDto> GetForUserAsync(Guid userId)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId)
                ?? throw new NotFoundException($"Không tìm thấy tài khoản với ID: {userId}");
            return await BuildAsync(user);
        }

        public async Task<UserWarehousesDto> AssignAsync(Guid userId, AssignUserWarehousesRequest request, Guid actorId)
        {
            var userRepo = _unitOfWork.Repository<User>();
            var user = await userRepo.GetByIdAsync(userId)
                ?? throw new NotFoundException($"Không tìm thấy tài khoản với ID: {userId}");

            var wantedIds = (request?.WarehouseIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var note = string.IsNullOrWhiteSpace(request?.Note) ? null : request!.Note!.Trim();
            if (note != null && note.Length > 500)
                throw new ArgumentException("Ghi chú tối đa 500 ký tự.");

            var assignmentRepo = _unitOfWork.Repository<UserWarehouse>();
            var current = (await assignmentRepo.FindAsync(a => a.UserId == userId)).ToList();

            if (wantedIds.Count > 0)
            {
                if (!StaffRoleHelper.IsWarehouseStaffRole(user.Role))
                {
                    throw new ArgumentException(
                        $"Chỉ gán kho cho tài khoản nhân viên kho. Tài khoản {user.FullName} đang có vai trò {user.Role}.");
                }

                var warehouses = (await _unitOfWork.Repository<Warehouse>().FindAsync(w => wantedIds.Contains(w.Id))).ToList();
                var missing = wantedIds.Where(id => warehouses.All(w => w.Id != id)).ToList();
                if (missing.Count > 0)
                    throw new NotFoundException($"Không tìm thấy kho với ID: {string.Join(", ", missing)}");

                // Kho đã gán từ trước mà nay ngừng hoạt động vẫn được giữ; chỉ chặn gán MỚI vào kho ngừng hoạt động.
                var currentIds = current.Select(a => a.WarehouseId).ToHashSet();
                var inactive = warehouses.Where(w => !w.IsActive && !currentIds.Contains(w.Id)).ToList();
                if (inactive.Count > 0)
                {
                    throw new ArgumentException(
                        $"Kho {string.Join(", ", inactive.Select(w => w.WarehouseName))} đang ngừng hoạt động, không gán nhân viên được.");
                }

                var noRegion = warehouses.Where(w => !w.Region.HasValue).ToList();
                if (noRegion.Count > 0)
                {
                    throw new ArgumentException(
                        $"Kho {string.Join(", ", noRegion.Select(w => w.WarehouseName))} chưa khai vùng. Cập nhật vùng cho kho trước khi gán nhân viên.");
                }

                var first = warehouses[0];
                var otherRegion = warehouses.Where(w => !WarehouseAccessPolicy.SameRegion(w.Region, first.Region)).ToList();
                if (otherRegion.Count > 0)
                {
                    throw new ArgumentException(
                        "Các kho gán cho một nhân viên phải cùng một vùng: " +
                        string.Join(", ", warehouses.Select(w => $"{w.WarehouseName} ({w.Region})")) + ".");
                }

                if (user.Region.HasValue && !WarehouseAccessPolicy.SameRegion(user.Region, first.Region))
                {
                    throw new ArgumentException(
                        $"Tài khoản {user.FullName} đang phụ trách vùng {user.Region}, không gán được vào kho vùng {first.Region}. " +
                        "Admin đổi vùng của tài khoản trước (Đổi vai trò/vùng) rồi gán lại.");
                }
            }

            var result = new UserWarehousesDto();
            var previousRegion = user.Region?.ToString();

            if (wantedIds.Count > 0 && !user.Region.HasValue)
            {
                var regionWarehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(wantedIds[0]);
                user.Region = regionWarehouse!.Region;
                userRepo.Update(user);
                result.RegionChanged = true;
            }

            var now = DateTime.UtcNow;
            foreach (var row in current.Where(a => !wantedIds.Contains(a.WarehouseId)))
                assignmentRepo.Delete(row);

            foreach (var row in current.Where(a => wantedIds.Contains(a.WarehouseId)))
            {
                // Kho giữ nguyên: chỉ cập nhật ghi chú khi Admin ghi chú mới, giữ người gán/lúc gán gốc.
                if (note != null && !string.Equals(row.Note, note, StringComparison.Ordinal))
                {
                    row.Note = note;
                    assignmentRepo.Update(row);
                }
            }

            var currentWarehouseIds = current.Select(a => a.WarehouseId).ToHashSet();
            foreach (var warehouseId in wantedIds.Where(id => !currentWarehouseIds.Contains(id)))
            {
                await assignmentRepo.AddAsync(new UserWarehouse
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    WarehouseId = warehouseId,
                    AssignedById = actorId,
                    AssignedAt = now,
                    Note = note
                });
            }

            await _unitOfWork.SaveChangesAsync();

            var dto = await BuildAsync(user);
            dto.RegionChanged = result.RegionChanged;
            dto.PreviousRegion = previousRegion;
            return dto;
        }

        public async Task<WarehouseStaffDto> GetStaffOfWarehouseAsync(Guid warehouseId)
        {
            var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId)
                ?? throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");

            var rows = (await _unitOfWork.Repository<UserWarehouse>().FindAsync(a => a.WarehouseId == warehouseId)).ToList();
            var userIds = rows.Select(r => r.UserId)
                .Concat(rows.Where(r => r.AssignedById.HasValue).Select(r => r.AssignedById!.Value))
                .Distinct()
                .ToList();
            var users = (await _unitOfWork.Repository<User>().FindAsync(u => userIds.Contains(u.Id))).ToDictionary(u => u.Id);

            return new WarehouseStaffDto
            {
                WarehouseId = warehouse.Id,
                WarehouseCode = warehouse.WarehouseCode ?? string.Empty,
                WarehouseName = warehouse.WarehouseName ?? string.Empty,
                Region = warehouse.Region?.ToString(),
                Staff = rows
                    .Where(r => users.ContainsKey(r.UserId))
                    .Select(r =>
                    {
                        var u = users[r.UserId];
                        return new WarehouseStaffMemberDto
                        {
                            UserId = u.Id,
                            FullName = u.FullName ?? string.Empty,
                            Email = u.Email ?? string.Empty,
                            Role = u.Role ?? string.Empty,
                            Region = u.Region?.ToString(),
                            Status = u.Status ?? string.Empty,
                            AssignedAt = r.AssignedAt,
                            AssignedByName = r.AssignedById.HasValue && users.TryGetValue(r.AssignedById.Value, out var by)
                                ? by.FullName ?? string.Empty
                                : string.Empty,
                            Note = r.Note
                        };
                    })
                    .OrderBy(m => m.FullName)
                    .ToList()
            };
        }

        /// <summary>
        /// Phạm vi kho để lọc danh sách: null = không giới hạn theo kho (Admin, hoặc tài khoản chưa
        /// được gán — lọc theo vùng như cũ); ngược lại là tập kho được gán.
        /// </summary>
        public Task<IReadOnlyCollection<Guid>?> GetListScopeAsync(Guid? userId, string? role) =>
            WarehouseAccessPolicy.GetListScopeAsync(_unitOfWork, userId, role);

        /// <summary>Kiểm theo kho được gán (chỉ chặn khi tài khoản đã được gán kho).</summary>
        public Task EnsureAssignedWarehouseAsync(Guid? userId, string? role, Guid? warehouseId) =>
            WarehouseAccessPolicy.EnsureAssignedWarehouseAsync(_unitOfWork, userId, role, warehouseId);

        /// <summary>Kho của ô kệ.</summary>
        public async Task<Guid?> WarehouseOfBinAsync(Guid binId) =>
            (await _unitOfWork.Repository<Bin>().GetByIdAsync(binId))?.WarehouseId;

        /// <summary>Kho đang giữ kiện (theo ô kệ hiện tại). Null nếu kiện chưa lên kệ.</summary>
        public async Task<Guid?> WarehouseOfParcelAsync(Guid parcelId)
        {
            var parcel = await _unitOfWork.Repository<Parcel>().GetByIdAsync(parcelId);
            return parcel?.BinId.HasValue == true ? await WarehouseOfBinAsync(parcel.BinId.Value) : null;
        }

        /// <summary>Kho của một dòng tồn kho (theo ô kệ).</summary>
        public async Task<Guid?> WarehouseOfInventoryAsync(Guid inventoryId)
        {
            var inventory = await _unitOfWork.Repository<Inventory>().GetByIdAsync(inventoryId);
            return inventory == null ? null : await WarehouseOfBinAsync(inventory.BinId);
        }

        /// <summary>Kho của phiếu tiếp nhận.</summary>
        public async Task<Guid?> WarehouseOfReceivingNoteAsync(Guid receivingNoteId) =>
            (await _unitOfWork.Repository<WarehouseReceivingNote>().GetByIdAsync(receivingNoteId))?.WarehouseId;

        /// <summary>Kho đích của lô quốc tế.</summary>
        public async Task<Guid?> DestinationWarehouseOfShipmentAsync(Guid shipmentId) =>
            (await _unitOfWork.Repository<Shipment>().GetByIdAsync(shipmentId))?.DestinationWarehouseId;

        /// <summary>Kho được gán của mọi tài khoản — cho cột "Kho phụ trách" ở danh sách người dùng.</summary>
        public async Task<Dictionary<Guid, List<AssignedWarehouseBriefDto>>> GetAllAssignmentsAsync()
        {
            var rows = (await _unitOfWork.Repository<UserWarehouse>().GetAllAsync()).ToList();
            if (rows.Count == 0) return new Dictionary<Guid, List<AssignedWarehouseBriefDto>>();

            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id);
            return rows
                .Where(r => warehouses.ContainsKey(r.WarehouseId))
                .GroupBy(r => r.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => warehouses[r.WarehouseId])
                        .OrderBy(w => w.WarehouseName)
                        .Select(w => new AssignedWarehouseBriefDto
                        {
                            WarehouseId = w.Id,
                            WarehouseCode = w.WarehouseCode ?? string.Empty,
                            WarehouseName = w.WarehouseName ?? string.Empty,
                            Region = w.Region?.ToString()
                        })
                        .ToList());
        }

        private async Task<UserWarehousesDto> BuildAsync(User user)
        {
            var rows = (await _unitOfWork.Repository<UserWarehouse>().FindAsync(a => a.UserId == user.Id)).ToList();
            var warehouseIds = rows.Select(r => r.WarehouseId).ToList();
            var warehouses = (await _unitOfWork.Repository<Warehouse>().FindAsync(w => warehouseIds.Contains(w.Id))).ToDictionary(w => w.Id);
            var actorIds = rows.Where(r => r.AssignedById.HasValue).Select(r => r.AssignedById!.Value).Distinct().ToList();
            var actors = (await _unitOfWork.Repository<User>().FindAsync(u => actorIds.Contains(u.Id))).ToDictionary(u => u.Id);

            return new UserWarehousesDto
            {
                UserId = user.Id,
                FullName = user.FullName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Role = user.Role ?? string.Empty,
                Region = user.Region?.ToString(),
                IsWarehouseRole = StaffRoleHelper.IsWarehouseStaffRole(user.Role),
                Warehouses = rows
                    .Where(r => warehouses.ContainsKey(r.WarehouseId))
                    .Select(r =>
                    {
                        var w = warehouses[r.WarehouseId];
                        return new UserWarehouseItemDto
                        {
                            WarehouseId = w.Id,
                            WarehouseCode = w.WarehouseCode ?? string.Empty,
                            WarehouseName = w.WarehouseName ?? string.Empty,
                            Region = w.Region?.ToString(),
                            IsActive = w.IsActive,
                            Note = r.Note,
                            AssignedAt = r.AssignedAt,
                            AssignedById = r.AssignedById,
                            AssignedByName = r.AssignedById.HasValue && actors.TryGetValue(r.AssignedById.Value, out var by)
                                ? by.FullName ?? string.Empty
                                : string.Empty
                        };
                    })
                    .OrderBy(w => w.WarehouseName)
                    .ToList()
            };
        }
    }
}
