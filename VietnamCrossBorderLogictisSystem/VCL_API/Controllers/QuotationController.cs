using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
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
        /// Tính phí dự kiến dựa trên loại ký gửi, cân nặng/kích thước ước tính, và bảng giá dịch vụ.
        /// </summary>
        /// <param name="orderId">ID của đơn hàng ký gửi</param>
        /// <returns>Thông tin báo giá tạm tính</returns>
        /// <response code="200">Tạo báo giá tạm tính thành công</response>
        /// <response code="400">Đơn hàng chưa có sản phẩm</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="404">Không tìm thấy đơn hàng</response>
        [HttpPost("api/orders/{orderId}/quotation/estimate")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CreateEstimateQuotation(Guid orderId)
        {
            try
            {
                var result = await _quotationService.CreateEstimateQuotationAsync(orderId);
                return Ok(new
                {
                    message = "Tạo báo giá tạm tính thành công.",
                    data = result
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Khách hàng xác nhận báo giá tạm tính.
        /// Cập nhật Quotation → ACCEPTED, Order → WAITING_FOR_PARCEL.
        /// </summary>
        /// <param name="quotationId">ID của báo giá cần xác nhận</param>
        /// <returns>Thông tin sau khi xác nhận</returns>
        /// <response code="200">Xác nhận báo giá thành công</response>
        /// <response code="400">Báo giá đã hết hạn hoặc trạng thái không hợp lệ</response>
        /// <response code="401">Chưa đăng nhập hoặc không phải chủ đơn hàng</response>
        /// <response code="404">Không tìm thấy báo giá</response>
        [HttpPut("api/quotations/{quotationId}/accept")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> AcceptQuotation(Guid quotationId)
        {
            try
            {
                // Lấy userId từ JWT claims
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    return Unauthorized(new { message = "Không thể xác định người dùng." });

                var result = await _quotationService.AcceptQuotationAsync(quotationId, userId);
                return Ok(new
                {
                    message = "Xác nhận báo giá thành công.",
                    data = result
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
