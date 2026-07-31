using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IInventoryService
    {
        Task<InventoryResponseDto> RecordInventoryFromParcelAsync(Guid parcelId);
        Task<List<InventoryResponseDto>> GetInventoriesAsync(string? status, Guid? customerId, Guid? warehouseId);

        /// <summary>Điều chuyển kiện hàng của một bản ghi tồn kho sang ô kệ khác trong cùng kho</summary>
        Task<InventoryResponseDto> RelocateInventoryAsync(Guid inventoryId, RelocateInventoryRequestDto request, Guid? userId);

        /// <summary>Lấy nhật ký di chuyển kiện hàng trong kho</summary>
        Task<List<InventoryMovementResponseDto>> GetMovementsAsync(Guid? warehouseId, Guid? parcelId, int limit);
    }
}
