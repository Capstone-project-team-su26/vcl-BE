using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface ICarrierService
    {
        Task<IEnumerable<CarrierDto>> GetAllAsync();
        Task<IEnumerable<CarrierDto>> GetActiveAsync();
        Task<CarrierDto> GetByIdAsync(Guid id);
        Task<CarrierDto> CreateAsync(CreateCarrierRequestDto request);
        Task UpdateAsync(Guid id, UpdateCarrierRequestDto request);
        Task DeleteAsync(Guid id);
    }
}
