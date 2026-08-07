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
    [Route("api/purchase-requests")]
    [ApiController]
    [Authorize]
    public class PurchaseRequestController : ControllerBase
    {
        private readonly IPurchaseRequestService _purchaseRequestService;

        public PurchaseRequestController(IPurchaseRequestService purchaseRequestService)
        {
            _purchaseRequestService = purchaseRequestService;
        }

        /// <summary>
        /// Customer tạo yêu cầu mua hộ hàng quốc tế.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreatePurchaseRequest([FromBody] CreatePurchaseRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchaseRequestService.CreatePurchaseRequestAsync(userId, userRole, request);
                return Created(string.Empty, new { message = "Tạo yêu cầu mua hộ thành công.", data = result });
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
        /// Sales tạo yêu cầu mua hộ thay cho Customer.
        /// </summary>
        /// <param name="request">Thông tin khách hàng, tuyến hàng và danh sách sản phẩm cần mua hộ</param>
        /// <response code="201">Tạo yêu cầu mua hộ thành công</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc chứa hàng cấm</response>
        /// <response code="403">Không phải nhân viên Sales</response>
        /// <response code="404">Không tìm thấy Customer</response>
        [HttpPost("~/api/staff/purchase-requests")]
        [Authorize(Roles = "Sale")]
        public async Task<IActionResult> CreatePurchaseRequestByStaff([FromBody] CreatePurchaseRequestByStaffDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var staffUserId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng nhân viên." });
            }

            try
            {
                var result = await _purchaseRequestService.CreatePurchaseRequestByStaffAsync(staffUserId, request);
                return Created(string.Empty, new { message = "Tạo yêu cầu mua hộ thay khách hàng thành công.", data = result });
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
        /// Lấy danh sách yêu cầu mua hộ (Customer xem của mình, Staff/Sales xem tất cả)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPurchaseRequests(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null,
            [FromQuery] string? searchKeyword = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchaseRequestService.GetPurchaseRequestsAsync(userId, userRole, pageNumber, pageSize, status, searchKeyword);
                return Ok(new { message = "Lấy danh sách yêu cầu mua hộ thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Lấy chi tiết một yêu cầu mua hộ (Customer xem của mình, Staff/Sales xem tất cả)
        /// </summary>
        [HttpGet("{requestId}")]
        public async Task<IActionResult> GetPurchaseRequestDetail(Guid requestId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchaseRequestService.GetPurchaseRequestDetailAsync(requestId, userId, userRole);
                return Ok(new { message = "Lấy chi tiết yêu cầu mua hộ thành công.", data = result });
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
        /// Staff/Sales cập nhật trạng thái yêu cầu mua hộ (tiếp nhận, yêu cầu bổ sung, từ chối)
        /// </summary>
        [HttpPut("{requestId}/status")]
        public async Task<IActionResult> UpdatePurchaseRequestStatus(Guid requestId, [FromBody] UpdatePurchaseRequestStatusRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchaseRequestService.UpdatePurchaseRequestStatusAsync(requestId, userId, userRole, request);
                return Ok(new { message = "Cập nhật trạng thái yêu cầu mua hộ thành công.", data = result });
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
        /// Xác nhận mua hộ &amp; cập nhật tiến độ đơn mua hàng.
        /// Nhận PURCHASED, SELLER_SHIPPED hoặc ARRIVED_ORIGIN_WAREHOUSE, kèm tối đa 3 ảnh bằng chứng.
        /// </summary>
        [HttpPut("{requestId}/confirm-purchase")]
        public async Task<IActionResult> ConfirmPurchase(Guid requestId, [FromBody] ConfirmPurchaseRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchaseRequestService.ConfirmPurchaseAsync(requestId, userId, userRole, request);
                return Ok(new
                {
                    success = true,
                    message = "Xác nhận mua hộ & cập nhật tiến độ thành công.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { success = false, message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Nhật ký đổi trạng thái của một yêu cầu mua hộ (mới nhất trước).
        /// </summary>
        [HttpGet("{requestId}/history")]
        public async Task<IActionResult> GetHistory(Guid requestId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsed) ? parsed : null;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchaseRequestService.GetPurchaseRequestHistoryAsync(requestId, userId, userRole);
                return Ok(new { message = "Lấy lịch sử trạng thái thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Staff/Sales tạo báo giá cho một yêu cầu mua hộ
        /// </summary>
        [HttpPost("{requestId}/quotation")]
        public async Task<IActionResult> CreatePurchaseQuotation(Guid requestId, [FromBody] CreatePurchaseQuotationDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _purchaseRequestService.CreatePurchaseQuotationAsync(requestId, userId, userRole, dto);
                return Created(string.Empty, new { message = "Tạo báo giá mua hộ thành công.", data = result });
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
        /// Lấy báo giá hiện tại của một yêu cầu mua hộ
        /// </summary>
        [HttpGet("{requestId}/quotation")]
        public async Task<IActionResult> GetPurchaseQuotation(Guid requestId)
        {
            try
            {
                var result = await _purchaseRequestService.GetPurchaseQuotationByRequestIdAsync(requestId);
                return Ok(new { message = "Lấy báo giá mua hộ thành công.", data = result });
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
        /// Tính toán chi phí báo giá mua hộ tự động (Phí mua hộ % + Phí ship ước tính) trước khi tạo báo giá chính thức
        /// </summary>
        [HttpPost("{requestId}/calculate-fees")]
        public async Task<IActionResult> CalculatePurchaseQuotationFees(Guid requestId, [FromBody] CalculatePurchaseQuotationFeesRequestDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _purchaseRequestService.CalculatePurchaseQuotationFeesAsync(requestId, dto);
                return Ok(new { message = "Tính toán chi phí dự kiến thành công.", data = result });
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

