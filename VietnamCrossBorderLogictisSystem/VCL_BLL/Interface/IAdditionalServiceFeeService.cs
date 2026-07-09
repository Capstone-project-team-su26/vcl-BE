using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IAdditionalServiceFeeService
    {
        Task<List<AdditionalServiceFeeResponseDto>> GetAllAsync(bool activeOnly);
        Task<AdditionalServiceFeeResponseDto> GetByIdAsync(Guid id);
        Task<AdditionalServiceFeeResponseDto> CreateAsync(CreateAdditionalServiceFeeRequestDto request);
        Task UpdateAsync(Guid id, UpdateAdditionalServiceFeeRequestDto request);
        Task DeleteAsync(Guid id);
    }
}
