using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IConsolidationService
    {
        Task<ConsolidationResponseDto> CreateConsolidationAsync(CreateConsolidationRequest request);
        Task<ConsolidationResponseDto> GetConsolidationByIdAsync(Guid id);
        Task<IEnumerable<ConsolidationResponseDto>> GetConsolidationsAsync(string? status, string? search);
        Task<ConsolidationResponseDto> UpdateConsolidationAsync(Guid id, UpdateConsolidationRequest request);
        Task<bool> DeleteConsolidationAsync(Guid id);
    }
}
