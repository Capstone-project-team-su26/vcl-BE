using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Phiếu nhập kho tại kho VN — nhánh khách gửi lại hàng thay vì nhận ngay.
    /// Kho lập phiếu, OM duyệt, rồi kho mới được xếp hàng lên kệ.
    /// </summary>
    public interface IWarehouseInboundService
    {
        Task<WarehouseInboundRequestDto> CreateAsync(Guid userId, string userRole, CreateWarehouseInboundRequestDto request);

        Task<WarehouseInboundRequestDto> UpdateStatusAsync(Guid inboundRequestId, Guid userId, string userRole, UpdateWarehouseInboundStatusDto request);

        Task<WarehouseInboundRequestDto> GetByIdAsync(Guid inboundRequestId);

        Task<List<WarehouseInboundRequestDto>> GetListAsync(string? status, Guid? shipmentId);

        /// <summary>
        /// Đơn đang chờ kho lập phiếu nhập kho: khách đã tất toán, kiện đã kiểm đếm và được
        /// phân loại gửi lại kho, và (với ký gửi) Sale đã báo.
        /// </summary>
        Task<List<PendingInboundOrderDto>> GetPendingInboundOrdersAsync(string userRole);
    }
}
