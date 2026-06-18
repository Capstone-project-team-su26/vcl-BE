using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IRestrictedItemService
    {
        Task<IEnumerable<RestrictedItemDto>> GetAllAsync();
        Task<RestrictedItemDto> GetByIdAsync(Guid id);
        Task<RestrictedItemDto> CreateAsync(CreateRestrictedItemRequest request);
        Task UpdateAsync(Guid id, UpdateRestrictedItemRequest request);
        Task DeleteAsync(Guid id);
    }
}
