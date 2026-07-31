using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IProductTypeService
    {
        Task<List<ProductTypeResponse>> GetActiveProductTypesAsync();

        /// <summary>Danh sách loại hàng cho màn quản trị, gồm cả loại đã ngừng sử dụng</summary>
        Task<List<ProductTypeDetailResponse>> GetAllProductTypesAsync();

        Task<ProductTypeDetailResponse> GetByIdAsync(System.Guid id);

        Task<ProductTypeDetailResponse> CreateAsync(CreateProductTypeRequest request);

        Task UpdateAsync(System.Guid id, UpdateProductTypeRequest request);

        /// <summary>Ngừng sử dụng loại hàng. Loại hàng đã dùng trong đơn sẽ không bị xóa vật lý.</summary>
        Task DeleteAsync(System.Guid id);
    }
}
