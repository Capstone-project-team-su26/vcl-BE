using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    /// <summary>
    /// API giám sát dòng tiền dành riêng cho role Admin
    /// </summary>
    [EnableCors("AllowAll")]
    [Route("api/admin/finance")]
    [ApiController]
    [Authorize(Roles = "Admin,ADMIN,Administrator,ADMINISTRATOR")]
    public class AdminFinanceController : ControllerBase
    {
        private readonly IAdminFinanceService _adminFinanceService;

        public AdminFinanceController(IAdminFinanceService adminFinanceService)
        {
            _adminFinanceService = adminFinanceService;
        }

        /// <summary>
        /// Lấy thống kê tổng quan dòng tiền (Tổng phải thu, Đã thu, Còn nợ, Số lượng đơn theo trạng thái)
        /// </summary>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetFinanceSummary([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            try
            {
                var result = await _adminFinanceService.GetFinanceSummaryAsync(from, to);
                return Ok(new
                {
                    message = "Lấy tổng quan dòng tiền thành công.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách công nợ theo từng đơn ký gửi (phân trang, lọc trạng thái thanh toán, từ khóa tìm kiếm)
        /// </summary>
        [HttpGet("orders")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetFinanceOrders([FromQuery] AdminFinanceOrderQueryDto query)
        {
            try
            {
                var result = await _adminFinanceService.GetFinanceOrdersAsync(query);
                return Ok(new
                {
                    message = "Lấy danh sách công nợ theo đơn thành công.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách lịch sử giao dịch thanh toán (phân trang, lọc khoảng thời gian)
        /// </summary>
        [HttpGet("transactions")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetFinanceTransactions([FromQuery] AdminFinanceTransactionQueryDto query)
        {
            try
            {
                var result = await _adminFinanceService.GetFinanceTransactionsAsync(query);
                return Ok(new
                {
                    message = "Lấy danh sách giao dịch thanh toán thành công.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Danh sách khoản tiền đang treo chờ Admin đối soát tay: khách chuyển khoản tay (OFFLINE →
        /// PENDING_RECONCILIATION) hoặc đã phát hành link cổng thanh toán mà webhook chưa về (PENDING).
        /// Gồm cả tiền ký gửi lẫn tiền mua hộ; lọc bằng ?source=CONSIGNMENT|PURCHASE.
        /// </summary>
        [HttpGet("transactions/pending-approval")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetPendingApprovalTransactions([FromQuery] AdminPendingTransactionQueryDto query)
        {
            try
            {
                var result = await _adminFinanceService.GetPendingManualTransactionsAsync(query);
                return Ok(new
                {
                    message = "Lấy danh sách giao dịch chờ duyệt thành công.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Admin xác nhận đã thấy tiền về trong sao kê: ghi nhận khoản đã thu và đẩy đơn đi tiếp,
        /// đúng như khi webhook cổng thanh toán bắn về.
        /// </summary>
        [HttpPut("transactions/{paymentId}/approve")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ApproveTransaction(Guid paymentId, [FromBody] ApproveManualTransactionRequest request)
        {
            try
            {
                var result = await _adminFinanceService.ApproveManualTransactionAsync(paymentId, request);
                return Ok(new
                {
                    message = "Duyệt giao dịch thủ công thành công.",
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
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Admin từ chối khoản treo (không thấy tiền về, khách khai sai...). Bắt buộc ghi lý do.
        /// </summary>
        [HttpPut("transactions/{paymentId}/reject")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RejectTransaction(Guid paymentId, [FromBody] RejectManualTransactionRequest request)
        {
            try
            {
                var result = await _adminFinanceService.RejectManualTransactionAsync(paymentId, request);
                return Ok(new
                {
                    message = "Đã từ chối giao dịch.",
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
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }
    }
}
