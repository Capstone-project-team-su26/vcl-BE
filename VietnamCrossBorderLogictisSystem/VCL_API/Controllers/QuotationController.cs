using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_BLL.DTOs;
using VCL_API.Infrastructure;

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
        [Authorize(Roles = "Admin,ADMIN,Administrator,Sale,SALE,Sales,SALES")]
        [ProducesResponseType(typeof(QuotationActionResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> SendQuotation(Guid orderId, [FromBody] CreateQuotationRequest request)
        {
            if (!TryGetActor(out var actorId, out _))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _quotationService.SendQuotationAsync(orderId, request, actorId);
                return Ok(new
                {
                    message = result.Message,
                    status = result.Status,
                    consignment = result.Consignment
                });
            }
            catch (ConflictException ex)
            {
                // Báo giá đã tới tay khách (hoặc Admin đã duyệt) — không lập đè.
                return Conflict(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Từ chối báo giá ký gửi theo đơn hàng. Chỉ khách (chủ đơn) hoặc Admin; Sale/nhân viên khác → 403.
        /// Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        [HttpPost("api/orders/{orderId}/quotation/reject")]
        [ProducesResponseType(typeof(QuotationActionResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RejectQuotationByOrderId(Guid orderId, [FromBody] RejectQuotationRequest request)
        {
            if (!TryGetActor(out var actorId, out var actorRole))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _quotationService.RejectQuotationByOrderIdAsync(orderId, request?.RejectionReason ?? string.Empty, actorId, actorRole);
                return Ok(new
                {
                    message = result.Message,
                    status = result.Status,
                    rejectionReason = result.RejectionReason,
                    consignment = result.Consignment
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Từ chối báo giá ký gửi theo ID báo giá. Chỉ khách (chủ đơn) hoặc Admin; Sale/nhân viên khác → 403.
        /// Cập nhật Order → QUOTATION_REJECTED.
        /// </summary>
        [HttpPut("api/quotations/{quotationId}/reject")]
        [ProducesResponseType(typeof(QuotationActionResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RejectQuotationById(Guid quotationId, [FromBody] RejectQuotationRequest request)
        {
            if (!TryGetActor(out var actorId, out var actorRole))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _quotationService.RejectQuotationByIdAsync(quotationId, request?.RejectionReason ?? string.Empty, actorId, actorRole);
                return Ok(new
                {
                    message = result.Message,
                    status = result.Status,
                    rejectionReason = result.RejectionReason,
                    consignment = result.Consignment
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
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
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
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
            if (!TryGetActor(out var actorId, out var actorRole))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _quotationService.GetQuotationByOrderIdAsync(orderId, actorId, actorRole);
                return Ok(new
                {
                    message = "Lấy thông tin báo giá thành công.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
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
            if (!TryGetActor(out var actorId, out var actorRole))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _quotationService.GetQuotationByIdAsync(quotationId, actorId, actorRole);
                return Ok(new
                {
                    message = "Lấy thông tin báo giá thành công.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    
        /// <summary>
        /// Admin duyệt hoặc từ chối giá ngoại lệ của báo giá (Sales nhập giá khác bảng giá).
        /// Duyệt xong báo giá mới được gửi tới khách. Người lập báo giá không tự duyệt.
        /// </summary>
        [HttpPut("api/quotations/{quotationId}/price-approval")]
        [Authorize(Roles = "Admin,ADMIN,Administrator,ADMINISTRATOR")]
        [ProducesResponseType(typeof(QuotationActionResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DecidePriceOverride(Guid quotationId, [FromBody] QuotationPriceDecisionRequest request)
        {
            if (!TryGetActor(out var actorId, out var actorRole))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _quotationService.DecidePriceOverrideAsync(quotationId, actorId, actorRole, request);
                return Ok(new
                {
                    message = result.Message,
                    status = result.Status,
                    rejectionReason = result.RejectionReason,
                    consignment = result.Consignment
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        private bool TryGetActor(out Guid actorId, out string actorRole)
        {
            actorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out actorId);
        }
    }
}
