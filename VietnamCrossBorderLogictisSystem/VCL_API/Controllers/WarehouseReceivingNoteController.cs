using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin,Sale")]
    public class WarehouseReceivingNoteController : ControllerBase
    {
        private readonly IWarehouseReceivingNoteService _wrnService;

        public WarehouseReceivingNoteController(IWarehouseReceivingNoteService wrnService)
        {
            _wrnService = wrnService;
        }

        /// <summary>
        /// Lấy phiếu tiếp nhận kho đang hoạt động của đơn hàng ký gửi.
        /// </summary>
        [HttpGet("api/warehouse-receiving-notes/by-consignment/{consignmentOrderId}")]
        public async Task<IActionResult> GetReceivingNoteByConsignment(Guid consignmentOrderId)
        {
            try
            {
                var result = await _wrnService.GetReceivingNoteByConsignmentIdAsync(consignmentOrderId);
                return Ok(result);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo mới phiếu tiếp nhận kho cho đơn hàng ký gửi đã được duyệt (APPROVED).
        /// </summary>
        [HttpPost("api/warehouse-receiving-notes")]
        public async Task<IActionResult> CreateReceivingNote([FromBody] CreateWarehouseReceivingNoteRequestDto request)
        {
            try
            {
                var result = await _wrnService.CreateReceivingNoteAsync(request);
                return StatusCode(201, new
                {
                    message = "Gửi phiếu tiếp nhận kho thành công.",
                    receivingNote = result
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ConflictException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
