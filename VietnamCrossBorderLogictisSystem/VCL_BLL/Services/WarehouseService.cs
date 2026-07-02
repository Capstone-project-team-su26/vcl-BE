using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class WarehouseService : IWarehouseService
    {
        private readonly IUnitOfWork _unitOfWork;

        public WarehouseService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<WarehouseResponseDto>> GetWarehousesAsync(bool? isActive, string? search)
        {
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouses = await warehouseRepo.GetAllAsync();

            var query = warehouses.AsQueryable();

            if (isActive.HasValue)
            {
                query = query.Where(w => w.IsActive == isActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(w => 
                    (w.WarehouseName != null && w.WarehouseName.ToLower().Contains(searchLower)) ||
                    (w.WarehouseCode != null && w.WarehouseCode.ToLower().Contains(searchLower))
                );
            }

            return query.Select(w => new WarehouseResponseDto
            {
                Id = w.Id,
                Name = w.WarehouseName ?? string.Empty,
                Code = w.WarehouseCode ?? string.Empty,
                Address = w.Address ?? string.Empty,
                WarehouseType = w.WarehouseType ?? "DOMESTIC",
                IsActive = w.IsActive
            }).ToList();
        }

        public async Task<WarehouseResponseDto> CreateWarehouseAsync(CreateWarehouseRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Tên kho là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.Code))
                throw new ArgumentException("Mã kho là bắt buộc.");

            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var allWarehouses = await warehouseRepo.GetAllAsync();

            if (allWarehouses.Any(w => string.Equals(w.WarehouseCode, request.Code, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Mã kho đã tồn tại trên hệ thống.");

            var newWarehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                WarehouseName = request.Name,
                WarehouseCode = request.Code,
                Address = request.Address,
                WarehouseType = request.WarehouseType ?? "DOMESTIC",
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await warehouseRepo.AddAsync(newWarehouse);
            await _unitOfWork.SaveChangesAsync();

            return new WarehouseResponseDto
            {
                Id = newWarehouse.Id,
                Name = newWarehouse.WarehouseName,
                Code = newWarehouse.WarehouseCode,
                Address = newWarehouse.Address,
                WarehouseType = newWarehouse.WarehouseType,
                IsActive = newWarehouse.IsActive
            };
        }

        public async Task<WarehouseResponseDto> UpdateWarehouseAsync(Guid id, CreateWarehouseRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ArgumentException("Tên kho là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.Code))
                throw new ArgumentException("Mã kho là bắt buộc.");

            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(id);

            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho với ID: {id}");

            var allWarehouses = await warehouseRepo.GetAllAsync();

            if (allWarehouses.Any(w => w.Id != id && string.Equals(w.WarehouseCode, request.Code, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Mã kho đã tồn tại trên hệ thống.");

            // Nghiệp vụ: Chặn vô hiệu hóa kho đang có nghiệp vụ xử lý
            if (!request.IsActive && warehouse.IsActive)
            {
                var binRepo = _unitOfWork.Repository<Bin>();
                var allBins = await binRepo.GetAllAsync();
                var warehouseBinIds = allBins.Where(b => b.WarehouseId == id).Select(b => b.Id).ToList();

                var inventoryRepo = _unitOfWork.Repository<Inventory>();
                var allInventories = await inventoryRepo.GetAllAsync();
                var hasInventory = allInventories.Any(inv => warehouseBinIds.Contains(inv.BinId) && inv.Quantity > 0);
                if (hasInventory)
                {
                    throw new ArgumentException("Không thể vô hiệu hóa kho đang có hàng hóa lưu trữ khả dụng.");
                }

                var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
                var allWroRequests = await wroRequestRepo.GetAllAsync();
                var activeWros = allWroRequests.Where(w => !string.Equals(w.Status, "RELEASED", StringComparison.OrdinalIgnoreCase) 
                                                       && !string.Equals(w.Status, "REJECTED", StringComparison.OrdinalIgnoreCase));

                var wroItemRepo = _unitOfWork.Repository<WroItem>();
                var allWroItems = await wroItemRepo.GetAllAsync();
                var activeWroRequestIds = activeWros.Select(w => w.Id).ToHashSet();
                var hasActiveWroInWarehouse = allWroItems.Any(wi => activeWroRequestIds.Contains(wi.WroRequestId) 
                                                                    && allInventories.Any(inv => inv.Id == wi.InventoryId 
                                                                                                && warehouseBinIds.Contains(inv.BinId)));
                if (hasActiveWroInWarehouse)
                {
                    throw new ArgumentException("Không thể vô hiệu hóa kho đang được sử dụng trong các yêu cầu xuất kho đang xử lý.");
                }

                var wrnRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
                var allWrns = await wrnRepo.GetAllAsync();
                var hasActiveWrn = allWrns.Any(n => n.WarehouseId == id && !string.Equals(n.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase));
                if (hasActiveWrn)
                {
                    throw new ArgumentException("Không thể vô hiệu hóa kho đang có phiếu tiếp nhận hàng chưa hoàn tất.");
                }
            }

            warehouse.WarehouseName = request.Name;
            warehouse.WarehouseCode = request.Code;
            warehouse.Address = request.Address;
            warehouse.WarehouseType = request.WarehouseType ?? "DOMESTIC";
            warehouse.IsActive = request.IsActive;

            warehouseRepo.Update(warehouse);
            await _unitOfWork.SaveChangesAsync();

            return new WarehouseResponseDto
            {
                Id = warehouse.Id,
                Name = warehouse.WarehouseName,
                Code = warehouse.WarehouseCode,
                Address = warehouse.Address,
                WarehouseType = warehouse.WarehouseType,
                IsActive = warehouse.IsActive
            };
        }

        public async Task TaskDeleteWarehouseAsync(Guid id)
        {
            await DeleteWarehouseAsync(id);
        }

        public async Task DeleteWarehouseAsync(Guid id)
        {
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(id);

            if (warehouse == null)
                throw new NotFoundException($"Không tìm thấy kho với ID: {id}");

            // Rule: check if there are active parcels in bins of this warehouse
            var binRepo = _unitOfWork.Repository<Bin>();
            var allBins = await binRepo.GetAllAsync();
            var warehouseBinIds = allBins.Where(b => b.WarehouseId == id).Select(b => b.Id).ToList();

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var hasParcels = allParcels.Any(p => p.BinId.HasValue && warehouseBinIds.Contains(p.BinId.Value));

            if (hasParcels)
                throw new ArgumentException("Không thể xóa kho đã tiếp nhận và lưu trữ kiện hàng.");

            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var hasInventory = allInventories.Any(inv => warehouseBinIds.Contains(inv.BinId));
            if (hasInventory)
                throw new ArgumentException("Không thể xóa kho đang chứa lịch sử tồn kho.");

            warehouseRepo.Delete(warehouse);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<IEnumerable<WarehouseResponseDto>> GetActiveWarehousesAsync()
        {
            return await GetWarehousesAsync(true, null);
        }

        public async Task<IEnumerable<WarehouseLocationResponseDto>> GetWarehouseLocationsAsync(Guid warehouseId, bool? isActive)
        {
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(warehouseId);
            if (warehouse == null)
            {
                throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");
            }

            var binRepo = _unitOfWork.Repository<Bin>();
            var shelfRepo = _unitOfWork.Repository<Shelf>();
            var zoneRepo = _unitOfWork.Repository<WarehouseZone>();

            var allBins = await binRepo.GetAllAsync();
            var allShelves = await shelfRepo.GetAllAsync();
            var allZones = await zoneRepo.GetAllAsync();

            var query = allBins.Where(b => b.WarehouseId == warehouseId).AsQueryable();

            if (isActive.HasValue)
            {
                query = query.Where(b => b.IsActive == isActive.Value);
            }

            var result = new List<WarehouseLocationResponseDto>();
            foreach (var bin in query)
            {
                var shelf = allShelves.FirstOrDefault(s => s.Id == bin.ShelfId);
                var zone = shelf != null ? allZones.FirstOrDefault(z => z.Id == shelf.ZoneId) : null;

                result.Add(new WarehouseLocationResponseDto
                {
                    LocationId = bin.Id,
                    BinCode = bin.BinCode ?? string.Empty,
                    ShelfId = bin.ShelfId ?? Guid.Empty,
                    ShelfCode = shelf?.ShelfCode ?? string.Empty,
                    ZoneId = shelf?.ZoneId ?? Guid.Empty,
                    ZoneName = zone?.ZoneName ?? string.Empty,
                    WarehouseId = warehouseId,
                    WarehouseName = warehouse.WarehouseName ?? string.Empty,
                    MaxVolume = bin.MaxVolume,
                    MaxWeight = bin.MaxWeight,
                    IsActive = bin.IsActive,
                    Note = bin.Note ?? string.Empty
                });
            }

            return result;
        }

        public async Task<WarehouseLocationResponseDto> CreateWarehouseLocationAsync(Guid warehouseId, CreateLocationRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.ZoneName))
                throw new ArgumentException("Tên Zone là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.ShelfCode))
                throw new ArgumentException("Mã Shelf là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.BinCode))
                throw new ArgumentException("Mã Bin là bắt buộc.");

            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(warehouseId);
            if (warehouse == null)
            {
                throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");
            }

            // Kiểm tra trùng mã Bin trong cùng một kho
            var binRepo = _unitOfWork.Repository<Bin>();
            var allBins = await binRepo.GetAllAsync();
            var isDuplicate = allBins.Any(b => b.WarehouseId == warehouseId && string.Equals(b.BinCode, request.BinCode, StringComparison.OrdinalIgnoreCase));
            if (isDuplicate)
            {
                throw new ArgumentException($"Vị trí lưu trữ với mã '{request.BinCode}' đã tồn tại trong kho này.");
            }

            // Tìm hoặc tạo Zone
            var zoneRepo = _unitOfWork.Repository<WarehouseZone>();
            var allZones = await zoneRepo.GetAllAsync();
            var zone = allZones.FirstOrDefault(z => z.WarehouseId == warehouseId && string.Equals(z.ZoneName, request.ZoneName, StringComparison.OrdinalIgnoreCase));
            if (zone == null)
            {
                zone = new WarehouseZone
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = warehouseId,
                    ZoneName = request.ZoneName
                };
                await zoneRepo.AddAsync(zone);
            }

            // Tìm hoặc tạo Shelf
            var shelfRepo = _unitOfWork.Repository<Shelf>();
            var allShelves = await shelfRepo.GetAllAsync();
            var shelf = allShelves.FirstOrDefault(s => s.ZoneId == zone.Id && string.Equals(s.ShelfCode, request.ShelfCode, StringComparison.OrdinalIgnoreCase));
            if (shelf == null)
            {
                shelf = new Shelf
                {
                    Id = Guid.NewGuid(),
                    ZoneId = zone.Id,
                    ShelfCode = request.ShelfCode
                };
                await shelfRepo.AddAsync(shelf);
            }

            // Tạo Bin/Location
            var newBin = new Bin
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseId,
                ShelfId = shelf.Id,
                BinCode = request.BinCode,
                MaxVolume = request.MaxVolume,
                MaxWeight = request.MaxWeight,
                IsActive = request.IsActive,
                Note = request.Note
            };

            await binRepo.AddAsync(newBin);
            await _unitOfWork.SaveChangesAsync();

            return new WarehouseLocationResponseDto
            {
                LocationId = newBin.Id,
                BinCode = newBin.BinCode,
                ShelfId = newBin.ShelfId.Value,
                ShelfCode = shelf.ShelfCode,
                ZoneId = zone.Id,
                ZoneName = zone.ZoneName,
                WarehouseId = warehouseId,
                WarehouseName = warehouse.WarehouseName,
                MaxVolume = newBin.MaxVolume,
                MaxWeight = newBin.MaxWeight,
                IsActive = newBin.IsActive,
                Note = newBin.Note ?? string.Empty
            };
        }

        public async Task<WarehouseLocationResponseDto> UpdateWarehouseLocationAsync(Guid locationId, CreateLocationRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.ZoneName))
                throw new ArgumentException("Tên Zone là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.ShelfCode))
                throw new ArgumentException("Mã Shelf là bắt buộc.");
            if (string.IsNullOrWhiteSpace(request.BinCode))
                throw new ArgumentException("Mã Bin là bắt buộc.");

            var binRepo = _unitOfWork.Repository<Bin>();
            var bin = await binRepo.GetByIdAsync(locationId);
            if (bin == null)
            {
                throw new NotFoundException($"Không tìm thấy vị trí kho với ID: {locationId}");
            }

            var warehouseId = bin.WarehouseId ?? Guid.Empty;
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(warehouseId);

            // Kiểm tra trùng mã Bin trong cùng một kho (trừ chính nó)
            var allBins = await binRepo.GetAllAsync();
            var isDuplicate = allBins.Any(b => b.Id != locationId && b.WarehouseId == warehouseId && string.Equals(b.BinCode, request.BinCode, StringComparison.OrdinalIgnoreCase));
            if (isDuplicate)
            {
                throw new ArgumentException($"Vị trí lưu trữ với mã '{request.BinCode}' đã tồn tại trong kho này.");
            }

            // Kiểm tra nếu chuyển IsActive = false: kiểm tra xem vị trí có đang được sử dụng không
            if (!request.IsActive && bin.IsActive)
            {
                var inventoryRepo = _unitOfWork.Repository<Inventory>();
                var allInventories = await inventoryRepo.GetAllAsync();
                var hasInventory = allInventories.Any(i => i.BinId == locationId && i.Quantity > 0);
                if (hasInventory)
                {
                    throw new ArgumentException("Không thể vô hiệu hóa vị trí kho đang chứa hàng hóa khả dụng.");
                }

                var parcelRepo = _unitOfWork.Repository<Parcel>();
                var allParcels = await parcelRepo.GetAllAsync();
                var hasParcels = allParcels.Any(p => p.BinId == locationId);
                if (hasParcels)
                {
                    throw new ArgumentException("Không thể vô hiệu hóa vị trí kho đang liên kết với kiện hàng.");
                }
            }

            // Tìm hoặc tạo Zone
            var zoneRepo = _unitOfWork.Repository<WarehouseZone>();
            var allZones = await zoneRepo.GetAllAsync();
            var zone = allZones.FirstOrDefault(z => z.WarehouseId == warehouseId && string.Equals(z.ZoneName, request.ZoneName, StringComparison.OrdinalIgnoreCase));
            if (zone == null)
            {
                zone = new WarehouseZone
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = warehouseId,
                    ZoneName = request.ZoneName
                };
                await zoneRepo.AddAsync(zone);
            }

            // Tìm hoặc tạo Shelf
            var shelfRepo = _unitOfWork.Repository<Shelf>();
            var allShelves = await shelfRepo.GetAllAsync();
            var shelf = allShelves.FirstOrDefault(s => s.ZoneId == zone.Id && string.Equals(s.ShelfCode, request.ShelfCode, StringComparison.OrdinalIgnoreCase));
            if (shelf == null)
            {
                shelf = new Shelf
                {
                    Id = Guid.NewGuid(),
                    ZoneId = zone.Id,
                    ShelfCode = request.ShelfCode
                };
                await shelfRepo.AddAsync(shelf);
            }

            bin.ShelfId = shelf.Id;
            bin.BinCode = request.BinCode;
            bin.MaxVolume = request.MaxVolume;
            bin.MaxWeight = request.MaxWeight;
            bin.IsActive = request.IsActive;
            bin.Note = request.Note;

            binRepo.Update(bin);
            await _unitOfWork.SaveChangesAsync();

            return new WarehouseLocationResponseDto
            {
                LocationId = bin.Id,
                BinCode = bin.BinCode,
                ShelfId = bin.ShelfId.Value,
                ShelfCode = shelf.ShelfCode,
                ZoneId = zone.Id,
                ZoneName = zone.ZoneName,
                WarehouseId = warehouseId,
                WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                MaxVolume = bin.MaxVolume,
                MaxWeight = bin.MaxWeight,
                IsActive = bin.IsActive,
                Note = bin.Note ?? string.Empty
            };
        }

        public async Task DeleteWarehouseLocationAsync(Guid locationId)
        {
            var binRepo = _unitOfWork.Repository<Bin>();
            var bin = await binRepo.GetByIdAsync(locationId);
            if (bin == null)
            {
                throw new NotFoundException($"Không tìm thấy vị trí kho với ID: {locationId}");
            }

            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var hasInventory = allInventories.Any(i => i.BinId == locationId && i.Quantity > 0);
            if (hasInventory)
            {
                throw new ArgumentException("Không thể xóa vị trí kho đang chứa hàng hóa.");
            }

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var hasParcels = allParcels.Any(p => p.BinId == locationId);
            if (hasParcels)
            {
                throw new ArgumentException("Không thể xóa vị trí kho đang liên kết với kiện hàng.");
            }

            binRepo.Delete(bin);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
