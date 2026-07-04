using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class ProductTypeService : IProductTypeService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ProductTypeService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<ProductTypeResponse>> GetActiveProductTypesAsync()
        {
            var all = await _unitOfWork.Repository<ProductType>().GetAllAsync();
            return all
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .Select(t => new ProductTypeResponse { Id = t.Id, Name = t.Name ?? string.Empty })
                .ToList();
        }
    }
}
