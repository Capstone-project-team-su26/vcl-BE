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

            warehouseRepo.Delete(warehouse);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
