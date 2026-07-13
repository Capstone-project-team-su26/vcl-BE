using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface;

public interface IWarehouseLocationLayoutService
{
    Task<List<WarehouseLocationLayoutDto>> GetLayoutByWarehouseIdAsync(Guid warehouseId, string userRole);
    Task<WarehouseLocationLayoutDto> CreateLayoutAsync(Guid warehouseId, string userRole, CreateWarehouseLocationLayoutDto dto);
    Task<WarehouseLocationLayoutDto> UpdateLayoutAsync(Guid warehouseId, Guid layoutId, string userRole, UpdateWarehouseLocationLayoutDto dto);
    Task<bool> DeleteLayoutAsync(Guid warehouseId, Guid layoutId, string userRole);
}
