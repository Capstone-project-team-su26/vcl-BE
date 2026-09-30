using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;
using VCL_BLL.Helpers;

namespace VCL_BLL.Services
{
    public class AdditionalServiceFeeService : IAdditionalServiceFeeService
    {
        private readonly IUnitOfWork _unitOfWork;

        private static readonly string[] ValidCalculationTypes = { "FIXED", "PERCENTAGE" };

        public AdditionalServiceFeeService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<AdditionalServiceFeeResponseDto>> GetAllAsync(bool activeOnly)
        {
            var repo = _unitOfWork.Repository<AdditionalServiceFee>();
            var all = await repo.GetAllAsync();

            var query = all.AsEnumerable();
            if (activeOnly)
            {
                query = query.Where(f => f.IsActive);
            }

            return query
                .OrderBy(f => f.FeeName)
                .Select(MapToDto)
                .ToList();
        }

        public async Task<AdditionalServiceFeeResponseDto> GetByIdAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<AdditionalServiceFee>();
            var fee = await repo.GetByIdAsync(id);

            if (fee == null)
            {
                throw new NotFoundException($"Không tìm thấy loại phí dịch vụ bổ sung với ID: {id}");
            }

            return MapToDto(fee);
        }

        public async Task<AdditionalServiceFeeResponseDto> CreateAsync(CreateAdditionalServiceFeeRequestDto request)
        {
            if (request == null)
            {
                throw new ArgumentException("Dữ liệu phí dịch vụ bổ sung không được để trống.");
            }

            ValidateRequest(request.FeeName, request.FeeCode, request.CalculationType, request.Value);

            var repo = _unitOfWork.Repository<AdditionalServiceFee>();
            var all = await repo.GetAllAsync();

            // Check duplicate code
            if (all.Any(f => string.Equals(f.FeeCode, request.FeeCode.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã loại phí '{request.FeeCode}' đã tồn tại.");
            }

            var fee = new AdditionalServiceFee
            {
                Id = Guid.NewGuid(),
                FeeName = request.FeeName.Trim(),
                FeeCode = request.FeeCode.Trim().ToUpper(),
                CalculationType = request.CalculationType.Trim().ToUpper(),
                Value = request.Value,
                Unit = request.Unit?.Trim(),
                IsActive = request.IsActive,
                Description = request.Description?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(fee);
            await _unitOfWork.SaveChangesAsync();

            return MapToDto(fee);
        }

        public async Task UpdateAsync(Guid id, UpdateAdditionalServiceFeeRequestDto request)
        {
            if (request == null)
            {
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");
            }

            ValidateRequest(request.FeeName, request.FeeCode, request.CalculationType, request.Value);

            var repo = _unitOfWork.Repository<AdditionalServiceFee>();
            var fee = await repo.GetByIdAsync(id);

            if (fee == null)
            {
                throw new NotFoundException($"Không tìm thấy loại phí dịch vụ bổ sung với ID: {id}");
            }

            // Check duplicate code with other records
            var all = await repo.GetAllAsync();
            if (all.Any(f => f.Id != id && string.Equals(f.FeeCode, request.FeeCode.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã loại phí '{request.FeeCode}' đã tồn tại.");
            }

            fee.FeeName = request.FeeName.Trim();
            fee.FeeCode = request.FeeCode.Trim().ToUpper();
            fee.CalculationType = request.CalculationType.Trim().ToUpper();
            fee.Value = request.Value;
            fee.Unit = request.Unit?.Trim();
            fee.IsActive = request.IsActive;
            fee.Description = request.Description?.Trim();
            fee.UpdatedAt = DateTime.UtcNow;

            repo.Update(fee);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<AdditionalServiceFee>();
            var fee = await repo.GetByIdAsync(id);

            if (fee == null)
            {
                throw new NotFoundException($"Không tìm thấy loại phí dịch vụ bổ sung với ID: {id}");
            }

            repo.Delete(fee);
            await _unitOfWork.SaveChangesAsync();
        }

        private void ValidateRequest(string name, string code, string calcType, decimal value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Tên loại phí không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Mã loại phí không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(calcType))
            {
                throw new ArgumentException("Cách tính phí không được để trống.");
            }

            if (!ValidCalculationTypes.Contains(calcType.Trim().ToUpper()))
            {
                throw new ArgumentException($"Cách tính phí {StatusNames.Quote(calcType)} không hợp lệ. Chỉ chọn {StatusNames.QuoteAny("FIXED", "PERCENTAGE")}.");
            }

            if (value < 0)
            {
                throw new ArgumentException("Giá trị phí phải lớn hơn hoặc bằng 0.");
            }
        }

        private static AdditionalServiceFeeResponseDto MapToDto(AdditionalServiceFee f) => new AdditionalServiceFeeResponseDto
        {
            Id = f.Id,
            FeeName = f.FeeName ?? string.Empty,
            FeeCode = f.FeeCode ?? string.Empty,
            CalculationType = f.CalculationType ?? string.Empty,
            Value = f.Value,
            Unit = f.Unit,
            IsActive = f.IsActive,
            Description = f.Description,
            CreatedAt = f.CreatedAt,
            UpdatedAt = f.UpdatedAt
        };
    }
}
