using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

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
        private readonly VCL_BLL.Services.UserWarehouseService _userWarehouses;

        private Guid? CurrentUserId =>
            Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

        private string CurrentRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>Sale / OM: xem phiếu mọi kho, không áp phạm vi kho được gán.</summary>
        private static bool IsOfficeRole(string? role) =>
            role?.Replace(" ", string.Empty).Trim().ToUpperInvariant() is "SALE" or "OPERATIONSMANAGER" or "OPERATIONS";

        public WarehouseReceivingNoteController(
            IWarehouseReceivingNoteService wrnService,
            VCL_BLL.Services.UserWarehouseService userWarehouses)
        {
            _wrnService = wrnService;
            _userWarehouses = userWarehouses;
        }

        /// <summary>
        /// Lấy phiếu tiếp nhận kho đang hoạt động của đơn hàng ký gửi.
        /// </summary>
        [HttpGet("api/warehouse-receiving-notes/by-consignment/{consignmentOrderId}")]
        [AuthorizeRoles("Admin,Sale,WarehouseStaff,OperationsManager")]
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
        [AuthorizeRoles("WarehouseStaff,Admin")]
        public async Task<IActionResult> ScanReceivingNote([FromQuery] string code)
        {
            try
            {
                var result = await _wrnService.ScanReceivingNoteAsync(code, CurrentUserId, CurrentRole);
                return Ok(new { message = "Quét phiếu tiếp nhận kho thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                // Phiếu của kho không được gán cho tài khoản này.
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Sale lập phiếu nhập kho cho đơn khách đã thanh toán cọc (DEPOSIT_PAID). Phiếu chờ quản lý
        /// kho duyệt; duyệt xong khách mang phiếu và hàng tới kho.
        /// </summary>
        [HttpPost("api/warehouse-receiving-notes")]
        [Authorize(Roles = "Admin,Sale")]
        public async Task<IActionResult> CreateReceivingNote([FromBody] CreateWarehouseReceivingNoteRequestDto request)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var actorId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var result = await _wrnService.CreateReceivingNoteAsync(request, actorId);
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
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Lấy biên bản nhập kho đầy đủ, gồm các dòng đối chiếu số lượng và trọng lượng
        /// khách hàng khai báo với số liệu thực tế tại kho.
        /// </summary>
        [HttpGet("api/warehouse-receiving-notes/{receivingNoteId}")]
        [AuthorizeRoles("Admin,Sale,WarehouseStaff,OperationsManager")]
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
        [AuthorizeRoles("Admin,WarehouseStaff")]
        public async Task<IActionResult> ConfirmReceipt(Guid receivingNoteId, [FromBody] ConfirmReceiptRequestDto request)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var actorId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            // Nhân viên kho đã được gán kho chỉ xác nhận nhận hàng ở đúng kho của phiếu.
            try
            {
                await _userWarehouses.EnsureAssignedWarehouseAsync(
                    actorId,
                    User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
                    await _userWarehouses.WarehouseOfReceivingNoteAsync(receivingNoteId));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }

            try
            {
                var result = await _wrnService.ConfirmReceiptAsync(receivingNoteId, request, actorId);
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
                return BadRequest(new { message = this.UserError(ex) });
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
                return StatusCode(403, new { message = this.UserError(ex) });
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
        /// <param name="search">
        /// Tìm theo mã phiếu, mã đơn, tên hoặc số điện thoại khách; với hàng mua hộ còn tìm được theo
        /// mã đơn NCC, mã đơn mua (PO-), mã yêu cầu mua hộ và mã vận đơn nội địa (bỏ khoảng trắng,
        /// không phân biệt hoa thường).
        /// </param>
        /// <param name="orderType">CONSIGNMENT (ký gửi) / PURCHASE (mua hộ). Bỏ trống = tất cả.</param>
        [HttpGet("api/warehouse-receiving-notes")]
        [AuthorizeRoles("Admin,ADMIN,Sale,SALE,Staff,STAFF,WarehouseStaff,WAREHOUSESTAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Manager,MANAGER")]
        [ProducesResponseType(typeof(PaginatedResponse<WarehouseReceivingNoteListItemDto>), 200)]
        public async Task<IActionResult> GetReceivingNotes(
            [FromQuery] string? status = null,
            [FromQuery] Guid? warehouseId = null,
            [FromQuery] string? search = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? orderType = null)
        {
            try
            {
                // Nhân viên kho đã được gán kho chỉ thấy phiếu của các kho được gán; Sale / OM / Admin
                // xem toàn bộ như cũ. Tài khoản chưa gán kho giữ hành vi cũ.
                IReadOnlyCollection<Guid>? scope = null;
                if (!IsOfficeRole(CurrentRole))
                {
                    scope = await _userWarehouses.GetListScopeAsync(CurrentUserId, CurrentRole);
                    if (scope != null && warehouseId.HasValue && warehouseId != Guid.Empty && !scope.Contains(warehouseId.Value))
                    {
                        try
                        {
                            await _userWarehouses.EnsureAssignedWarehouseAsync(CurrentUserId, CurrentRole, warehouseId);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            return StatusCode(403, new { message = this.UserError(ex) });
                        }
                    }
                }

                var result = await _wrnService.GetReceivingNotesAsync(
                    status, warehouseId, search, pageNumber, pageSize, scope, orderType);
                return Ok(new { message = "Lấy danh sách phiếu tiếp nhận thành công.", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Quản lý kho / Operations Manager quyết định phiếu tiếp nhận ở một trong ba bước
        /// (<c>approvalStage</c> của danh sách): RECEIVE (duyệt cho nhận hàng), DISCREPANCY (biên bản lệch
        /// số lượng, phiếu ở RECEIVED/PARTIALLY_RECEIVED) và DISCREPANCY_ACK (phiếu đã tự chốt APPROVED
        /// nhưng lệch cân — chỉ nhận APPROVED = chấp nhận số thực tế). Từ chối thì bắt buộc ghi lý do.
        /// Quyết định lần hai trả 409.
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
        [ProducesResponseType(409)]
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
                return StatusCode(403, new { message = this.UserError(ex) });
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
                return BadRequest(new { message = this.UserError(ex) });
            }
        }
    }
}
