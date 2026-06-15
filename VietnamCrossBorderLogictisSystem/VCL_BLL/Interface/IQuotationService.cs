using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IQuotationService
    {
        /// <summary>
        /// Tạo báo giá tạm tính cho yêu cầu ký gửi dựa trên Order và OrderItem.
        /// </summary>
        /// <param name="orderId">ID của Order cần tạo báo giá.</param>
        /// <returns>Thông tin báo giá tạm tính.</returns>
        Task<EstimateQuotationResponseDto> CreateEstimateAsync(Guid orderId);
    }
}
