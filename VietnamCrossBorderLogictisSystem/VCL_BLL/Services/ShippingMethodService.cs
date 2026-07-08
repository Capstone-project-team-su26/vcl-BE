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
    public class ShippingMethodService : IShippingMethodService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ShippingMethodService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<ShippingMethodResponseDto>> GetAllAsync(bool activeOnly)
        {
            var all = await _unitOfWork.Repository<ShippingMethod>().GetAllAsync();
            var query = all.AsEnumerable();
            if (activeOnly)
                query = query.Where(m => m.IsActive);

            return query
                .OrderBy(m => m.MethodName)
                .Select(MapToDto)
                .ToList();
        }

        public async Task<ShippingMethodResponseDto> GetByIdAsync(Guid id)
        {
            var method = await _unitOfWork.Repository<ShippingMethod>().GetByIdAsync(id);
            if (method == null)
                throw new NotFoundException($"Không tìm thấy phương thức vận chuyển với ID: {id}");
            return MapToDto(method);
        }

        public async Task<ShippingMethodResponseDto> CreateAsync(CreateShippingMethodRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu phương thức vận chuyển không được để trống.");
            if (string.IsNullOrWhiteSpace(request.MethodName))
                throw new ArgumentException("Tên phương thức vận chuyển không được để trống.");
            if (string.IsNullOrWhiteSpace(request.MethodCode))
                throw new ArgumentException("Mã phương thức vận chuyển không được để trống.");

            var repo = _unitOfWork.Repository<ShippingMethod>();
            var all = await repo.GetAllAsync();

            // Không cho trùng mã
            if (all.Any(m => string.Equals(m.MethodCode, request.MethodCode.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ConflictException($"Mã phương thức vận chuyển '{request.MethodCode}' đã tồn tại.");

            var method = new ShippingMethod
            {
                Id = Guid.NewGuid(),
                MethodName = request.MethodName.Trim(),
                MethodCode = request.MethodCode.Trim(),
                Description = request.Description,
                EstimatedTransitTime = request.EstimatedTransitTime,
                ApplicableCondition = request.ApplicableCondition,
                IsActive = request.IsActive,
                InternalNote = request.InternalNote,
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(method);
            await _unitOfWork.SaveChangesAsync();

            return MapToDto(method);
        }

        public async Task UpdateAsync(Guid id, UpdateShippingMethodRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            var repo = _unitOfWork.Repository<ShippingMethod>();
            var method = await repo.GetByIdAsync(id);
            if (method == null)
                throw new NotFoundException($"Không tìm thấy phương thức vận chuyển với ID: {id}");

            if (string.IsNullOrWhiteSpace(request.MethodCode))
                throw new ArgumentException("Mã phương thức vận chuyển không được để trống.");

            // Không cho trùng mã với phương thức khác
            var all = await repo.GetAllAsync();
            if (all.Any(m => m.Id != id
                && string.Equals(m.MethodCode, request.MethodCode.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ConflictException($"Mã phương thức vận chuyển '{request.MethodCode}' đã tồn tại.");

            method.MethodName = request.MethodName.Trim();
            method.MethodCode = request.MethodCode.Trim();
            method.Description = request.Description;
            method.EstimatedTransitTime = request.EstimatedTransitTime;
            method.ApplicableCondition = request.ApplicableCondition;
            method.IsActive = request.IsActive;
            method.InternalNote = request.InternalNote;

            repo.Update(method);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<ShippingMethod>();
            var method = await repo.GetByIdAsync(id);
            if (method == null)
                throw new NotFoundException($"Không tìm thấy phương thức vận chuyển với ID: {id}");

            repo.Delete(method);
            await _unitOfWork.SaveChangesAsync();
        }

        private static ShippingMethodResponseDto MapToDto(ShippingMethod m) => new ShippingMethodResponseDto
        {
            Id = m.Id,
            MethodName = m.MethodName ?? string.Empty,
            MethodCode = m.MethodCode ?? string.Empty,
            Description = m.Description,
            EstimatedTransitTime = m.EstimatedTransitTime,
            ApplicableCondition = m.ApplicableCondition,
            IsActive = m.IsActive,
            InternalNote = m.InternalNote,
            CreatedAt = m.CreatedAt
        };
    }
}
