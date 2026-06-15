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

        /// <summary>
        /// Khách hàng xác nhận báo giá tạm tính.
        /// Cập nhật Quotation → ACCEPTED, Order → WAITING_FOR_PARCEL.
        /// </summary>
        /// <param name="quotationId">ID của báo giá cần xác nhận</param>
        /// <param name="userId">ID của user đang đăng nhập (từ JWT)</param>
        /// <returns>Thông tin sau khi xác nhận</returns>
        Task<AcceptQuotationResponse> AcceptQuotationAsync(Guid quotationId, Guid userId);
    }
}
