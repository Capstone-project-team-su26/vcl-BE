using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IQuotationService
    {
        /// <summary>
        /// Tạo báo giá tạm tính cho đơn hàng ký gửi dựa trên thông tin OrderItems.
        /// </summary>
        /// <param name="orderId">ID của đơn hàng cần tạo báo giá</param>
        /// <returns>Thông tin báo giá tạm tính</returns>
        Task<EstimateQuotationResponse> CreateEstimateQuotationAsync(Guid orderId);
    }
}
