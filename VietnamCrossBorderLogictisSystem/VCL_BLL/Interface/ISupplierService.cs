using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface ISupplierService
    {
        Task<List<SupplierDto>> GetAllAsync(string? supplierType, bool? isActive);
        Task<SupplierDto> GetByIdAsync(Guid id);
        Task<SupplierDto> CreateAsync(CreateSupplierRequest request);
        Task UpdateAsync(Guid id, UpdateSupplierRequest request);
        Task DeleteAsync(Guid id);
    }
}
