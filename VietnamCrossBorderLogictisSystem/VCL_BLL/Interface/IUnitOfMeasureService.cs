using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IUnitOfMeasureService
    {
        /// <summary>Danh sách đơn vị tính đang hoạt động, dùng đổ danh sách chọn</summary>
        Task<List<UnitOfMeasureResponse>> GetActiveUnitsAsync();

        /// <summary>Toàn bộ đơn vị tính cho màn quản trị, gồm cả loại đã ngừng sử dụng</summary>
        Task<List<UnitOfMeasureDetailResponse>> GetAllUnitsAsync();

        Task<UnitOfMeasureDetailResponse> GetByIdAsync(Guid id);

        Task<UnitOfMeasureDetailResponse> CreateAsync(CreateUnitOfMeasureRequest request);

        Task UpdateAsync(Guid id, UpdateUnitOfMeasureRequest request);

        /// <summary>Đơn vị tính đã dùng trong đơn sẽ được ngừng sử dụng thay vì xóa vật lý</summary>
        Task DeleteAsync(Guid id);
    }
}
