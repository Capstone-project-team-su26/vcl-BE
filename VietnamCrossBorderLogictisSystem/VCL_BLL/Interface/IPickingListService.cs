using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPickingListService
    {
        Task<PickingListResponseDto> GetPickingListByIdAsync(Guid pickingListId, Guid userId, string role);
        Task<ScanPickingItemResponseDto> ScanPickingItemAsync(Guid pickingListId, Guid userId, string role, ScanPickingItemRequestDto request);
        Task<ConfirmPickingResponseDto> ConfirmPickingListAsync(Guid pickingListId, Guid userId, string role, ConfirmPickingRequestDto request);
    }
}

