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
        /// Phiếu tiếp nhận của một đơn, dành cho khách tự tra. Trả null khi đơn chưa có phiếu.
        /// Ném <see cref="UnauthorizedAccessException"/> nếu đơn không thuộc về người gọi.
        /// </summary>
        Task<WarehouseReceivingNoteDetailDto?> GetMyReceivingNoteAsync(Guid consignmentOrderId, Guid userId);

        /// <summary>
        /// Nhân viên kho xác nhận nhập kho: ghi nhận số lượng và trọng lượng thực tế
        /// từng dòng hàng, tính chênh lệch so với khai báo và chuyển phiếu sang RECEIVED.
        /// </summary>
        Task<WarehouseReceivingNoteDetailDto> ConfirmReceiptAsync(Guid receivingNoteId, ConfirmReceiptRequestDto request);

        /// <summary>
        /// Danh sách phiếu tiếp nhận cho màn hàng đợi của kho và màn duyệt của OM.
        /// Phiếu đang chờ duyệt luôn xếp lên đầu.
        /// </summary>
        Task<PaginatedResponse<WarehouseReceivingNoteListItemDto>> GetReceivingNotesAsync(
            string? status, Guid? warehouseId, string? search, int pageNumber, int pageSize);

        /// <summary>
        /// Operations Manager duyệt hoặc từ chối biên bản đối chiếu. Phiếu APPROVED là điều kiện
        /// để kho được xếp hàng lên kệ.
        /// </summary>
        Task<WarehouseReceivingNoteDetailDto> UpdateStatusAsync(
            Guid receivingNoteId, Guid userId, string userRole, UpdateReceivingNoteStatusDto request);

        /// <summary>
        /// Sinh phiếu tiếp nhận cho đơn ký gửi vừa thu được tiền. Idempotent và không ném lỗi —
        /// xem phần ghi chú của bản cài đặt.
        /// </summary>
        /// <returns>Id phiếu đang có hiệu lực, hoặc null nếu không áp dụng / không sinh được.</returns>
        Task<Guid?> EnsureReceivingNoteForPaidOrderAsync(Guid orderId);
    }
}
