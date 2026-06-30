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
    [Route("api/delivery-addresses")]
    [ApiController]
    [Authorize(Roles = "Customer")]
    public class DeliveryAddressController : ControllerBase
    {
        private readonly IDeliveryAddressService _service;

        public DeliveryAddressController(IDeliveryAddressService service)
        {
            _service = service;
        }

        private bool TryGetUserId(out Guid userId)
        {
            userId = Guid.Empty;
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out userId);
        }

        /// <summary>Lấy sổ địa chỉ nhận hàng của khách đang đăng nhập.</summary>
        [HttpGet]
        public async Task<IActionResult> GetMyAddresses()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                var data = await _service.GetMyAddressesAsync(userId);
                return Ok(data);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        /// <summary>Thêm địa chỉ nhận hàng mới.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDeliveryAddressRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                var created = await _service.CreateAsync(userId, request);
                return Ok(new { message = "Thêm địa chỉ thành công.", data = created });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        /// <summary>Cập nhật một địa chỉ nhận hàng.</summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeliveryAddressRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                await _service.UpdateAsync(userId, id, request);
                return Ok(new { message = "Cập nhật địa chỉ thành công." });
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

        /// <summary>Xóa một địa chỉ nhận hàng.</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                await _service.DeleteAsync(userId, id);
                return Ok(new { message = "Xóa địa chỉ thành công." });
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
    }
}
