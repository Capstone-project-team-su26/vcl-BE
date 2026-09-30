using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IWarehouseReceivingNoteService
    {
        Task<WarehouseReceivingNoteResponseDto> GetReceivingNoteByConsignmentIdAsync(Guid consignmentOrderId);
        Task<WarehouseReceivingNoteDetailDto> CreateReceivingNoteAsync(CreateWarehouseReceivingNoteRequestDto request, Guid actorId);
        /// <summary>
        /// Quét mã phiếu. Truyền người quét thì tài khoản đã được gán kho chỉ quét được phiếu của kho
        /// được gán (ném <see cref="UnauthorizedAccessException"/> nêu tên kho).
        /// </summary>
        Task<WarehouseReceivingNoteScanResponseDto> ScanReceivingNoteAsync(string code, Guid? actorId = null, string? actorRole = null);

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
        Task<WarehouseReceivingNoteDetailDto> ConfirmReceiptAsync(Guid receivingNoteId, ConfirmReceiptRequestDto request, Guid actorId);

        /// <summary>
        /// Danh sách phiếu tiếp nhận cho màn hàng đợi của kho và màn duyệt của OM.
        /// Phiếu đang chờ duyệt luôn xếp lên đầu.
        /// </summary>
        /// <param name="warehouseScope">Null = không giới hạn theo kho; có giá trị = chỉ phiếu của các kho này (kho được gán).</param>
        /// <param name="orderType">CONSIGNMENT / PURCHASE — lọc phiếu ký gửi hay mua hộ. Bỏ trống = tất cả.</param>
        Task<PaginatedResponse<WarehouseReceivingNoteListItemDto>> GetReceivingNotesAsync(
            string? status, Guid? warehouseId, string? search, int pageNumber, int pageSize,
            IReadOnlyCollection<Guid>? warehouseScope = null, string? orderType = null);

        /// <summary>
        /// Operations Manager duyệt hoặc từ chối biên bản đối chiếu. Phiếu APPROVED là điều kiện
        /// để kho được xếp hàng lên kệ.
        /// </summary>
        Task<WarehouseReceivingNoteDetailDto> UpdateStatusAsync(
            Guid receivingNoteId, Guid userId, string userRole, UpdateReceivingNoteStatusDto request);
    }
}
