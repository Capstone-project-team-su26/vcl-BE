using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IPurchaseOrderService
    {
        Task<PurchaseOrderResponseDto> CreatePurchaseOrderAsync(Guid requestId, Guid userId, string userRole, CreatePurchaseOrderDto request);
        Task<UpdatePurchaseOrderStatusResponseDto> UpdatePurchaseOrderStatusAsync(Guid purchaseOrderId, Guid userId, string userRole, UpdatePurchaseOrderStatusRequestDto request);
    }
}
