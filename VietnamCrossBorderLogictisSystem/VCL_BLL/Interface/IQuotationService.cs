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
        /// <param name="orderId">Đơn ký gửi cần báo giá.</param>
        /// <param name="selectedPricingRuleIds">Danh sách PricingRule (phụ phí TÙY CHỌN) khách tự tick.
        /// Null = client cũ chưa gửi -> dùng logic đoán theo note như trước.</param>
        Task<EstimateQuotationResponse> CreateEstimateQuotationAsync(Guid orderId, IReadOnlyCollection<Guid>? selectedPricingRuleIds = null);

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

        /// <summary>
        /// Lấy thông tin báo giá theo ID báo giá.
        /// </summary>
        /// <param name="quotationId">ID của báo giá</param>
        Task<QuotationDetailResponse> GetQuotationByIdAsync(Guid quotationId);
    }
}
