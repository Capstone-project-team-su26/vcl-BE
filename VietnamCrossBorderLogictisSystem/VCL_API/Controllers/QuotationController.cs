using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/orders")]
    [Authorize]
    public class QuotationController : ControllerBase
    {
        private readonly IQuotationService _quotationService;

        public QuotationController(IQuotationService quotationService)
        {
            _quotationService = quotationService;
        }

        /// <summary>
        /// Tạo báo giá tạm tính cho yêu cầu ký gửi.
        /// Tính phí dự kiến dựa trên thông tin Order và OrderItem hiện có.
        /// </summary>
        /// <param name="orderId">ID của đơn hàng cần tạo báo giá.</param>
        /// <returns>Thông tin báo giá tạm tính.</returns>
        /// <response code="200">Tạo báo giá tạm tính thành công.</response>
        /// <response code="400">Đơn hàng chưa có sản phẩm.</response>
        /// <response code="401">Chưa đăng nhập.</response>
        /// <response code="404">Không tìm thấy đơn hàng.</response>
        [HttpPost("{orderId}/quotation/estimate")]
        public async Task<IActionResult> CreateEstimateQuotation(Guid orderId)
        {
            try
            {
                var result = await _quotationService.CreateEstimateAsync(orderId);
                return Ok(result);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
