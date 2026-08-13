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
    /// <summary>
    /// Phiếu nhập kho tại kho VN — dành cho hàng khách xin gửi lại thay vì nhận ngay.
    /// Kho lập phiếu, OM duyệt, rồi kho mới xếp hàng lên kệ được.
    /// </summary>
    [Route("api/warehouse-inbound-requests")]
    [ApiController]
    [Authorize]
    public class WarehouseInboundController : ControllerBase
    {
        private readonly IWarehouseInboundService _inboundService;

        public WarehouseInboundController(IWarehouseInboundService inboundService)
        {
            _inboundService = inboundService;
        }

        private bool TryGetUser(out Guid userId, out string userRole)
        {
            userId = Guid.Empty;
            userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out userId);
        }

        /// <summary>
        /// Kho VN lập phiếu nhập kho cho những kiện khách xin gửi lại.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateWarehouseInboundRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUser(out var userId, out var userRole))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var result = await _inboundService.CreateAsync(userId, userRole, request);
                return StatusCode(201, new { message = "Lập phiếu nhập kho thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex) { return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message }); }
        }

        /// <summary>
        /// OM duyệt hoặc từ chối phiếu nhập kho. Từ chối thì bắt buộc ghi lý do.
        /// </summary>
        [HttpPut("{inboundRequestId}/status")]
        public async Task<IActionResult> UpdateStatus(Guid inboundRequestId, [FromBody] UpdateWarehouseInboundStatusDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUser(out var userId, out var userRole))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var result = await _inboundService.UpdateStatusAsync(inboundRequestId, userId, userRole, request);
                return Ok(new { message = "Cập nhật phiếu nhập kho thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex) { return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message }); }
        }

        /// <summary>
        /// Danh sách phiếu nhập kho, lọc theo trạng thái hoặc theo lô vận chuyển.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] string? status = null, [FromQuery] Guid? shipmentId = null)
        {
            try
            {
                var result = await _inboundService.GetListAsync(status, shipmentId);
                return Ok(new { message = "Lấy danh sách phiếu nhập kho thành công.", data = new { items = result, totalCount = result.Count } });
            }
            catch (Exception ex) { return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message }); }
        }

        /// <summary>
        /// Chi tiết một phiếu nhập kho, kèm danh sách kiện và vị trí ô kệ nếu đã xếp.
        /// </summary>
        [HttpGet("{inboundRequestId}")]
        public async Task<IActionResult> GetById(Guid inboundRequestId)
        {
            try
            {
                var result = await _inboundService.GetByIdAsync(inboundRequestId);
                return Ok(new { message = "Lấy chi tiết phiếu nhập kho thành công.", data = result });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (Exception ex) { return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message }); }
        }
    }
}
