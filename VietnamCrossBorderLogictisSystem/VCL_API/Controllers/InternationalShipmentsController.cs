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
    }
}
