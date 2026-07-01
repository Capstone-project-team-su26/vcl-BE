using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IServicePricingService
    {
        Task<IEnumerable<ServicePricingDto>> GetAllAsync();
        Task<ServicePricingDto> GetByIdAsync(Guid id);
        Task<ServicePricingDto> CreateAsync(CreateServicePricingRequest request);
        Task UpdateAsync(Guid id, UpdateServicePricingRequest request);
        Task DeleteAsync(Guid id);
    }
}
