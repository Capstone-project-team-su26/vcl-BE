using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPricingRuleService
    {
        /// <param name="orderType">
        /// Lọc theo loại đơn áp dụng: <c>CONSIGNMENT</c> (ký gửi) hoặc <c>PURCHASE</c> (mua hộ).
        /// Bỏ trống để lấy toàn bộ (màn quản trị bảng giá).
        /// </param>
        Task<IEnumerable<PricingRuleDto>> GetAllAsync(string? orderType = null);
        Task<PricingRuleDto> GetByIdAsync(Guid id);
        Task<PricingRuleDto> CreateAsync(CreatePricingRuleRequest request);
        Task UpdateAsync(Guid id, UpdatePricingRuleRequest request);
        Task DeleteAsync(Guid id);
    }
}
