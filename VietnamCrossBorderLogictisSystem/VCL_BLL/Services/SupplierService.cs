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
    public class SupplierService : ISupplierService
    {
        private readonly IUnitOfWork _unitOfWork;

        public SupplierService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<SupplierDto>> GetAllAsync(string? supplierType, bool? isActive)
        {
            var all = await _unitOfWork.Repository<Supplier>().GetAllAsync();
            var query = all.AsQueryable();

            if (!string.IsNullOrWhiteSpace(supplierType))
            {
                query = query.Where(s => string.Equals(s.SupplierType, supplierType, StringComparison.OrdinalIgnoreCase));
            }

            if (isActive.HasValue)
            {
                query = query.Where(s => s.IsActive == isActive.Value);
            }

            return query.OrderBy(s => s.SupplierName).Select(ToDto).ToList();
        }

        public async Task<SupplierDto> GetByIdAsync(Guid id)
        {
            var supplier = await _unitOfWork.Repository<Supplier>().GetByIdAsync(id);
            if (supplier == null)
            {
                throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {id}");
            }

            return ToDto(supplier);
        }

        public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request)
        {
            var repo = _unitOfWork.Repository<Supplier>();
            var all = await repo.GetAllAsync();

            var code = request.SupplierCode.Trim();
            if (all.Any(s => string.Equals(s.SupplierCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã nhà cung cấp \"{code}\" đã tồn tại.");
            }

            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                SupplierCode = code,
                SupplierName = request.SupplierName.Trim(),
                SupplierType = request.SupplierType.Trim().ToUpperInvariant(),
                Country = request.Country?.Trim() ?? string.Empty,
                ContactPerson = request.ContactPerson?.Trim() ?? string.Empty,
                Phone = request.Phone?.Trim() ?? string.Empty,
                Email = request.Email?.Trim() ?? string.Empty,
                Address = request.Address?.Trim() ?? string.Empty,
                Note = request.Note?.Trim() ?? string.Empty,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(supplier);
            await _unitOfWork.SaveChangesAsync();

            return ToDto(supplier);
        }

        public async Task UpdateAsync(Guid id, UpdateSupplierRequest request)
        {
            var repo = _unitOfWork.Repository<Supplier>();
            var supplier = await repo.GetByIdAsync(id);
            if (supplier == null)
            {
                throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {id}");
            }

            var all = await repo.GetAllAsync();
            var code = request.SupplierCode.Trim();
            if (all.Any(s => s.Id != id && string.Equals(s.SupplierCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã nhà cung cấp \"{code}\" đã tồn tại.");
            }

            supplier.SupplierCode = code;
            supplier.SupplierName = request.SupplierName.Trim();
            supplier.SupplierType = request.SupplierType.Trim().ToUpperInvariant();
            supplier.Country = request.Country?.Trim() ?? string.Empty;
            supplier.ContactPerson = request.ContactPerson?.Trim() ?? string.Empty;
            supplier.Phone = request.Phone?.Trim() ?? string.Empty;
            supplier.Email = request.Email?.Trim() ?? string.Empty;
            supplier.Address = request.Address?.Trim() ?? string.Empty;
            supplier.Note = request.Note?.Trim() ?? string.Empty;
            supplier.IsActive = request.IsActive;
            supplier.UpdatedAt = DateTime.UtcNow;

            repo.Update(supplier);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<Supplier>();
            var supplier = await repo.GetByIdAsync(id);
            if (supplier == null)
            {
                throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {id}");
            }

            // Nhà cung cấp đang được gán cho tuyến vận chuyển thì chỉ ngừng sử dụng
            var routes = await _unitOfWork.Repository<ShippingRoute>().GetAllAsync();
            if (routes.Any(r => r.SupplierId == id))
            {
                if (!supplier.IsActive)
                {
                    throw new ArgumentException("Nhà cung cấp đã được ngừng sử dụng trước đó.");
                }

                supplier.IsActive = false;
                supplier.UpdatedAt = DateTime.UtcNow;
                repo.Update(supplier);
            }
            else
            {
                repo.Delete(supplier);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        private static SupplierDto ToDto(Supplier supplier) => new SupplierDto
        {
            Id = supplier.Id,
            SupplierCode = supplier.SupplierCode ?? string.Empty,
            SupplierName = supplier.SupplierName ?? string.Empty,
            SupplierType = supplier.SupplierType ?? string.Empty,
            Country = supplier.Country ?? string.Empty,
            ContactPerson = supplier.ContactPerson ?? string.Empty,
            Phone = supplier.Phone ?? string.Empty,
            Email = supplier.Email ?? string.Empty,
            Address = supplier.Address ?? string.Empty,
            Note = supplier.Note ?? string.Empty,
            IsActive = supplier.IsActive,
            CreatedAt = supplier.CreatedAt
        };
    }
}
