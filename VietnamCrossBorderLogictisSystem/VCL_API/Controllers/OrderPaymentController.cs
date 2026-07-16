using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Authorize]
    public class OrderPaymentController : ControllerBase
    {
        private readonly IConsignmentPaymentService _consignmentPaymentService;

        public OrderPaymentController(IConsignmentPaymentService consignmentPaymentService)
        {
            _consignmentPaymentService = consignmentPaymentService;
        }

        /// <summary>
        /// Sale tạo thanh toán cuối (phần còn lại + phí phát sinh) cho đơn ký gửi
        /// khi kiện hàng đã về đến kho đích (RECEIVED_AT_DESTINATION).
        /// </summary>
        [HttpPost("api/orders/{orderId}/payments/final")]
        public async Task<IActionResult> CreateFinalPayment(Guid orderId, [FromBody] CreateFinalPaymentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _consignmentPaymentService.CreateFinalPaymentAsync(orderId, userId, userRole, request);
                return Ok(new { message = "Tạo thanh toán cuối thành công.", data = result });
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
        /// Lấy lịch sử/danh sách các đợt thanh toán của đơn ký gửi (cọc + thanh toán cuối).
        /// </summary>
        [HttpGet("api/orders/{orderId}/payments")]
        public async Task<IActionResult> GetOrderPayments(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _consignmentPaymentService.GetOrderPaymentsAsync(orderId, userId, userRole);
                return Ok(new { message = "Lấy danh sách thanh toán của đơn thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy lịch sử thanh toán chi tiết của đơn ký gửi (kèm thông tin Customer, báo giá,
        /// từng đợt thanh toán với trạng thái PENDING/SUCCESS/FAILED/CANCELED). Customer/Sales/Admin.
        /// </summary>
        [HttpGet("api/orders/{orderId}/payments/history")]
        public async Task<IActionResult> GetPaymentHistory(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _consignmentPaymentService.GetPaymentHistoryAsync(orderId, userId, userRole);
                return Ok(new { message = "Lấy lịch sử thanh toán của đơn thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }
    }
}
