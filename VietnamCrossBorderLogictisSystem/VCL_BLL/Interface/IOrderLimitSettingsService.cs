using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>Giới hạn tạo đơn ký gửi / mua hộ do Admin cấu hình.</summary>
    public interface IOrderLimitSettingsService
    {
        /// <param name="includeAudit">true → kèm người sửa / thời điểm sửa (chỉ Admin).</param>
        Task<OrderLimitSettingsResponseDto> GetAsync(bool includeAudit);

        Task<OrderLimitSettingsResponseDto> UpdateAsync(UpdateOrderLimitSettingsRequestDto request, Guid adminUserId);

        Task<List<OrderLimitHistoryItemDto>> GetHistoryAsync(int take);
    }
}
