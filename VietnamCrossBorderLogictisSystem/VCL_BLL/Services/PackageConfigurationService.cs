using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class PackageConfigurationService : IPackageConfigurationService
    {
        private readonly IUnitOfWork _unitOfWork;

        public PackageConfigurationService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<PackageConfigurationResponseDto>> GetActiveConfigurationsAsync()
        {
            var configRepo = _unitOfWork.Repository<PackageConfiguration>();
            var configs = await configRepo.GetAllAsync();

            return configs
                .Where(c => string.Equals(c.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .Select(MapToDto);
        }

        public async Task<PackageConfigurationResponseDto?> SuggestConfigurationAsync(decimal length, decimal width, decimal height, decimal weight)
        {
            var configRepo = _unitOfWork.Repository<PackageConfiguration>();
            var allConfigs = await configRepo.GetAllAsync();
            var activeConfigs = allConfigs
                .Where(c => string.Equals(c.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sortedDims = new[] { length, width, height }.OrderBy(d => d).ToList();

            var fittingConfig = activeConfigs
                .Where(c => !string.Equals(c.ConfigCode, "CUSTOM", StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Length * c.Width * c.Height)
                .FirstOrDefault(c =>
                {
                    var boxDims = new[] { c.Length, c.Width, c.Height }.OrderBy(d => d).ToList();
                    return sortedDims[0] <= boxDims[0] &&
                           sortedDims[1] <= boxDims[1] &&
                           sortedDims[2] <= boxDims[2] &&
                           weight <= c.MaxWeight;
                });

            if (fittingConfig != null)
            {
                return MapToDto(fittingConfig);
            }

            var customConfig = activeConfigs.FirstOrDefault(c => string.Equals(c.ConfigCode, "CUSTOM", StringComparison.OrdinalIgnoreCase));
            if (customConfig != null)
            {
                return MapToDto(customConfig);
            }

            return null;
        }

        public async Task<PackageConfigurationResponseDto> GetByIdAsync(Guid id)
        {
            var configRepo = _unitOfWork.Repository<PackageConfiguration>();
            var config = await configRepo.GetByIdAsync(id);

            if (config == null)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình đóng gói với ID: {id}");
            }

            return MapToDto(config);
        }

        private static PackageConfigurationResponseDto MapToDto(PackageConfiguration config)
        {
            return new PackageConfigurationResponseDto
            {
                Id = config.Id,
                ConfigCode = config.ConfigCode,
                ConfigName = config.ConfigName,
                Length = config.Length,
                Width = config.Width,
                Height = config.Height,
                MaxWeight = config.MaxWeight,
                PackageFee = config.PackageFee,
                Status = config.Status
            };
        }
    }
}
