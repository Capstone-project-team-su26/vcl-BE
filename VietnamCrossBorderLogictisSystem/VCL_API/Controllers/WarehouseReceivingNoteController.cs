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
    // Chỉ yêu cầu đăng nhập ở cấp controller; vai trò khai riêng từng action.
    //
    // Trước đây cấp controller khai [Authorize(Roles = "Admin,Sale")]. ASP.NET Core cộng dồn các
    // attribute Authorize theo kiểu VÀ, nên action khai "WarehouseStaff,Admin" hoá ra đòi
    // (WarehouseStaff hoặc Admin) VÀ (Admin hoặc Sale) — tức chỉ Admin lọt. Nhân viên kho vì thế
    // không quét được phiếu lẫn không xác nhận nhận hàng được, cả nhánh kho của luồng đứng im.
    [Authorize]
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
        [Authorize(Roles = "Admin,Sale,WarehouseStaff,OperationsManager")]
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
        /// Warehouse Staff scan mã phiếu tiếp nhận kho (mobile) để lấy thông tin Customer,
        /// yêu cầu ký gửi và danh sách hàng dự kiến nhận.
        /// </summary>
        [HttpGet("api/warehouse-receiving-notes/scan")]
        [Authorize(Roles = "WarehouseStaff,Admin")]
        public async Task<IActionResult> ScanReceivingNote([FromQuery] string code)
        {
            try
            {
                var result = await _wrnService.ScanReceivingNoteAsync(code);
                return Ok(new { message = "Quét phiếu tiếp nhận kho thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo mới phiếu tiếp nhận kho cho đơn ký gửi đang ở APPROVED, DEPOSIT_PAID hoặc
        /// PAYMENT_CONFIRMED — tức đơn nhân viên tạo, hoặc đơn khách đã trả cọc.
        /// </summary>
        [HttpPost("api/warehouse-receiving-notes")]
        [Authorize(Roles = "Admin,Sale")]
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

        /// <summary>
        /// Lấy biên bản nhập kho đầy đủ, gồm các dòng đối chiếu số lượng và trọng lượng
        /// khách hàng khai báo với số liệu thực tế tại kho.
        /// </summary>
        [HttpGet("api/warehouse-receiving-notes/{receivingNoteId}")]
        [Authorize(Roles = "Admin,Sale,WarehouseStaff,OperationsManager")]
        public async Task<IActionResult> GetReceivingNoteDetail(Guid receivingNoteId)
        {
            try
            {
                var result = await _wrnService.GetReceivingNoteDetailAsync(receivingNoteId);
                return Ok(new { message = "Lấy biên bản nhập kho thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Nhân viên kho xác nhận nhập kho: nhập số lượng và trọng lượng thực tế của từng dòng hàng.
        /// Hệ thống tự tính chênh lệch so với khai báo và chuyển phiếu sang trạng thái RECEIVED.
        /// </summary>
        [HttpPost("api/warehouse-receiving-notes/{receivingNoteId}/confirm-receipt")]
        [Authorize(Roles = "Admin,WarehouseStaff")]
        public async Task<IActionResult> ConfirmReceipt(Guid receivingNoteId, [FromBody] ConfirmReceiptRequestDto request)
        {
            try
            {
                var result = await _wrnService.ConfirmReceiptAsync(receivingNoteId, request);
                return Ok(new { message = "Xác nhận nhập kho thành công.", data = result });
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
