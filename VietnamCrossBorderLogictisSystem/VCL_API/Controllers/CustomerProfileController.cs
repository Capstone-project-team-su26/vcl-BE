using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/customer/profile")]
    [ApiController]
    [Authorize(Roles = "Customer")]
    public class CustomerProfileController : ControllerBase
    {
        private readonly ICustomerService _customerService;

        public CustomerProfileController(ICustomerService customerService)
        {
            _customerService = customerService;
        }

        private bool TryGetUserId(out Guid userId)
        {
            userId = Guid.Empty;
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out userId);
        }

        /// <summary>Customer xem hồ sơ cá nhân của mình.</summary>
        [HttpGet]
        public async Task<IActionResult> GetMyProfile()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                var data = await _customerService.GetMyProfileAsync(userId);
                return Ok(new { message = "Lấy hồ sơ cá nhân thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Customer cập nhật hồ sơ cá nhân (email và mã khách hàng không sửa được).</summary>
        [HttpPut]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateCustomerProfileRequestDto request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                var data = await _customerService.UpdateMyProfileAsync(userId, request);
                return Ok(new { message = "Cập nhật hồ sơ cá nhân thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }
    }
}
