using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_BLL.DTOs;

namespace VCL_API.Controllers
{
    [ApiController]
    [Authorize]
    public class QuotationController : ControllerBase
    {
        private readonly IQuotationService _quotationService;
        private readonly IConsignmentPaymentService _consignmentPaymentService;

        public QuotationController(IQuotationService quotationService, IConsignmentPaymentService consignmentPaymentService)
        {
            _quotationService = quotationService;
            _consignmentPaymentService = consignmentPaymentService;
        }

        /// <summary>
        /// Lưu báo giá chính thức gửi khách hàng.
        /// Cập nhật Order → QUOTATION_SENT.
        /// </summary>
        [HttpPost("api/orders/{orderId}/quotation/send")]
        [ProducesResponseType(typeof(QuotationActionResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> SendQuotation(Guid orderId, [FromBody] CreateQuotationRequest request)
        {
            try
            {
                var result = await _quotationService.SendQuotationAsync(orderId, request);
                return Ok(new
                {
                    message = result.Message,
                    status = result.Status,
                    consignment = result.Consignment
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
        /// Từ chối báo giá ký gửi theo đơn hàng.
        /// Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        [HttpPost("api/orders/{orderId}/quotation/reject")]
        [ProducesResponseType(typeof(QuotationActionResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RejectQuotationByOrderId(Guid orderId, [FromBody] RejectQuotationRequest request)
        {
            try
            {
                var result = await _quotationService.RejectQuotationByOrderIdAsync(orderId, request.RejectionReason);
                return Ok(new
                {
                    message = result.Message,
                    status = result.Status,
                    rejectionReason = result.RejectionReason,
                    consignment = result.Consignment
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
        /// Từ chối báo giá ký gửi theo ID báo giá.
        /// Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        [HttpPut("api/quotations/{quotationId}/reject")]
        [ProducesResponseType(typeof(QuotationActionResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RejectQuotationById(Guid quotationId, [FromBody] RejectQuotationRequest request)
        {
            try
            {
                var result = await _quotationService.RejectQuotationByIdAsync(quotationId, request.RejectionReason);
                return Ok(new
                {
                    message = result.Message,
                    status = result.Status,
                    rejectionReason = result.RejectionReason,
                    consignment = result.Consignment
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
        /// Cập nhật Quotation → ACCEPTED, Order → QUOTATION_CONFIRMED.
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

        /// <summary>
        /// Khách hàng xác nhận báo giá ký gửi và tạo thanh toán (payOS). Trả về link thanh toán cho FE.
        /// </summary>
        [HttpPut("api/quotations/{quotationId}/confirm-and-pay")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ConfirmQuotationAndPay(Guid quotationId, [FromBody] ConfirmQuotationPaymentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _consignmentPaymentService.ConfirmQuotationAndPayAsync(quotationId, userId, request);
                return Ok(new { message = "Xác nhận báo giá và tạo thanh toán thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Khách hàng xem thông tin báo giá mới nhất của đơn hàng.
        /// Trả về báo giá ở trạng thái SENT, PENDING, DRAFT hoặc ACCEPTED.
        /// </summary>
        [HttpGet("api/orders/{orderId}/quotation")]
        [ProducesResponseType(typeof(QuotationDetailResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetQuotationByOrderId(Guid orderId)
        {
            try
            {
                var result = await _quotationService.GetQuotationByOrderIdAsync(orderId);
                return Ok(new
                {
                    message = "Lấy thông tin báo giá thành công.",
                    data = result
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Xem thông tin báo giá theo ID báo giá.
        /// </summary>
        [HttpGet("api/quotations/{quotationId}")]
        [ProducesResponseType(typeof(QuotationDetailResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetQuotationById(Guid quotationId)
        {
            try
            {
                var result = await _quotationService.GetQuotationByIdAsync(quotationId);
                return Ok(new
                {
                    message = "Lấy thông tin báo giá thành công.",
                    data = result
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
