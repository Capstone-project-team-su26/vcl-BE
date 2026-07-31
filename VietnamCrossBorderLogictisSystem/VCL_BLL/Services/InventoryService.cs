using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly IUnitOfWork _unitOfWork;

        public InventoryService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<InventoryResponseDto> RecordInventoryFromParcelAsync(Guid parcelId)
        {
            // 1. Tìm Parcel theo parcelId
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(parcelId);
            if (parcel == null)
            {
                throw new NotFoundException($"Không tìm thấy kiện hàng với ID: {parcelId}");
            }

            // 2. Không cho tạo tồn kho nếu Parcel chưa được put-away
            // Put-away thành công khi PackageStatus == "STORED" và có BinId
            if (!string.Equals(parcel.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase) || !parcel.BinId.HasValue)
            {
                throw new ArgumentException("Kiện hàng chưa được nhập vị trí kho (chưa put-away).");
            }

            // 3. Nếu kiện hàng đã được ghi nhận tồn kho trước đó -> trả về bản ghi hiện có (idempotent)
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var existingInventory = allInventories.FirstOrDefault(i => i.ParcelId == parcelId);
            if (existingInventory != null)
            {
                var existingDtos = await BuildInventoryDtosAsync(new List<Inventory> { existingInventory });
                return existingDtos.First();
            }

            // 4. Kiểm tra yêu cầu ký gửi liên kết
            if (!parcel.ConsignmentId.HasValue)
            {
                throw new ArgumentException("Kiện hàng không liên kết với yêu cầu ký gửi nào.");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(parcel.ConsignmentId.Value);
            if (order == null)
            {
                throw new NotFoundException("Không tìm thấy thông tin yêu cầu ký gửi liên kết với kiện hàng.");
            }

            if (!order.CustomerId.HasValue)
            {
                throw new ArgumentException("Yêu cầu ký gửi không liên kết với khách hàng nào.");
            }

            // 5. Tìm OrderItem và tổng số lượng hàng trong Parcel
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var consignmentItems = allItems.Where(i => i.OrderId == order.Id).ToList();

            Guid? orderItemId = consignmentItems.FirstOrDefault()?.Id;
            int quantity = consignmentItems.Sum(item => item.Quantity ?? 1);
            if (quantity <= 0)
            {
                quantity = 1;
            }

            // 6. Ghi nhận bản ghi tồn kho mới
            var inventory = new Inventory
            {
                Id = Guid.NewGuid(),
                CustomerId = order.CustomerId.Value,
                OrderId = order.Id,
                OrderItemId = orderItemId,
                ParcelId = parcel.Id,
                BinId = parcel.BinId.Value,
                Quantity = quantity,
                Status = "AVAILABLE",
                CreatedAt = DateTime.UtcNow
            };

            await inventoryRepo.AddAsync(inventory);
            await _unitOfWork.SaveChangesAsync();

            // 7. Trả kết quả kèm thông tin khách hàng, kiện hàng và vị trí lưu kho
            var createdDtos = await BuildInventoryDtosAsync(new List<Inventory> { inventory });
            return createdDtos.First();
        }

        public async Task<List<InventoryResponseDto>> GetInventoriesAsync(string? status, Guid? customerId, Guid? warehouseId)
        {
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();

            var query = allInventories.AsQueryable();

            // Loại bỏ các bản ghi zombie AVAILABLE có Quantity <= 0
            query = query.Where(i => !(string.Equals(i.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase) && i.Quantity <= 0));

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            if (customerId.HasValue)
            {
                query = query.Where(i => i.CustomerId == customerId.Value);
            }

            var dtos = await BuildInventoryDtosAsync(query.ToList());

            // Lọc theo kho sau khi đã xác định được kho từ ô kệ của từng bản ghi tồn kho
            if (warehouseId.HasValue)
            {
                dtos = dtos.Where(d => d.WarehouseId == warehouseId.Value).ToList();
            }

            return dtos;
        }

        public async Task<InventoryResponseDto> RelocateInventoryAsync(Guid inventoryId, RelocateInventoryRequestDto request, Guid? userId)
        {
            if (request == null || request.ToBinId == Guid.Empty)
            {
                throw new ArgumentException("Vui lòng chọn ô kệ đích để điều chuyển.");
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new ArgumentException("Vui lòng nhập lý do điều chuyển.");
            }

            // 1. Tìm bản ghi tồn kho
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var inventory = await inventoryRepo.GetByIdAsync(inventoryId);
            if (inventory == null)
            {
                throw new NotFoundException($"Không tìm thấy bản ghi tồn kho với ID: {inventoryId}");
            }

            // 2. Hàng đã lấy hoặc đã xuất kho thì không còn nằm ở vị trí cũ để điều chuyển
            var blockedStatuses = new[] { "PICKED", "RELEASED" };
            if (blockedStatuses.Any(s => string.Equals(s, inventory.Status, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Không thể điều chuyển hàng tồn ở trạng thái {inventory.Status}.");
            }

            if (inventory.BinId == request.ToBinId)
            {
                throw new ArgumentException("Ô kệ đích trùng với ô kệ hiện tại của kiện hàng.");
            }

            // 3. Tìm kiện hàng và ô kệ nguồn / đích
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(inventory.ParcelId);
            if (parcel == null)
            {
                throw new NotFoundException("Không tìm thấy kiện hàng liên kết với bản ghi tồn kho.");
            }

            var binRepo = _unitOfWork.Repository<Bin>();
            var fromBin = await binRepo.GetByIdAsync(inventory.BinId);
            var toBin = await binRepo.GetByIdAsync(request.ToBinId);
            if (toBin == null)
            {
                throw new NotFoundException("Không tìm thấy ô kệ đích.");
            }

            // 4. Ô kệ đích phải đang hoạt động và thuộc đúng kho đang lưu kiện hàng (BR-56)
            if (!toBin.IsActive)
            {
                throw new ArgumentException("Ô kệ đích đang ngừng sử dụng.");
            }

            if (fromBin?.WarehouseId != null && toBin.WarehouseId != fromBin.WarehouseId)
            {
                throw new ArgumentException("Chỉ được điều chuyển trong phạm vi cùng một kho.");
            }

            // 5. Kiểm tra sức chứa của ô kệ đích trước khi gán (BR-53)
            decimal parcelVolume = (parcel.Length ?? 0) * (parcel.Width ?? 0) * (parcel.Height ?? 0);
            if (toBin.MaxVolume.HasValue)
            {
                decimal availableVolume = toBin.MaxVolume.Value - (toBin.CurrentVolume ?? 0);
                if (parcelVolume > availableVolume)
                {
                    throw new ArgumentException($"Ô kệ đích không đủ sức chứa thể tích. Thể tích kiện hàng: {parcelVolume}, thể tích khả dụng: {availableVolume}.");
                }
            }

            if (toBin.MaxWeight.HasValue)
            {
                var allParcels = await parcelRepo.GetAllAsync();
                var currentWeightInBin = allParcels
                    .Where(p => p.BinId == toBin.Id && string.Equals(p.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.ActualWeight ?? 0);

                decimal availableWeight = toBin.MaxWeight.Value - currentWeightInBin;
                decimal parcelWeight = parcel.ActualWeight ?? 0;
                if (parcelWeight > availableWeight)
                {
                    throw new ArgumentException($"Ô kệ đích không đủ sức chứa tải trọng. Khối lượng kiện hàng: {parcelWeight}, tải trọng khả dụng: {availableWeight}.");
                }
            }

            // 6. Cập nhật vị trí của kiện hàng, bản ghi tồn kho và thể tích hai ô kệ trong cùng transaction (BR-57)
            var movedAt = DateTime.UtcNow;
            var fromBinId = inventory.BinId;

            parcel.BinId = toBin.Id;
            inventory.BinId = toBin.Id;

            if (fromBin != null)
            {
                var remainingVolume = (fromBin.CurrentVolume ?? 0) - parcelVolume;
                fromBin.CurrentVolume = remainingVolume < 0 ? 0 : remainingVolume;
                binRepo.Update(fromBin);
            }

            toBin.CurrentVolume = (toBin.CurrentVolume ?? 0) + parcelVolume;

            parcelRepo.Update(parcel);
            inventoryRepo.Update(inventory);
            binRepo.Update(toBin);

            // 7. Ghi nhật ký điều chuyển (BR-47)
            var movementRepo = _unitOfWork.Repository<InventoryMovement>();
            await movementRepo.AddAsync(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                InventoryId = inventory.Id,
                ParcelId = parcel.Id,
                FromBinId = fromBinId,
                ToBinId = toBin.Id,
                MovementType = "RELOCATION",
                Reason = request.Reason.Trim(),
                Quantity = inventory.Quantity,
                MovedById = userId,
                MovedAt = movedAt
            });

            await _unitOfWork.SaveChangesAsync();

            var dtos = await BuildInventoryDtosAsync(new List<Inventory> { inventory });
            return dtos.First();
        }

        public async Task<List<InventoryMovementResponseDto>> GetMovementsAsync(Guid? warehouseId, Guid? parcelId, int limit)
        {
            if (limit <= 0)
            {
                limit = 50;
            }

            var movementRepo = _unitOfWork.Repository<InventoryMovement>();
            var allMovements = await movementRepo.GetAllAsync();

            var movements = allMovements.AsQueryable();

            if (parcelId.HasValue)
            {
                movements = movements.Where(m => m.ParcelId == parcelId.Value);
            }

            var ordered = movements.OrderByDescending(m => m.MovedAt).ToList();

            // Nạp các bảng tra cứu một lần
            var binIds = ordered.Select(m => m.ToBinId)
                .Concat(ordered.Select(m => m.FromBinId).OfType<Guid>())
                .ToHashSet();

            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => binIds.Contains(b.Id))
                .ToDictionary(b => b.Id);

            var parcelIds = ordered.Select(m => m.ParcelId).ToHashSet();
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToDictionary(p => p.Id);

            var userIds = ordered.Select(m => m.MovedById).OfType<Guid>().ToHashSet();
            var users = userIds.Count == 0
                ? new Dictionary<Guid, User>()
                : (await _unitOfWork.Repository<User>().GetAllAsync())
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionary(u => u.Id);

            var result = new List<InventoryMovementResponseDto>();

            foreach (var movement in ordered)
            {
                bins.TryGetValue(movement.ToBinId, out var toBin);
                Bin? fromBin = null;
                if (movement.FromBinId is Guid fromBinId)
                {
                    bins.TryGetValue(fromBinId, out fromBin);
                }

                // Kho được xác định theo ô kệ đích của thao tác
                var movementWarehouseId = toBin?.WarehouseId;
                if (warehouseId.HasValue && movementWarehouseId != warehouseId.Value)
                {
                    continue;
                }

                parcels.TryGetValue(movement.ParcelId, out var parcel);
                User? movedBy = null;
                if (movement.MovedById is Guid movedById)
                {
                    users.TryGetValue(movedById, out movedBy);
                }

                result.Add(new InventoryMovementResponseDto
                {
                    Id = movement.Id,
                    InventoryId = movement.InventoryId,
                    ParcelId = movement.ParcelId,
                    PackageCode = parcel?.PackageCode ?? string.Empty,
                    FromBinId = movement.FromBinId,
                    FromBinCode = fromBin?.BinCode ?? string.Empty,
                    ToBinId = movement.ToBinId,
                    ToBinCode = toBin?.BinCode ?? string.Empty,
                    Type = movement.MovementType ?? string.Empty,
                    Reason = movement.Reason ?? string.Empty,
                    Quantity = movement.Quantity,
                    WarehouseId = movementWarehouseId,
                    StaffName = movedBy?.FullName ?? string.Empty,
                    MovedAt = movement.MovedAt
                });

                if (result.Count >= limit)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Các trạng thái mà hàng vẫn đang nằm vật lý trong kho.
        /// PICKED và RELEASED đã rời vị trí lưu nên không tính vào thời gian lưu kho.
        /// </summary>
        private static readonly string[] InStorageStatuses = { "AVAILABLE", "RESERVED" };

        public async Task<List<LongStayAlertResponseDto>> GetLongStayAlertsAsync(Guid? warehouseId, Guid? customerId, int minDays, int criticalDays, int limit)
        {
            if (minDays < 0)
            {
                minDays = 0;
            }

            if (criticalDays < minDays)
            {
                criticalDays = minDays;
            }

            if (limit <= 0)
            {
                limit = 100;
            }

            var inventories = await GetInventoriesAsync(null, customerId, warehouseId);

            return inventories
                .Where(i => InStorageStatuses.Contains(i.Status, StringComparer.OrdinalIgnoreCase))
                .Where(i => i.StorageDays >= minDays)
                .OrderByDescending(i => i.StorageHours)
                .Take(limit)
                .Select(i => new LongStayAlertResponseDto
                {
                    InventoryId = i.InventoryId,
                    CustomerId = i.CustomerId,
                    OrderId = i.OrderId,
                    OrderItemId = i.OrderItemId,
                    ParcelId = i.ParcelId,
                    BinId = i.BinId,
                    Quantity = i.Quantity,
                    Status = i.Status,
                    CreatedAt = i.CreatedAt,
                    CustomerCode = i.CustomerCode,
                    CustomerName = i.CustomerName,
                    CustomerPhone = i.CustomerPhone,
                    ConsignmentCode = i.ConsignmentCode,
                    PackageCode = i.PackageCode,
                    BinCode = i.BinCode,
                    WarehouseId = i.WarehouseId,
                    WarehouseName = i.WarehouseName,
                    StoredAt = i.StoredAt,
                    ActualWeight = i.ActualWeight,
                    ActualVolume = i.ActualVolume,
                    StorageDays = i.StorageDays,
                    StorageHours = i.StorageHours,
                    Severity = i.StorageDays >= criticalDays ? "CRITICAL" : "WARNING"
                })
                .ToList();
        }

        public async Task<List<CustomerStorageReportResponseDto>> GetCustomerStorageReportAsync(Guid? warehouseId, Guid? customerId)
        {
            var inventories = await GetInventoriesAsync(null, customerId, warehouseId);

            var inStorage = inventories
                .Where(i => InStorageStatuses.Contains(i.Status, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return inStorage
                .GroupBy(i => i.CustomerId)
                .Select(group =>
                {
                    var first = group.First();
                    var parcelCount = group.Count();

                    return new CustomerStorageReportResponseDto
                    {
                        CustomerId = group.Key,
                        CustomerCode = first.CustomerCode,
                        CustomerName = first.CustomerName,
                        CustomerPhone = first.CustomerPhone,
                        ParcelCount = parcelCount,
                        TotalQuantity = group.Sum(i => i.Quantity),
                        TotalWeight = group.Sum(i => i.ActualWeight),
                        TotalVolume = group.Sum(i => i.ActualVolume),
                        AverageAgeDays = parcelCount == 0 ? 0 : group.Sum(i => (double)i.StorageHours) / parcelCount / 24,
                        OldestAgeDays = group.Max(i => i.StorageHours) / 24.0,
                        BinCodes = group
                            .Select(i => i.BinCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Distinct()
                            .OrderBy(code => code)
                            .ToList()
                    };
                })
                .OrderByDescending(r => r.ParcelCount)
                .ToList();
        }

        public async Task<CycleCountResponseDto> CreateCycleCountAsync(CreateCycleCountRequestDto request, Guid? userId)
        {
            if (request == null || request.BinId == Guid.Empty)
            {
                throw new ArgumentException("Vui lòng chọn ô kệ cần kiểm kê.");
            }

            if (request.ActualCount < 0)
            {
                throw new ArgumentException("Số kiện thực tế không được nhỏ hơn 0.");
            }

            // 1. Ô kệ phải tồn tại và đang hoạt động
            var binRepo = _unitOfWork.Repository<Bin>();
            var bin = await binRepo.GetByIdAsync(request.BinId);
            if (bin == null)
            {
                throw new NotFoundException("Không tìm thấy ô kệ cần kiểm kê.");
            }

            if (!bin.IsActive)
            {
                throw new ArgumentException("Ô kệ đang ngừng sử dụng, không thể kiểm kê.");
            }

            // 2. Số kiện hệ thống ghi nhận do server tự đếm, không lấy theo số client gửi lên
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var expectedCount = allParcels.Count(p => p.BinId == bin.Id
                && string.Equals(p.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase));

            var difference = request.ActualCount - expectedCount;

            var cycleCount = new CycleCount
            {
                Id = Guid.NewGuid(),
                WarehouseId = bin.WarehouseId,
                BinId = bin.Id,
                ExpectedCount = expectedCount,
                ActualCount = request.ActualCount,
                Difference = difference,
                Status = difference == 0 ? "MATCHED" : "DISCREPANCY",
                Note = request.Note?.Trim() ?? string.Empty,
                CountedById = userId,
                CountedAt = DateTime.UtcNow,
                ResolutionNote = string.Empty
            };

            await _unitOfWork.Repository<CycleCount>().AddAsync(cycleCount);
            await _unitOfWork.SaveChangesAsync();

            var dtos = await BuildCycleCountDtosAsync(new List<CycleCount> { cycleCount });
            return dtos.First();
        }

        public async Task<List<CycleCountResponseDto>> GetCycleCountsAsync(Guid? warehouseId, Guid? binId, string? status, int limit)
        {
            if (limit <= 0)
            {
                limit = 50;
            }

            var allCounts = await _unitOfWork.Repository<CycleCount>().GetAllAsync();
            var query = allCounts.AsQueryable();

            if (warehouseId.HasValue)
            {
                query = query.Where(c => c.WarehouseId == warehouseId.Value);
            }

            if (binId.HasValue)
            {
                query = query.Where(c => c.BinId == binId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(c => string.Equals(c.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query.OrderByDescending(c => c.CountedAt).Take(limit).ToList();
            return await BuildCycleCountDtosAsync(ordered);
        }

        public async Task<CycleCountResponseDto> ResolveCycleCountAsync(Guid cycleCountId, ResolveCycleCountRequestDto request, Guid? userId)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ResolutionNote))
            {
                throw new ArgumentException("Vui lòng nhập cách xử lý chênh lệch.");
            }

            var cycleCountRepo = _unitOfWork.Repository<CycleCount>();
            var cycleCount = await cycleCountRepo.GetByIdAsync(cycleCountId);
            if (cycleCount == null)
            {
                throw new NotFoundException($"Không tìm thấy phiếu kiểm kê với ID: {cycleCountId}");
            }

            // Chỉ phiếu có chênh lệch mới cần xử lý
            if (!string.Equals(cycleCount.Status, "DISCREPANCY", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Chỉ xử lý được phiếu kiểm kê có chênh lệch. Trạng thái hiện tại: {cycleCount.Status}.");
            }

            cycleCount.Status = "RESOLVED";
            cycleCount.ResolutionNote = request.ResolutionNote.Trim();
            cycleCount.ResolvedById = userId;
            cycleCount.ResolvedAt = DateTime.UtcNow;

            cycleCountRepo.Update(cycleCount);
            await _unitOfWork.SaveChangesAsync();

            var dtos = await BuildCycleCountDtosAsync(new List<CycleCount> { cycleCount });
            return dtos.First();
        }

        /// <summary>
        /// Bổ sung mã ô kệ, tên kho và tên nhân viên cho danh sách phiếu kiểm kê.
        /// </summary>
        private async Task<List<CycleCountResponseDto>> BuildCycleCountDtosAsync(List<CycleCount> cycleCounts)
        {
            if (cycleCounts == null || cycleCounts.Count == 0)
            {
                return new List<CycleCountResponseDto>();
            }

            var binIds = cycleCounts.Select(c => c.BinId).ToHashSet();
            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => binIds.Contains(b.Id))
                .ToDictionary(b => b.Id);

            var warehouseIds = cycleCounts.Select(c => c.WarehouseId).OfType<Guid>().ToHashSet();
            var warehouses = warehouseIds.Count == 0
                ? new Dictionary<Guid, Warehouse>()
                : (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                    .Where(w => warehouseIds.Contains(w.Id))
                    .ToDictionary(w => w.Id);

            var userIds = cycleCounts.Select(c => c.CountedById)
                .Concat(cycleCounts.Select(c => c.ResolvedById))
                .OfType<Guid>()
                .ToHashSet();

            var users = userIds.Count == 0
                ? new Dictionary<Guid, User>()
                : (await _unitOfWork.Repository<User>().GetAllAsync())
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionary(u => u.Id);

            var result = new List<CycleCountResponseDto>();

            foreach (var cycleCount in cycleCounts)
            {
                bins.TryGetValue(cycleCount.BinId, out var bin);

                Warehouse? warehouse = null;
                if (cycleCount.WarehouseId is Guid cycleWarehouseId)
                {
                    warehouses.TryGetValue(cycleWarehouseId, out warehouse);
                }

                User? countedBy = null;
                if (cycleCount.CountedById is Guid countedById)
                {
                    users.TryGetValue(countedById, out countedBy);
                }

                User? resolvedBy = null;
                if (cycleCount.ResolvedById is Guid resolvedById)
                {
                    users.TryGetValue(resolvedById, out resolvedBy);
                }

                result.Add(new CycleCountResponseDto
                {
                    Id = cycleCount.Id,
                    WarehouseId = cycleCount.WarehouseId,
                    WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                    BinId = cycleCount.BinId,
                    BinCode = bin?.BinCode ?? string.Empty,
                    ExpectedCount = cycleCount.ExpectedCount,
                    ActualCount = cycleCount.ActualCount,
                    Difference = cycleCount.Difference,
                    Status = cycleCount.Status ?? string.Empty,
                    Note = cycleCount.Note ?? string.Empty,
                    StaffName = countedBy?.FullName ?? string.Empty,
                    CountedAt = cycleCount.CountedAt,
                    ResolvedByName = resolvedBy?.FullName ?? string.Empty,
                    ResolvedAt = cycleCount.ResolvedAt,
                    ResolutionNote = cycleCount.ResolutionNote ?? string.Empty
                });
            }

            return result;
        }

        /// <summary>
        /// Bổ sung thông tin khách hàng, yêu cầu ký gửi, kiện hàng và vị trí lưu kho cho danh sách tồn kho.
        /// Các bảng liên quan được nạp một lần rồi tra cứu theo Id để tránh truy vấn lặp trên từng dòng.
        /// </summary>
        private async Task<List<InventoryResponseDto>> BuildInventoryDtosAsync(List<Inventory> inventories)
        {
            if (inventories == null || inventories.Count == 0)
            {
                return new List<InventoryResponseDto>();
            }

            var customerIds = inventories.Select(i => i.CustomerId).ToHashSet();
            var orderIds = inventories.Select(i => i.OrderId).ToHashSet();
            var parcelIds = inventories.Select(i => i.ParcelId).ToHashSet();
            var binIds = inventories.Select(i => i.BinId).ToHashSet();

            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionary(c => c.Id);

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id))
                .ToDictionary(o => o.Id);

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToDictionary(p => p.Id);

            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => binIds.Contains(b.Id))
                .ToDictionary(b => b.Id);

            var warehouseIds = bins.Values
                .Select(b => b.WarehouseId)
                .OfType<Guid>()
                .ToHashSet();

            var warehouses = warehouseIds.Count == 0
                ? new Dictionary<Guid, Warehouse>()
                : (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                    .Where(w => warehouseIds.Contains(w.Id))
                    .ToDictionary(w => w.Id);

            var result = new List<InventoryResponseDto>();

            foreach (var inventory in inventories)
            {
                customers.TryGetValue(inventory.CustomerId, out var customer);
                orders.TryGetValue(inventory.OrderId, out var order);
                parcels.TryGetValue(inventory.ParcelId, out var parcel);
                bins.TryGetValue(inventory.BinId, out var bin);

                Warehouse? warehouse = null;
                if (bin?.WarehouseId is Guid binWarehouseId)
                {
                    warehouses.TryGetValue(binWarehouseId, out warehouse);
                }

                // Mốc bắt đầu lưu kho ưu tiên thời điểm put-away của kiện hàng
                var storedAt = parcel?.PutAwayTime ?? inventory.CreatedAt;

                // Thể tích thực tế tính theo cùng công thức với kiểm tra sức chứa ô kệ
                var actualVolume = (parcel?.Length ?? 0) * (parcel?.Width ?? 0) * (parcel?.Height ?? 0);

                // Thời gian đã lưu kho tính tới hiện tại (M07)
                var storageHours = (int)Math.Max(0, Math.Floor((DateTime.UtcNow - storedAt).TotalHours));

                result.Add(new InventoryResponseDto
                {
                    InventoryId = inventory.Id,
                    CustomerId = inventory.CustomerId,
                    OrderId = inventory.OrderId,
                    OrderItemId = inventory.OrderItemId,
                    ParcelId = inventory.ParcelId,
                    BinId = inventory.BinId,
                    Quantity = inventory.Quantity,
                    Status = inventory.Status ?? string.Empty,
                    CreatedAt = inventory.CreatedAt,

                    CustomerCode = customer?.CustomerCode ?? string.Empty,
                    CustomerName = customer?.FullName ?? string.Empty,
                    CustomerPhone = customer?.Phone ?? string.Empty,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    PackageCode = parcel?.PackageCode ?? string.Empty,
                    BinCode = bin?.BinCode ?? string.Empty,
                    WarehouseId = bin?.WarehouseId,
                    WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                    StoredAt = storedAt,
                    ActualWeight = parcel?.ActualWeight ?? 0,
                    ActualVolume = actualVolume,
                    StorageDays = storageHours / 24,
                    StorageHours = storageHours
                });
            }

            return result;
        }
    }
}
