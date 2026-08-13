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
    /// Yêu cầu giao hàng chặng cuối: sale lập, OM duyệt, kho gom hàng rồi đặt đơn vị giao.
    /// Nhận cả kiện giao thẳng lẫn kiện khách gửi lại kho VN.
    /// </summary>
    [Route("api/delivery-requests")]
    [ApiController]
    [Authorize]
    public class DeliveryRequestController : ControllerBase
    {
        private readonly IDeliveryRequestService _deliveryService;

        public DeliveryRequestController(IDeliveryRequestService deliveryService)
        {
            _deliveryService = deliveryService;
        }

        private bool TryGetUser(out Guid userId, out string userRole)
        {
            userId = Guid.Empty;
            userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out userId);
        }

        private IActionResult Handle(Exception ex) => ex switch
        {
            UnauthorizedAccessException => StatusCode(403, new { message = ex.Message }),
            NotFoundException => NotFound(new { message = ex.Message }),
            ArgumentException => BadRequest(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message })
        };

        /// <summary>Sale lập yêu cầu giao hàng cho đơn đã tất toán.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDeliveryRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUser(out var userId, out var userRole))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var result = await _deliveryService.CreateAsync(userId, userRole, request);
                return StatusCode(201, new { message = "Lập yêu cầu giao hàng thành công.", data = result });
            }
            catch (Exception ex) { return Handle(ex); }
        }

        /// <summary>OM duyệt hoặc từ chối. Từ chối thì bắt buộc ghi lý do.</summary>
        [HttpPut("{deliveryRequestId}/status")]
        public async Task<IActionResult> UpdateStatus(Guid deliveryRequestId, [FromBody] UpdateDeliveryRequestStatusDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUser(out var userId, out var userRole))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var result = await _deliveryService.UpdateStatusAsync(deliveryRequestId, userId, userRole, request);
                return Ok(new { message = "Cập nhật yêu cầu giao hàng thành công.", data = result });
            }
            catch (Exception ex) { return Handle(ex); }
        }

        /// <summary>
        /// Kho gom hàng xong bấm đặt đơn vị giao. Phiếu phải được OM duyệt trước.
        /// Kiện nhặt trên kệ sẽ được nhả ô và dừng tính phí lưu kho ở bước này.
        /// </summary>
        [HttpPost("{deliveryRequestId}/dispatch")]
        public async Task<IActionResult> Dispatch(Guid deliveryRequestId, [FromBody] DispatchDeliveryRequestDto? request = null)
        {
            if (!TryGetUser(out var userId, out var userRole))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var result = await _deliveryService.DispatchAsync(deliveryRequestId, userId, userRole, request ?? new DispatchDeliveryRequestDto());
                return Ok(new { message = "Đặt đơn vị giao hàng thành công.", data = result });
            }
            catch (Exception ex) { return Handle(ex); }
        }

        /// <summary>Danh sách yêu cầu giao hàng, lọc theo trạng thái hoặc theo đơn.</summary>
        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] string? status = null, [FromQuery] Guid? orderId = null)
        {
            try
            {
                var result = await _deliveryService.GetListAsync(status, orderId);
                return Ok(new { message = "Lấy danh sách yêu cầu giao hàng thành công.", data = new { items = result, totalCount = result.Count } });
            }
            catch (Exception ex) { return Handle(ex); }
        }

        /// <summary>Chi tiết một yêu cầu giao hàng, kèm danh sách kiện và nơi nhặt hàng.</summary>
        [HttpGet("{deliveryRequestId}")]
        public async Task<IActionResult> GetById(Guid deliveryRequestId)
        {
            try
            {
                var result = await _deliveryService.GetByIdAsync(deliveryRequestId);
                return Ok(new { message = "Lấy chi tiết yêu cầu giao hàng thành công.", data = result });
            }
            catch (Exception ex) { return Handle(ex); }
        }
    }
}
