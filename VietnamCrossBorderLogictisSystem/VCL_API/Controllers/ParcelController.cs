using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/parcels")]
    [ApiController]
    [Authorize(Roles = "Staff,WarehouseStaff")]
    public class ParcelController : ControllerBase
    {
        private readonly IParcelService _parcelService;

        public ParcelController(IParcelService parcelService)
        {
            _parcelService = parcelService;
        }

        /// <summary>
        /// Check-in kiện hàng bằng mã gửi hàng
        /// </summary>
        /// <param name="request">Thông tin mã gửi hàng, cân nặng và kích thước thực tế</param>
        /// <returns>Thông tin Parcel đã check-in thành công</returns>
        /// <response code="200">Check-in thành công</response>
        /// <response code="400">Dữ liệu yêu cầu không hợp lệ hoặc mã gửi hàng không đúng định dạng</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="403">Không có quyền thao tác (yêu cầu role Staff hoặc WarehouseStaff)</response>
        /// <response code="404">Không tìm thấy yêu cầu ký gửi tương ứng</response>
        [HttpPost("check-in")]
        [ProducesResponseType(typeof(ParcelCheckInResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CheckIn([FromBody] ParcelCheckInRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _parcelService.CheckInParcelAsync(request);
                return Ok(new { message = "Check-in kiện hàng thành công.", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
