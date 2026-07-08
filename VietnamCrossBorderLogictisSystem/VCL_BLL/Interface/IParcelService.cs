using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IParcelService
    {
        Task<ParcelCheckInResponse> CheckInParcelAsync(ParcelCheckInRequest request);
        Task<PaginatedResponse<PendingPutawayParcelDto>> GetPendingPutawayParcelsAsync(int pageNumber, int pageSize, string? search, string? status);
        Task<ParcelPutAwayResponse> PutAwayParcelAsync(Guid parcelId, ParcelPutAwayRequest request);
        Task<ParcelDetailResponse> GetParcelDetailAsync(Guid parcelId);
        Task<List<CustomerParcelDto>> GetCustomerParcelsAsync(Guid userId, string? consignmentCode, string? searchCode);
    }
}
