using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IQuotationService
    {
        /// <summary>
        /// Tạo báo giá tạm tính cho đơn hàng ký gửi dựa trên thông tin OrderItems, cộng phí thùng và
        /// phí dịch vụ khách chọn cho từng kiện.
        /// </summary>
        /// <param name="orderId">Đơn ký gửi cần báo giá.</param>
        Task<EstimateQuotationResponse> CreateEstimateQuotationAsync(Guid orderId);

        /// <summary>
        /// Ước tính báo giá cho một đơn CHƯA tạo. Chạy đúng hàm tính của báo giá tạm tính,
        /// chỉ khác là không ghi gì xuống DB — để màn xác nhận hiện đúng con số hệ thống
        /// sẽ phát hành ngay sau khi khách bấm tạo đơn.
        /// </summary>
        Task<EstimateQuotationResponse> PreviewEstimateAsync(
            VCL_DLL.Models.Order order,
            System.Collections.Generic.List<VCL_DLL.Models.OrderItem> orderItems,
            System.Collections.Generic.IReadOnlyDictionary<System.Guid, System.Collections.Generic.List<VCL_DLL.Models.PricingRule>> itemServices);

        /// <summary>
        /// Lưu báo giá chính thức gửi khách hàng, cập nhật Order → QUOTATION_SENT, Quotation → SENT/PENDING.
        /// </summary>
        Task<QuotationActionResponse> SendQuotationAsync(Guid orderId, CreateQuotationRequest request, Guid actorId);

        Task<QuotationActionResponse> DecidePriceOverrideAsync(Guid quotationId, Guid actorId, string actorRole, QuotationPriceDecisionRequest request);

        /// <summary>
        /// Từ chối báo giá theo OrderId. Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        Task<QuotationActionResponse> RejectQuotationByOrderIdAsync(Guid orderId, string reason, Guid actorId, string actorRole);

        /// <summary>
        /// Từ chối báo giá theo QuotationId. Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        Task<QuotationActionResponse> RejectQuotationByIdAsync(Guid quotationId, string reason, Guid actorId, string actorRole);

        /// <summary>
        /// Khách hàng xác nhận báo giá tạm tính.
        /// Cập nhật Quotation → ACCEPTED, Order → QUOTATION_CONFIRMED.
        /// </summary>
        /// <param name="quotationId">ID của báo giá cần xác nhận</param>
        /// <param name="userId">ID của user đang đăng nhập (từ JWT)</param>
        /// <returns>Thông tin sau khi xác nhận</returns>

        /// <summary>
        /// Lấy thông tin báo giá mới nhất (SENT/PENDING) theo đơn hàng để khách hàng xem.
        /// </summary>
        /// <param name="orderId">ID của đơn hàng</param>
        Task<QuotationDetailResponse> GetQuotationByOrderIdAsync(Guid orderId, Guid actorId, string actorRole);

        /// <summary>
        /// Lấy thông tin báo giá theo ID báo giá.
        /// </summary>
        /// <param name="quotationId">ID của báo giá</param>
        Task<QuotationDetailResponse> GetQuotationByIdAsync(Guid quotationId, Guid actorId, string actorRole);
    }
}
