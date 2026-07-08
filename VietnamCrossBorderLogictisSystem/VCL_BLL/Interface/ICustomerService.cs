using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface ICustomerService
    {
        Task<IEnumerable<CustomerResponseDto>> GetAllCustomersAsync(string? search);
        Task<CustomerResponseDto> GetCustomerByIdAsync(Guid id);
        Task<CustomerResponseDto> CreateCustomerAsync(CreateCustomerRequestDto request);
        Task<CustomerResponseDto> UpdateCustomerAsync(Guid id, UpdateCustomerRequestDto request);
        Task<CustomerProfileResponseDto> GetMyProfileAsync(Guid userId);
        Task<CustomerProfileResponseDto> UpdateMyProfileAsync(Guid userId, UpdateCustomerProfileRequestDto request);
        Task DeactivateCustomerAsync(Guid id);
    }
}
