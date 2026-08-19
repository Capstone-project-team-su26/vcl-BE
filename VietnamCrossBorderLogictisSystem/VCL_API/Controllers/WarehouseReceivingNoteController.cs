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

        /// <summary>
        /// Khách tự tra phiếu tiếp nhận của đơn mình: kho đã nhận hàng chưa, đếm được bao nhiêu,
        /// có thiếu so với khai báo không.
        /// </summary>
        /// <remarks>
        /// Trả <c>data: null</c> khi đơn chưa có phiếu (chưa thanh toán, hoặc là đơn mua hộ).
        /// Xem đơn của người khác → 403.
        /// </remarks>
        [HttpGet("api/warehouse-receiving-notes/my-order/{consignmentOrderId}")]
        [Authorize]
        [ProducesResponseType(typeof(WarehouseReceivingNoteDetailDto), 200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetMyReceivingNote(Guid consignmentOrderId)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            try
            {
                var result = await _wrnService.GetMyReceivingNoteAsync(consignmentOrderId, userId);
                return Ok(new
                {
                    message = result == null
                        ? "Đơn hàng chưa có phiếu tiếp nhận kho."
                        : "Lấy phiếu tiếp nhận thành công.",
                    data = result,
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Danh sách phiếu tiếp nhận — hàng đợi việc của kho và màn duyệt của OM.
        /// Phiếu chờ duyệt luôn nằm trên đầu.
        /// </summary>
        /// <param name="status">ACTIVE · PARTIALLY_RECEIVED · RECEIVED · APPROVED · REJECTED</param>
        /// <param name="warehouseId">Lọc theo kho tiếp nhận</param>
        /// <param name="search">Tìm theo mã phiếu, mã đơn, tên hoặc số điện thoại khách</param>
        [HttpGet("api/warehouse-receiving-notes")]
        [Authorize(Roles = "Admin,ADMIN,Sale,SALE,Staff,STAFF,WarehouseStaff,WAREHOUSESTAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Manager,MANAGER")]
        [ProducesResponseType(typeof(PaginatedResponse<WarehouseReceivingNoteListItemDto>), 200)]
        public async Task<IActionResult> GetReceivingNotes(
            [FromQuery] string? status = null,
            [FromQuery] Guid? warehouseId = null,
            [FromQuery] string? search = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await _wrnService.GetReceivingNotesAsync(status, warehouseId, search, pageNumber, pageSize);
                return Ok(new { message = "Lấy danh sách phiếu tiếp nhận thành công.", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Operations Manager duyệt hoặc từ chối biên bản đối chiếu của phiếu tiếp nhận.
        /// Phiếu phải đang ở RECEIVED. Từ chối thì bắt buộc ghi lý do.
        /// </summary>
        /// <remarks>
        /// Duyệt xong kho mới xếp được hàng lên kệ — chốt nằm ở
        /// <c>PUT /api/parcels/{parcelId}/put-away</c>.
        /// </remarks>
        [HttpPut("api/warehouse-receiving-notes/{receivingNoteId}/status")]
        [Authorize]
        [ProducesResponseType(typeof(WarehouseReceivingNoteDetailDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateStatus(
            Guid receivingNoteId, [FromBody] UpdateReceivingNoteStatusDto request)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _wrnService.UpdateStatusAsync(receivingNoteId, userId, userRole, request);
                return Ok(new { message = "Cập nhật phiếu tiếp nhận thành công.", data = result });
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
        }
    }
}
