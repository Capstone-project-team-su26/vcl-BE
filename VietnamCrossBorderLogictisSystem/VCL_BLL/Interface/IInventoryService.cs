using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IInventoryService
    {
        Task<InventoryResponseDto> RecordInventoryFromParcelAsync(Guid parcelId);
        Task<List<InventoryResponseDto>> GetInventoriesAsync(string? status, Guid? customerId);
    }
}
