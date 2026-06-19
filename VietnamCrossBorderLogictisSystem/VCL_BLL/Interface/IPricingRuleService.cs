using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPricingRuleService
    {
        Task<IEnumerable<PricingRuleDto>> GetAllAsync();
        Task<PricingRuleDto> GetByIdAsync(Guid id);
        Task<PricingRuleDto> CreateAsync(CreatePricingRuleRequest request);
        Task UpdateAsync(Guid id, UpdatePricingRuleRequest request);
        Task DeleteAsync(Guid id);
    }
}
