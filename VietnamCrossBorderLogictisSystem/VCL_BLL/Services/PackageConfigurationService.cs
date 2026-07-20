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
                var fittingDto = MapToDto(fittingConfig);
                fittingDto.EstimatedFee = fittingConfig.PackageFee; // thùng tiêu chuẩn: phí cố định
                return fittingDto;
            }

            var customConfig = activeConfigs.FirstOrDefault(c => string.Equals(c.ConfigCode, "CUSTOM", StringComparison.OrdinalIgnoreCase));
            if (customConfig != null)
            {
                var dto = MapToDto(customConfig);
                // CUSTOM: phí tính theo thể tích khách nhập (PackageFee = đơn giá / 1.000 cm³)
                dto.EstimatedFee = IPackageConfigurationService.ComputeCustomBoxFee(
                    customConfig.PackageFee, length, width, height);
                return dto;
            }

            return null;
        }

        public async Task<PackageConfigurationResponseDto> CreateAsync(SavePackageConfigurationRequest request)
        {
            var configRepo = _unitOfWork.Repository<PackageConfiguration>();
            var allConfigs = await configRepo.GetAllAsync();

            var code = request.ConfigCode.Trim().ToUpperInvariant();
            if (allConfigs.Any(c => string.Equals(c.ConfigCode, code, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Mã thùng '{code}' đã tồn tại.");

            var config = new PackageConfiguration
            {
                Id = Guid.NewGuid(),
                ConfigCode = code,
                ConfigName = request.ConfigName.Trim(),
                Length = request.Length,
                Width = request.Width,
                Height = request.Height,
                MaxWeight = request.MaxWeight,
                PackageFee = request.PackageFee,
                Status = NormalizeStatus(request.Status)
            };

            await configRepo.AddAsync(config);
            await _unitOfWork.SaveChangesAsync();
            return MapToDto(config);
        }

        public async Task<PackageConfigurationResponseDto> UpdateAsync(Guid id, SavePackageConfigurationRequest request)
        {
            var configRepo = _unitOfWork.Repository<PackageConfiguration>();
            var config = await configRepo.GetByIdAsync(id);
            if (config == null)
                throw new NotFoundException($"Không tìm thấy cấu hình đóng gói với ID: {id}");

            var code = request.ConfigCode.Trim().ToUpperInvariant();
            var allConfigs = await configRepo.GetAllAsync();
            if (allConfigs.Any(c => c.Id != id && string.Equals(c.ConfigCode, code, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Mã thùng '{code}' đã được dùng cho cấu hình khác.");

            config.ConfigCode = code;
            config.ConfigName = request.ConfigName.Trim();
            config.Length = request.Length;
            config.Width = request.Width;
            config.Height = request.Height;
            config.MaxWeight = request.MaxWeight;
            config.PackageFee = request.PackageFee;
            config.Status = NormalizeStatus(request.Status);

            configRepo.Update(config);
            await _unitOfWork.SaveChangesAsync();
            return MapToDto(config);
        }

        public async Task DeactivateAsync(Guid id)
        {
            var configRepo = _unitOfWork.Repository<PackageConfiguration>();
            var config = await configRepo.GetByIdAsync(id);
            if (config == null)
                throw new NotFoundException($"Không tìm thấy cấu hình đóng gói với ID: {id}");

            // Soft-delete: đơn/kiện cũ vẫn FK tới thùng này nên không xóa cứng
            config.Status = "INACTIVE";
            configRepo.Update(config);
            await _unitOfWork.SaveChangesAsync();
        }

        private static string NormalizeStatus(string? status)
        {
            var s = status?.Trim().ToUpperInvariant();
            return s == "INACTIVE" ? "INACTIVE" : "ACTIVE";
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
