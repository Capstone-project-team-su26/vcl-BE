using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IOrderService
    {
        Task UpdateConsignmentStatusAsync(Guid orderId, UpdateConsignmentStatusRequest request);
    }
}
