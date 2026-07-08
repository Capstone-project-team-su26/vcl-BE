using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IWarehouseReceivingNoteService
    {
        Task<WarehouseReceivingNoteResponseDto> GetReceivingNoteByConsignmentIdAsync(Guid consignmentOrderId);
        Task<WarehouseReceivingNoteResponseDto> CreateReceivingNoteAsync(CreateWarehouseReceivingNoteRequestDto request);
        Task<WarehouseReceivingNoteScanResponseDto> ScanReceivingNoteAsync(string code);
    }
}
