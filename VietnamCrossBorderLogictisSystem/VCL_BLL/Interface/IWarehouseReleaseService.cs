using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Phiếu xuất kho quốc tế: lập phiếu → quản lý kho duyệt → bốc hàng sang khu xuất → vào lô.
    /// Bàn giao nằm ở lô vận chuyển (<see cref="IInternationalShipmentService"/>).
    /// </summary>
    public interface IWarehouseReleaseService
    {
        Task<List<EligibleParcelDto>> GetEligibleParcelsAsync(
            Guid userId, string role, Guid shippingRouteId, string? search, bool includeBlocked);

        Task<PagedWroListDto> GetListAsync(Guid userId, string role, WroListQuery query);

        Task<WroDetailDto> GetDetailAsync(Guid userId, string role, Guid requestId);

        Task<WroDetailDto> CreateAsync(Guid userId, string role, CreateWarehouseReleaseRequest request);

        Task<WroDetailDto> UpdateAsync(Guid userId, string role, Guid requestId, CreateWarehouseReleaseRequest request);

        Task<WroDetailDto> SubmitAsync(Guid userId, string role, Guid requestId);

        Task<WroDetailDto> WithdrawAsync(Guid userId, string role, Guid requestId);

        Task<WroDetailDto> CancelAsync(Guid userId, string role, Guid requestId);

        Task<WroDetailDto> DecideAsync(Guid userId, string role, Guid requestId, DecideWarehouseReleaseRequest request);

        Task<WroDetailDto> PickAsync(Guid userId, string role, Guid requestId, PickParcelRequest request);

        Task<WroDetailDto> RemoveParcelAsync(Guid userId, string role, Guid requestId, Guid parcelId, RemoveWroParcelRequest request);

        Task<WroDetailDto> SplitAsync(Guid userId, string role, Guid requestId, SplitWarehouseReleaseRequest request);
    }
}
