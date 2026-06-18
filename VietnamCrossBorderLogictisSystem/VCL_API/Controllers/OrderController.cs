using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Interface;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/orders")]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        /// <summary>
        /// Khách hàng tạo yêu cầu ký gửi (A1 + A2).
        /// Tạo Order + OrderItems, đối chiếu hàng cấm/hạn chế, và sinh báo giá tạm tính ngay.
        /// </summary>
        /// <param name="request">Thông tin mặt hàng và phương án vận chuyển TQ → VN</param>
        /// <returns>Thông tin yêu cầu ký gửi kèm báo giá tạm tính và cảnh báo (nếu có)</returns>
        /// <response code="201">Tạo yêu cầu ký gửi thành công</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc chứa hàng cấm</response>
        /// <response code="401">Chưa đăng nhập</response>
        [HttpPost("consignments")]
        [Authorize(Roles = "Customer")]
        [ProducesResponseType(typeof(object), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> CreateConsignment([FromBody] CreateConsignmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _orderService.CreateConsignmentAsync(request, userId);
                return CreatedAtAction(nameof(GetConsignments), new { },
                    new { message = "Tạo yêu cầu ký gửi thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Khách hàng hủy yêu cầu ký gửi của chính mình.
        /// Chỉ hủy được khi hàng chưa vào kho VN (PENDING_REVIEW, APPROVED, WAITING_FOR_PARCEL).
        /// Tự động hủy các báo giá liên quan.
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <param name="request">Lý do hủy (tùy chọn)</param>
        /// <returns>Kết quả hủy</returns>
        /// <response code="200">Hủy thành công</response>
        /// <response code="400">Trạng thái không cho phép hủy</response>
        /// <response code="401">Chưa đăng nhập hoặc không phải chủ đơn</response>
        /// <response code="404">Không tìm thấy yêu cầu ký gửi</response>
        [HttpPut("consignments/{orderId}/cancel")]
        [Authorize(Roles = "Customer")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CancelConsignment(Guid orderId, [FromBody] CancelConsignmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _orderService.CancelConsignmentAsync(orderId, userId, request ?? new CancelConsignmentRequest());
                return Ok(new
                {
                    message = "Hủy yêu cầu ký gửi thành công.",
                    status = result.Status,
                    consignmentCode = result.ConsignmentCode,
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
        /// Staff cập nhật trạng thái yêu cầu ký gửi
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <param name="request">Thông tin trạng thái cần cập nhật và lý do từ chối (nếu có)</param>
        /// <returns>Kết quả cập nhật trạng thái</returns>
        [HttpPut("consignments/{orderId}/status")]
        [Authorize(Roles = "Admin,Sale,WarehouseStaff,OperationsManager,Delivery")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateConsignmentStatus(Guid orderId, [FromBody] UpdateConsignmentStatusRequest request)
        {
            try
            {
                var result = await _orderService.UpdateConsignmentStatusAsync(orderId, request);
                return Ok(new 
                { 
                    message = "Cập nhật trạng thái thành công.",
                    status = result.Status,
                    consignmentCode = result.ConsignmentCode
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
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Xem chi tiết một yêu cầu ký gửi.
        /// Customer chỉ xem được đơn của chính mình; các role nội bộ xem được mọi đơn.
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <returns>Chi tiết đầy đủ: header + khách hàng + mặt hàng + báo giá mới nhất</returns>
        /// <response code="200">Lấy chi tiết thành công</response>
        /// <response code="401">Chưa đăng nhập hoặc không phải chủ đơn</response>
        /// <response code="404">Không tìm thấy yêu cầu ký gửi</response>
        [HttpGet("consignments/{orderId}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetConsignmentDetail(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedId))
                userId = parsedId;

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _orderService.GetConsignmentDetailAsync(orderId, userId, role);
                return Ok(new { message = "Lấy chi tiết yêu cầu ký gửi thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách yêu cầu ký gửi
        /// </summary>
        /// <param name="pageNumber">Số trang (mặc định 1)</param>
        /// <param name="pageSize">Số lượng item mỗi trang (mặc định 10)</param>
        /// <param name="status">Trạng thái yêu cầu (tuỳ chọn)</param>
        /// <param name="searchCode">Mã yêu cầu ký gửi để tìm kiếm (tuỳ chọn)</param>
        /// <returns>Danh sách yêu cầu ký gửi phân trang</returns>
        [HttpGet("consignments")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetConsignments(
            [FromQuery] int pageNumber = 1, 
            [FromQuery] int pageSize = 10, 
            [FromQuery] string? status = null, 
            [FromQuery] string? searchCode = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _orderService.GetConsignmentsAsync(userId, role, pageNumber, pageSize, status, searchCode);
                return Ok(new
                {
                    message = "Lấy danh sách yêu cầu ký gửi thành công.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


    }
}
