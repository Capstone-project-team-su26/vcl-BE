using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IInternationalShipmentService
    {
        /// <summary>Danh sách lô vận chuyển quốc tế, lọc theo tab/trạng thái/kho.</summary>
        Task<PaginatedResponse<InternationalShipmentResponseDto>> GetListAsync(
            int pageNumber, int pageSize, string? statusTab, string? status, Guid? originWarehouseId, string? search);


        /// <summary>Chi tiết một lô vận chuyển quốc tế.</summary>
        Task<InternationalShipmentResponseDto> GetByIdAsync(Guid shipmentId);
        Task<ConfirmShipmentArrivalResponseDto> ConfirmArrivalAsync(Guid shipmentId, Guid userId, string userRole, ConfirmShipmentArrivalDto request);
        Task<DestinationReceivingResponseDto> ConfirmDestinationReceivingAsync(Guid shipmentId, Guid userId, string userRole, DestinationReceivingDto request);

        /// <summary>
        /// Biên bản kiểm đếm kiện tại kho VN, cho OM theo dõi chênh lệch.
        /// <para>
        /// <paramref name="onlyDiscrepancy"/> mặc định true vì đó là thứ OM mở màn này để xem;
        /// muốn xem cả biên bản khớp thì truyền false.
        /// </para>
        /// </summary>
        Task<ParcelInspectionListDto> GetParcelInspectionsAsync(
            bool onlyDiscrepancy, Guid? shipmentId, string userRole, Guid? userId = null);

        /// <summary>
        /// Kho lưu kết quả đối chiếu một kiện ngay khi cân đếm xong, không đợi chốt cả lô.
        /// <para>
        /// Lưu đè bản ghi cũ của cùng kiện thay vì đẻ thêm dòng mới — sửa số nhập nhầm là
        /// chuyện thường, để lại hai dòng chỉ làm danh sách của OM khó đọc.
        /// </para>
        /// </summary>
        Task<ParcelInspectionDto> SaveParcelInspectionAsync(
            Guid parcelId, SaveParcelInspectionRequest request, Guid userId, string userRole);
    }
}
