using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/customer/parcels")]
    [ApiController]
    [Authorize(Roles = "Customer")]
    public class CustomerParcelController : ControllerBase
    {
        private readonly IParcelService _parcelService;

        public CustomerParcelController(IParcelService parcelService)
        {
            _parcelService = parcelService;
        }

        /// <summary>
        /// Customer tra cứu danh sách kiện hàng thuộc yêu cầu ký gửi của mình (sau khi kho check-in / put-away).
        /// </summary>
        /// <param name="consignmentCode">Lọc theo mã yêu cầu ký gửi (tùy chọn).</param>
        /// <param name="searchCode">Tìm theo mã kiện hàng (tùy chọn)</param>
        [HttpGet]
        public async Task<IActionResult> GetMyParcels(
            [FromQuery] string? consignmentCode = null,
            [FromQuery] string? searchCode = null)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            try
            {
                var data = await _parcelService.GetCustomerParcelsAsync(userId, consignmentCode, searchCode);
                return Ok(new { message = "Lấy danh sách kiện hàng thành công.", data });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }
    }
}
