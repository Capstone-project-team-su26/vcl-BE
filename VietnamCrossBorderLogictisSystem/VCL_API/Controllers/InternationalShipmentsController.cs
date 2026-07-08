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
    [Route("api/international-shipments")]
    [ApiController]
    [Authorize]
    public class InternationalShipmentsController : ControllerBase
    {
        private readonly IInternationalShipmentService _shipmentService;

        public InternationalShipmentsController(IInternationalShipmentService shipmentService)
        {
            _shipmentService = shipmentService;
        }

        /// <summary>
        /// Staff/Operations tạo lô vận chuyển quốc tế từ các yêu cầu xuất kho đã RELEASED
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateInternationalShipment([FromBody] CreateInternationalShipmentDto request)
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
                var result = await _shipmentService.CreateAsync(userId, userRole, request);
                return Created(string.Empty, new { message = "Tạo lô vận chuyển quốc tế thành công.", data = result });
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
        /// Staff/Operations cập nhật trạng thái lô vận chuyển quốc tế (MANIFESTED → IN_TRANSIT → ARRIVED_DESTINATION)
        /// </summary>
        [HttpPut("{shipmentId}/status")]
        public async Task<IActionResult> UpdateStatus(Guid shipmentId, [FromBody] UpdateInternationalShipmentStatusDto request)
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
                var result = await _shipmentService.UpdateStatusAsync(shipmentId, userId, userRole, request);
                return Ok(new { message = "Cập nhật trạng thái lô vận chuyển thành công.", data = result });
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
        /// Staff/Operations xác nhận lô vận chuyển quốc tế đã đến kho đích (chỉ khi đang IN_TRANSIT).
        /// Cập nhật lô + các kiện sang ARRIVED_DESTINATION để sẵn sàng cho quy trình nhập kho.
        /// </summary>
        [HttpPut("{shipmentId}/arrive")]
        public async Task<IActionResult> ConfirmArrival(Guid shipmentId, [FromBody] ConfirmShipmentArrivalDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _shipmentService.ConfirmArrivalAsync(shipmentId, userId, userRole, request ?? new ConfirmShipmentArrivalDto());
                return Ok(new { message = "Xác nhận lô đến kho đích thành công.", data = result });
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
        /// Warehouse Staff/Staff/Operations xác nhận nhận kiện từ lô tại kho đích (chỉ khi lô đã ARRIVED_DESTINATION).
        /// Cập nhật các Parcel sang RECEIVED_AT_DESTINATION để tiếp tục nhập kho / put-away.
        /// </summary>
        [HttpPut("{shipmentId}/destination-receiving")]
        public async Task<IActionResult> ConfirmDestinationReceiving(Guid shipmentId, [FromBody] DestinationReceivingDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _shipmentService.ConfirmDestinationReceivingAsync(shipmentId, userId, userRole, request ?? new DestinationReceivingDto());
                return Ok(new { message = "Xác nhận nhận kiện tại kho đích thành công.", data = result });
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
    }
}
