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

        /// <summary>Lấy biên bản nhập kho kèm các dòng đối chiếu khai báo với thực tế</summary>
        Task<WarehouseReceivingNoteDetailDto> GetReceivingNoteDetailAsync(Guid receivingNoteId);

        /// <summary>
        /// Nhân viên kho xác nhận nhập kho: ghi nhận số lượng và trọng lượng thực tế
        /// từng dòng hàng, tính chênh lệch so với khai báo và chuyển phiếu sang RECEIVED.
        /// </summary>
        Task<WarehouseReceivingNoteDetailDto> ConfirmReceiptAsync(Guid receivingNoteId, ConfirmReceiptRequestDto request);
    }
}
