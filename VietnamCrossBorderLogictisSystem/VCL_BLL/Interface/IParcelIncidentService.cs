using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>Sự cố hàng hoá: kho mở, khách chọn cách xử lý, quản lý kho quyết, Admin chi bồi thường.</summary>
    public interface IParcelIncidentService
    {
        Task<ParcelIncidentDto> CreateAsync(Guid userId, string role, CreateParcelIncidentRequest request);
        Task<List<ParcelIncidentDto>> CreateComplaintAsync(Guid userId, string role, Guid orderId, CreateComplaintRequest request);
        Task<PagedParcelIncidentsDto> GetListAsync(Guid userId, string role, ParcelIncidentQuery query);
        Task<ParcelIncidentDto> GetDetailAsync(Guid userId, string role, Guid incidentId);
        Task<ParcelIncidentDto> RespondAsync(Guid userId, string role, Guid incidentId, IncidentCustomerResponseRequest request);
        Task<ParcelIncidentDto> ResolveAsync(Guid userId, string role, Guid incidentId, ResolveIncidentRequest request);
        Task<ParcelIncidentDto> MarkCompensationPaidAsync(Guid userId, string role, Guid incidentId, CompensationPaidRequest request);
    }
}
