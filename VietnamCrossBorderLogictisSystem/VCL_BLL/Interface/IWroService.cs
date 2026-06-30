using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IWroService
    {
        Task<WroResponseDto> CreateWroRequestAsync(Guid userId, string userRole, CreateWroRequestDto request);
        Task<WroDetailResponseDto> GetWroRequestDetailAsync(Guid requestId, Guid userId, string role);
        Task<UpdateWroStatusResponseDto> UpdateWroStatusAsync(Guid requestId, Guid userId, string role, UpdateWroStatusRequestDto request);
    }
}
