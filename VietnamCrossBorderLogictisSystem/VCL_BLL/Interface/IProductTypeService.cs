using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IProductTypeService
    {
        Task<List<ProductTypeResponse>> GetActiveProductTypesAsync();
    }
}
