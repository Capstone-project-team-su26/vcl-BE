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
    /// <summary>
    /// Tỷ giá do công ty tự quy định (đã cộng biên rủi ro và phí chuyển tiền), không lấy tỷ giá
    /// thị trường theo thời gian thực — số tiền báo cho khách phải ổn định và tra lại được.
    /// </summary>
    public class ExchangeRateService : IExchangeRateService
    {
        private const string VndCurrencyCode = "VND";

        private readonly IUnitOfWork _unitOfWork;

        public ExchangeRateService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<ExchangeRateDto>> GetAllAsync(bool activeOnly = false)
        {
            var rates = await _unitOfWork.Repository<ExchangeRate>().GetAllAsync();

            return rates
                .Where(r => !activeOnly || r.IsActive)
                .OrderBy(r => r.CurrencyCode)
                .Select(ToDto)
                .ToList();
        }

        public async Task<ExchangeRateDto> GetByIdAsync(Guid id)
        {
            var rate = await _unitOfWork.Repository<ExchangeRate>().GetByIdAsync(id)
                ?? throw new NotFoundException($"Không tìm thấy tỷ giá với ID {id}");

            return ToDto(rate);
        }

        public async Task<ExchangeRateDto> CreateAsync(CreateExchangeRateRequest request)
        {
            var code = NormalizeCode(request.CurrencyCode);
            if (code == VndCurrencyCode)
                throw new ArgumentException("Không cần cấu hình tỷ giá cho VND.");

            var repo = _unitOfWork.Repository<ExchangeRate>();
            var existing = await repo.GetAllAsync();
            if (existing.Any(r => NormalizeCode(r.CurrencyCode) == code))
                throw new ArgumentException($"Đã có cấu hình tỷ giá cho {code}. Vui lòng sửa bản ghi hiện có.");

            var entity = new ExchangeRate
            {
                Id = Guid.NewGuid(),
                CurrencyCode = code,
                CurrencyName = request.CurrencyName?.Trim(),
                RateToVnd = request.RateToVnd,
                IsActive = request.IsActive,
                Note = request.Note?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(entity);
            await _unitOfWork.SaveChangesAsync();

            return ToDto(entity);
        }

        public async Task<ExchangeRateDto> UpdateAsync(Guid id, UpdateExchangeRateRequest request)
        {
            var repo = _unitOfWork.Repository<ExchangeRate>();
            var entity = await repo.GetByIdAsync(id)
                ?? throw new NotFoundException($"Không tìm thấy tỷ giá với ID {id}");

            entity.CurrencyName = request.CurrencyName?.Trim() ?? entity.CurrencyName;
            entity.RateToVnd = request.RateToVnd;
            entity.IsActive = request.IsActive;
            entity.Note = request.Note?.Trim();
            entity.UpdatedAt = DateTime.UtcNow;

            repo.Update(entity);
            await _unitOfWork.SaveChangesAsync();

            return ToDto(entity);
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<ExchangeRate>();
            var entity = await repo.GetByIdAsync(id)
                ?? throw new NotFoundException($"Không tìm thấy tỷ giá với ID {id}");

            repo.Delete(entity);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<decimal> GetRateToVndAsync(string currencyCode)
        {
            var code = NormalizeCode(currencyCode);

            // VND không quy đổi — tỷ giá 1 để mọi phép nhân phía sau dùng chung một đường code.
            if (string.IsNullOrEmpty(code) || code == VndCurrencyCode) return 1m;

            var rates = await _unitOfWork.Repository<ExchangeRate>().GetAllAsync();
            var rate = rates.FirstOrDefault(r => NormalizeCode(r.CurrencyCode) == code)
                ?? throw new NotFoundException($"Chưa cấu hình tỷ giá cho {code}. Vui lòng liên hệ Admin bổ sung.");

            if (!rate.IsActive)
                throw new ArgumentException($"Tỷ giá {code} đang tắt, không dùng để báo giá được.");

            if (rate.RateToVnd <= 0m)
                throw new ArgumentException($"Tỷ giá {code} không hợp lệ ({rate.RateToVnd}).");

            return rate.RateToVnd;
        }

        public async Task<CurrencyConversionDto> ConvertToVndAsync(string currencyCode, decimal amount)
        {
            var code = NormalizeCode(currencyCode);
            if (string.IsNullOrEmpty(code)) code = VndCurrencyCode;

            var rate = await GetRateToVndAsync(code);

            return new CurrencyConversionDto
            {
                Currency = code,
                ExchangeRate = rate,
                AmountOriginal = amount,
                AmountVnd = Math.Round(amount * rate, 0, MidpointRounding.AwayFromZero)
            };
        }

        private static string NormalizeCode(string? value) =>
            (value ?? string.Empty).Trim().ToUpperInvariant();

        private static ExchangeRateDto ToDto(ExchangeRate entity) => new()
        {
            Id = entity.Id,
            CurrencyCode = entity.CurrencyCode,
            CurrencyName = entity.CurrencyName,
            RateToVnd = entity.RateToVnd,
            IsActive = entity.IsActive,
            Note = entity.Note,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
