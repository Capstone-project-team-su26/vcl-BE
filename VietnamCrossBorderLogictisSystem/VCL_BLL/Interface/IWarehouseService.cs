using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IWarehouseService
    {
        Task<IEnumerable<WarehouseResponseDto>> GetWarehousesAsync(bool? isActive, string? search);
        Task<WarehouseResponseDto> CreateWarehouseAsync(CreateWarehouseRequestDto request);
        Task<WarehouseResponseDto> UpdateWarehouseAsync(Guid id, CreateWarehouseRequestDto request);
        Task DeleteWarehouseAsync(Guid id);
    }
}
