using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IShippingMethodService
    {
        Task<List<ShippingMethodResponseDto>> GetAllAsync(bool activeOnly);
        Task<ShippingMethodResponseDto> GetByIdAsync(Guid id);
        Task<ShippingMethodResponseDto> CreateAsync(CreateShippingMethodRequestDto request);
        Task UpdateAsync(Guid id, UpdateShippingMethodRequestDto request);
        Task DeleteAsync(Guid id);
    }
}
