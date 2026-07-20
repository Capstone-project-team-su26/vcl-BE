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

        /// <summary>Admin tạo cấu hình thùng mới.</summary>
        Task<PackageConfigurationResponseDto> CreateAsync(SavePackageConfigurationRequest request);

        /// <summary>Admin cập nhật cấu hình thùng (kích thước, phí, trạng thái...).</summary>
        Task<PackageConfigurationResponseDto> UpdateAsync(Guid id, SavePackageConfigurationRequest request);

        /// <summary>Admin ngưng sử dụng thùng (soft-delete: Status → INACTIVE, giữ lịch sử đơn cũ).</summary>
        Task DeactivateAsync(Guid id);

        /// <summary>
        /// Tính phí đóng gói cho thùng CUSTOM theo thể tích khách nhập:
        /// phí = PackageFee (đơn giá / 1.000 cm³) × ceil(dài × rộng × cao / 1000).
        /// </summary>
        static decimal ComputeCustomBoxFee(decimal unitFeePerLiter, decimal length, decimal width, decimal height)
        {
            var volumeCm3 = length * width * height;
            if (volumeCm3 <= 0m || unitFeePerLiter <= 0m) return 0m;
            var liters = Math.Ceiling(volumeCm3 / 1000m);
            return liters * unitFeePerLiter;
        }
    }
}
