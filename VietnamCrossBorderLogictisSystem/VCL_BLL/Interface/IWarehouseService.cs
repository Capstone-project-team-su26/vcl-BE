using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IWarehouseService
    {
        Task<IEnumerable<WarehouseResponseDto>> GetWarehousesAsync(bool? isActive, string? search, string? region = null);
        Task<WarehouseResponseDto> CreateWarehouseAsync(CreateWarehouseRequestDto request);
        Task<WarehouseResponseDto> UpdateWarehouseAsync(Guid id, CreateWarehouseRequestDto request);
        Task DeleteWarehouseAsync(Guid id);
        Task<IEnumerable<WarehouseResponseDto>> GetActiveWarehousesAsync(string? region = null);
        Task<IEnumerable<WarehouseLocationResponseDto>> GetWarehouseLocationsAsync(Guid warehouseId, bool? isActive);
        Task<WarehouseLocationResponseDto> CreateWarehouseLocationAsync(Guid warehouseId, CreateLocationRequestDto request);
        Task<WarehouseLocationResponseDto> UpdateWarehouseLocationAsync(Guid locationId, CreateLocationRequestDto request);
        Task DeleteWarehouseLocationAsync(Guid locationId);
    }
}
