using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IShippingRouteService
    {
        Task<List<ShippingRouteDto>> GetAllAsync(string? transportMode, bool? isActive);
        Task<ShippingRouteDto> GetByIdAsync(Guid id);
        Task<ShippingRouteDto> CreateAsync(CreateShippingRouteRequest request);
        Task UpdateAsync(Guid id, UpdateShippingRouteRequest request);
        Task DeleteAsync(Guid id);
    }
}
