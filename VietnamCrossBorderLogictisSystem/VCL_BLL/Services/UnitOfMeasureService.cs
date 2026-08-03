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
    public class UnitOfMeasureService : IUnitOfMeasureService
    {
        private readonly IUnitOfWork _unitOfWork;

        public UnitOfMeasureService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<UnitOfMeasureResponse>> GetActiveUnitsAsync()
        {
            var all = await _unitOfWork.Repository<UnitOfMeasure>().GetAllAsync();
            return all
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayOrder)
                .ThenBy(u => u.UnitName)
                .Select(u => new UnitOfMeasureResponse
                {
                    Id = u.Id,
                    UnitCode = u.UnitCode ?? string.Empty,
                    UnitName = u.UnitName ?? string.Empty
                })
                .ToList();
        }

        public async Task<List<UnitOfMeasureDetailResponse>> GetAllUnitsAsync()
        {
            var all = await _unitOfWork.Repository<UnitOfMeasure>().GetAllAsync();
            return all
                .OrderBy(u => u.DisplayOrder)
                .ThenBy(u => u.UnitName)
                .Select(ToDetail)
                .ToList();
        }

        public async Task<UnitOfMeasureDetailResponse> GetByIdAsync(Guid id)
        {
            var unit = await _unitOfWork.Repository<UnitOfMeasure>().GetByIdAsync(id);
            if (unit == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn vị tính với ID: {id}");
            }

            return ToDetail(unit);
        }

        public async Task<UnitOfMeasureDetailResponse> CreateAsync(CreateUnitOfMeasureRequest request)
        {
            var repo = _unitOfWork.Repository<UnitOfMeasure>();
            var all = await repo.GetAllAsync();

            var code = request.UnitCode.Trim().ToUpperInvariant();
            if (all.Any(u => string.Equals(u.UnitCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã đơn vị tính \"{code}\" đã tồn tại.");
            }

            var unit = new UnitOfMeasure
            {
                Id = Guid.NewGuid(),
                UnitCode = code,
                UnitName = request.UnitName.Trim(),
                Description = request.Description?.Trim() ?? string.Empty,
                DisplayOrder = request.DisplayOrder,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(unit);
            await _unitOfWork.SaveChangesAsync();

            return ToDetail(unit);
        }

        public async Task UpdateAsync(Guid id, UpdateUnitOfMeasureRequest request)
        {
            var repo = _unitOfWork.Repository<UnitOfMeasure>();
            var unit = await repo.GetByIdAsync(id);
            if (unit == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn vị tính với ID: {id}");
            }

            var all = await repo.GetAllAsync();
            var code = request.UnitCode.Trim().ToUpperInvariant();
            if (all.Any(u => u.Id != id && string.Equals(u.UnitCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã đơn vị tính \"{code}\" đã tồn tại.");
            }

            unit.UnitCode = code;
            unit.UnitName = request.UnitName.Trim();
            unit.Description = request.Description?.Trim() ?? string.Empty;
            unit.DisplayOrder = request.DisplayOrder;
            unit.IsActive = request.IsActive;
            unit.UpdatedAt = DateTime.UtcNow;

            repo.Update(unit);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<UnitOfMeasure>();
            var unit = await repo.GetByIdAsync(id);
            if (unit == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn vị tính với ID: {id}");
            }

            // ORDER_ITEMS.unit và PURCHASE_REQUEST_ITEMS lưu đơn vị dưới dạng chuỗi,
            // nên đơn vị đã phát sinh dữ liệu chỉ được ngừng sử dụng để giữ lịch sử.
            var code = unit.UnitCode?.Trim() ?? string.Empty;
            var name = unit.UnitName?.Trim() ?? string.Empty;

            var orderItems = await _unitOfWork.Repository<OrderItem>().GetAllAsync();
            var isUsed = orderItems.Any(i =>
                string.Equals(i.Unit?.Trim(), code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.Unit?.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (isUsed)
            {
                if (!unit.IsActive)
                {
                    throw new ArgumentException("Đơn vị tính đã được ngừng sử dụng trước đó.");
                }

                unit.IsActive = false;
                unit.UpdatedAt = DateTime.UtcNow;
                repo.Update(unit);
            }
            else
            {
                repo.Delete(unit);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        private static UnitOfMeasureDetailResponse ToDetail(UnitOfMeasure unit) => new UnitOfMeasureDetailResponse
        {
            Id = unit.Id,
            UnitCode = unit.UnitCode ?? string.Empty,
            UnitName = unit.UnitName ?? string.Empty,
            Description = unit.Description ?? string.Empty,
            DisplayOrder = unit.DisplayOrder,
            IsActive = unit.IsActive,
            CreatedAt = unit.CreatedAt
        };
    }
}
