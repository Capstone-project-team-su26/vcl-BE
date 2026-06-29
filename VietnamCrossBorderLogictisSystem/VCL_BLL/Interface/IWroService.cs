using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IWroService
    {
        Task<WroResponseDto> CreateWroRequestAsync(Guid userId, string userRole, CreateWroRequestDto request);
    }
}
