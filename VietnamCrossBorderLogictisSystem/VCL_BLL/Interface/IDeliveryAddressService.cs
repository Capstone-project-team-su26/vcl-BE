using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IDeliveryAddressService
    {
        Task<IEnumerable<DeliveryAddressResponse>> GetMyAddressesAsync(Guid userId);
        Task<DeliveryAddressResponse> CreateAsync(Guid userId, CreateDeliveryAddressRequest request);
        Task UpdateAsync(Guid userId, Guid addressId, UpdateDeliveryAddressRequest request);
        Task DeleteAsync(Guid userId, Guid addressId);
    }
}
