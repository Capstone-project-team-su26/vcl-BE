using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IExchangeRateService
    {
        /// <param name="activeOnly">true (mặc định cho dropdown) chỉ trả tỷ giá đang bật.</param>
        Task<IEnumerable<ExchangeRateDto>> GetAllAsync(bool activeOnly = false);

        Task<ExchangeRateDto> GetByIdAsync(Guid id);

        Task<ExchangeRateDto> CreateAsync(CreateExchangeRateRequest request);

        Task<ExchangeRateDto> UpdateAsync(Guid id, UpdateExchangeRateRequest request);

        Task DeleteAsync(Guid id);

        /// <summary>
        /// Lấy tỷ giá đang hiệu lực của một đồng tiền. Trả 1 cho VND.
        /// Ném <see cref="Exceptions.NotFoundException"/> nếu chưa cấu hình hoặc đang tắt.
        /// </summary>
        Task<decimal> GetRateToVndAsync(string currencyCode);

        /// <summary>Quy đổi một số tiền sang VND theo tỷ giá đang hiệu lực.</summary>
        Task<CurrencyConversionDto> ConvertToVndAsync(string currencyCode, decimal amount);
    }
}
