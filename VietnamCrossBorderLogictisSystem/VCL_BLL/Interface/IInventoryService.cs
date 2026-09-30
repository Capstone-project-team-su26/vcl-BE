using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IInventoryService
    {
        Task<InventoryResponseDto> RecordInventoryFromParcelAsync(Guid parcelId);
        Task<List<InventoryResponseDto>> GetInventoriesAsync(string? status, Guid? customerId, Guid? warehouseId, string? region = null, IReadOnlyCollection<Guid>? warehouseIds = null);

        /// <summary>Điều chuyển kiện hàng của một bản ghi tồn kho sang ô kệ khác trong cùng kho</summary>
        Task<InventoryResponseDto> RelocateInventoryAsync(Guid inventoryId, RelocateInventoryRequestDto request, Guid? userId);

        /// <summary>Lấy nhật ký di chuyển kiện hàng trong kho</summary>
        Task<List<InventoryMovementResponseDto>> GetMovementsAsync(Guid? warehouseId, Guid? parcelId, int limit);

        /// <summary>Ghi nhận một lần kiểm kê định kỳ tại ô kệ</summary>
        Task<CycleCountResponseDto> CreateCycleCountAsync(CreateCycleCountRequestDto request, Guid? userId);

        /// <summary>Lấy lịch sử kiểm kê định kỳ</summary>
        Task<List<CycleCountResponseDto>> GetCycleCountsAsync(Guid? warehouseId, Guid? binId, string? status, int limit);

        /// <summary>Ghi nhận cách xử lý chênh lệch của một phiếu kiểm kê</summary>
        Task<CycleCountResponseDto> ResolveCycleCountAsync(Guid cycleCountId, ResolveCycleCountRequestDto request, Guid? userId);

        /// <summary>Danh sách kiện hàng tồn kho vượt ngưỡng cảnh báo thời gian lưu</summary>
        Task<List<LongStayAlertResponseDto>> GetLongStayAlertsAsync(Guid? warehouseId, Guid? customerId, int minDays, int criticalDays, int limit, string? region = null, IReadOnlyCollection<Guid>? warehouseIds = null);

        /// <summary>Báo cáo tồn kho tổng hợp theo từng khách hàng</summary>
        Task<List<CustomerStorageReportResponseDto>> GetCustomerStorageReportAsync(Guid? warehouseId, Guid? customerId, string? region = null, IReadOnlyCollection<Guid>? warehouseIds = null);
    }
}
