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
        /// Tạo báo giá tạm tính bằng cách truyền thông tin từ FE (không lưu DB, không đổi trạng thái đơn).
        /// </summary>
        Task<EstimateQuotationResponse> CreateEstimateQuotationWithBodyAsync(Guid orderId, CreateQuotationRequest request);

        /// <summary>
        /// Lưu báo giá chính thức gửi khách hàng, cập nhật Order → QUOTATION_SENT, Quotation → SENT/PENDING.
        /// </summary>
        Task<QuotationActionResponse> SendQuotationAsync(Guid orderId, CreateQuotationRequest request);

        /// <summary>
        /// Từ chối báo giá theo OrderId. Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        Task<QuotationActionResponse> RejectQuotationByOrderIdAsync(Guid orderId, string reason);

        /// <summary>
        /// Từ chối báo giá theo QuotationId. Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        Task<QuotationActionResponse> RejectQuotationByIdAsync(Guid quotationId, string reason);

        /// <summary>
        /// Khách hàng xác nhận báo giá tạm tính.
        /// Cập nhật Quotation → ACCEPTED, Order → QUOTATION_CONFIRMED.
        /// </summary>
        /// <param name="quotationId">ID của báo giá cần xác nhận</param>
        /// <param name="userId">ID của user đang đăng nhập (từ JWT)</param>
        /// <returns>Thông tin sau khi xác nhận</returns>
        Task<AcceptQuotationResponse> AcceptQuotationAsync(Guid quotationId, Guid userId);

        /// <summary>
        /// Lấy thông tin báo giá mới nhất (SENT/PENDING) theo đơn hàng để khách hàng xem.
        /// </summary>
        /// <param name="orderId">ID của đơn hàng</param>
        Task<QuotationDetailResponse> GetQuotationByOrderIdAsync(Guid orderId);
    }
}
