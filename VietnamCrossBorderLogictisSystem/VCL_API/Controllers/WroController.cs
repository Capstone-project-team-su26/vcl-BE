using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [EnableCors("AllowAll")]
    [Route("api/warehouse-release-requests")]
    [ApiController]
    [Authorize]
    public class WroController : ControllerBase
    {
        private readonly IWroService _wroService;

        public WroController(IWroService wroService)
        {
            _wroService = wroService;
        }

        /// <summary>
        /// Lấy danh sách yêu cầu xuất kho (WRO) có phân trang và bộ lọc
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetWroRequests([FromQuery] GetWroListRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _wroService.GetWroRequestsAsync(userId, userRole, request);
                return Ok(new { message = "Lấy danh sách yêu cầu xuất kho thành công.", data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// TH1: Tạo phiếu yêu cầu xuất kho lẻ đi theo Đơn hàng (Hỏa tốc, lẻ, 1 đơn riêng biệt).
        /// </summary>
        [HttpPost("single")]
        public async Task<IActionResult> CreateSingleWroRequest([FromBody] CreateSingleWroRequestDto request)
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
                var result = await _wroService.CreateSingleWroRequestAsync(userId, userRole, request);
                return Created(string.Empty, new { message = "Tạo yêu cầu xuất kho lẻ thành công.", data = result });
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
        /// TH2: Tạo phiếu yêu cầu xuất kho gom lô (nhiều kiện của nhiều đơn hàng khác nhau).
        /// </summary>
        [HttpPost("batch")]
        public async Task<IActionResult> CreateBatchWroRequest([FromBody] CreateBatchWroRequestDto request)
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
                var result = await _wroService.CreateBatchWroRequestAsync(userId, userRole, request);
                return Created(string.Empty, new { message = "Tạo yêu cầu xuất kho gom lô thành công.", data = result });
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
        /// Tạo yêu cầu xuất kho linh hoạt từ tồn kho khả dụng (WRO)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateWroRequest([FromBody] CreateWroRequestDto request)
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
                var result = await _wroService.CreateWroRequestAsync(userId, userRole, request);
                return Created(string.Empty, new { message = "Tạo yêu cầu xuất kho thành công.", data = result });
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
        /// Lấy chi tiết một yêu cầu xuất kho (WRO)
        /// </summary>
        [HttpGet("{requestId}")]
        public async Task<IActionResult> GetWroRequestDetail(Guid requestId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _wroService.GetWroRequestDetailAsync(requestId, userId, userRole);
                return Ok(new { message = "Lấy chi tiết yêu cầu xuất kho thành công.", data = result });
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
        /// Cập nhật trạng thái yêu cầu xuất kho (Staff duyệt/từ chối WRO)
        /// </summary>
        [HttpPut("{requestId}/status")]
        public async Task<IActionResult> UpdateWroStatus(Guid requestId, [FromBody] UpdateWroStatusRequestDto request)
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
                var result = await _wroService.UpdateWroStatusAsync(requestId, userId, userRole, request);
                return Ok(new { message = "Cập nhật trạng thái yêu cầu xuất kho thành công.", data = result });
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
        /// Tạo phiếu picking cho yêu cầu xuất kho đã được duyệt
        /// </summary>
        [HttpPost("{requestId}/picking-list")]
        public async Task<IActionResult> CreatePickingList(Guid requestId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _wroService.CreatePickingListAsync(requestId, userId, userRole);
                return StatusCode(201, new { message = "Tạo phiếu picking thành công.", data = result });
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
        /// Xác nhận đóng gói và hoàn tất xuất kho (Warehouse Staff, Staff hoặc Operations)
        /// </summary>
        [HttpPut("{requestId}/complete")]
        [Authorize(Roles = "WarehouseStaff,Staff,OperationsManager,Operations,Admin")]
        public async Task<IActionResult> CompleteWroRequest(Guid requestId, [FromBody] CompleteWroRequestDto request)
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
                var result = await _wroService.CompleteWroRequestAsync(requestId, userId, userRole, request);
                return Ok(new { message = "Hoàn tất xuất kho thành công.", data = result });
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
        /// Gán phương án, tuyến vận chuyển quốc tế &amp; đơn vị vận chuyển (Operations Manager / Operations / Admin)
        /// </summary>
        [HttpPut("{requestId}/shipping-route")]
        [Authorize(Roles = "OperationsManager,Operations,Admin,Staff,WarehouseStaff")]
        public async Task<IActionResult> AssignShippingRoute(Guid requestId, [FromBody] AssignShippingRouteRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _wroService.AssignShippingRouteAsync(requestId, userId, userRole, request);
                return Ok(new { message = "Cập nhật phương án & tuyến vận chuyển thành công.", data = result });
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
        /// Cập nhật thông tin đóng gói, cân đo trọng lượng/kích thước và ảnh chụp (Warehouse Staff / Staff / Operations)
        /// </summary>
        [HttpPut("{requestId}/packing")]
        [Authorize(Roles = "WarehouseStaff,Staff,OperationsManager,Operations,Admin")]
        public async Task<IActionResult> UpdatePackingDetails(Guid requestId, [FromBody] UpdatePackingDetailsRequestDto request)
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
                var result = await _wroService.UpdatePackingDetailsAsync(requestId, userId, userRole, request);
                return Ok(new { message = "Xác nhận đóng gói thành công.", data = result });
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
        /// Bàn giao kiện hàng xuất kho cho đơn vị vận chuyển (Warehouse Staff / Staff / Operations)
        /// </summary>
        [HttpPost("{requestId}/handover")]
        [Authorize(Roles = "WarehouseStaff,Staff,OperationsManager,Operations,Admin")]
        public async Task<IActionResult> HandoverToCarrier(Guid requestId, [FromBody] ReleaseHandoverRequestDto request)
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
                var result = await _wroService.HandoverToCarrierAsync(requestId, userId, userRole, request);
                return Ok(new { message = "Bàn giao cho đơn vị vận chuyển thành công.", data = result });
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

