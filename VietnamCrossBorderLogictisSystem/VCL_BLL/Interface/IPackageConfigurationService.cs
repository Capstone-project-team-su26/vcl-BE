using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPackageConfigurationService
    {
        Task<IEnumerable<PackageConfigurationResponseDto>> GetActiveConfigurationsAsync();
        Task<PackageConfigurationResponseDto?> SuggestConfigurationAsync(decimal length, decimal width, decimal height, decimal weight);
        Task<PackageConfigurationResponseDto> GetByIdAsync(Guid id);
    }
}
