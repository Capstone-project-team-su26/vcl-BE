using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_BLL.Services;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/parcels")]
    [ApiController]
    [Authorize]
    public class ParcelController : ControllerBase
    {
        private readonly IParcelService _parcelService;
        private readonly UserWarehouseService _userWarehouses;

        public ParcelController(IParcelService parcelService, UserWarehouseService userWarehouses)
        {
            _parcelService = parcelService;
            _userWarehouses = userWarehouses;
        }

        private Guid? CurrentUserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        private string CurrentRole => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>Chặn thao tác ở kho ngoài các kho được gán (tài khoản chưa gán kho thì như cũ).</summary>
        private async Task<IActionResult?> DenyOutsideAssignedAsync(Guid? warehouseId)
        {
            try
            {
                await _userWarehouses.EnsureAssignedWarehouseAsync(CurrentUserId, CurrentRole, warehouseId);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
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
        [AuthorizeRoles("Admin,ADMIN,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
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
                var actorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                Guid? actorUserId = Guid.TryParse(actorIdClaim, out var parsedActorId) ? parsedActorId : null;

                var result = await _parcelService.CheckInParcelAsync(request, actorUserId);
                return Ok(new { message = "Check-in kiện hàng thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Lấy chi tiết một kiện hàng trong kho theo parcelId.
        /// Trả về thông tin kiện, yêu cầu ký gửi và khách hàng liên quan, cân đo thực tế, và vị trí kho nếu đã put-away.
        /// </summary>
        /// <param name="parcelId">ID của kiện hàng</param>
        /// <returns>Chi tiết kiện hàng</returns>
        /// <response code="200">Lấy chi tiết thành công</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="403">Không có quyền truy cập (yêu cầu role Staff hoặc WarehouseStaff)</response>
        /// <response code="404">Không tìm thấy kiện hàng</response>
        [HttpGet("{parcelId:guid}")]
        [AuthorizeRoles("Admin,ADMIN,Administrator,ADMINISTRATOR,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Sale,SALE,Customer,CUSTOMER")]
        [ProducesResponseType(typeof(ParcelDetailResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetParcelDetail([FromRoute] Guid parcelId)
        {
            try
            {
                var result = await _parcelService.GetParcelDetailAsync(parcelId);
                return Ok(new { message = "Lấy chi tiết kiện hàng thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách kiện chờ nhập vị trí kho
        /// </summary>
        /// <param name="pageNumber">Số trang (mặc định 1)</param>
        /// <param name="pageSize">Số lượng kiện mỗi trang (mặc định 10)</param>
        /// <param name="search">Từ khóa tìm kiếm theo mã kiện hoặc mã gửi hàng (tùy chọn)</param>
        /// <param name="status">Lọc theo trạng thái kiện (tùy chọn)</param>
        /// <returns>Danh sách kiện chờ nhập vị trí kho phân trang</returns>
        /// <response code="200">Lấy danh sách thành công</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="403">Không có quyền truy cập (yêu cầu role Staff hoặc WarehouseStaff)</response>
        [HttpGet("pending-putaway")]
        [AuthorizeRoles("Admin,ADMIN,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Sale,SALE")]
        [ProducesResponseType(typeof(PaginatedResponse<PendingPutawayParcelDto>), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetPendingPutawayParcels(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] string? regionCode = null,
            [FromQuery] string? region = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            try
            {
                // Nhân viên kho đã được gán kho: chỉ kiện của các kho được gán (không dựa vào vùng trong token).
                var scope = await _userWarehouses.GetListScopeAsync(CurrentUserId, CurrentRole);

                var effectiveRegion = !string.IsNullOrWhiteSpace(regionCode) ? regionCode : region;
                if (string.IsNullOrWhiteSpace(effectiveRegion) && scope == null)
                {
                    var tokenRegion = User.FindFirst("region")?.Value;
                    if (!string.IsNullOrWhiteSpace(tokenRegion))
                    {
                        effectiveRegion = tokenRegion;
                    }
                }

                var result = await _parcelService.GetPendingPutawayParcelsAsync(pageNumber, pageSize, search, status, effectiveRegion, scope);
                return Ok(new
                {
                    message = "Lấy danh sách kiện chờ nhập vị trí kho thành công.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Nhập kiện vào vị trí kho (Put-Away)
        /// </summary>
        /// <param name="parcelId">ID của kiện hàng</param>
        /// <param name="request">Thông tin vị trí kho (Warehouse, Zone, Shelf, Bin)</param>
        /// <returns>Thông tin vị trí lưu trữ của Parcel sau khi nhập vị trí thành công</returns>
        /// <response code="200">Nhập vị trí kho thành công</response>
        /// <response code="400">Yêu cầu không hợp lệ, vị trí không hợp lệ hoặc không đủ sức chứa</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="403">Không có quyền thao tác (yêu cầu role Staff hoặc WarehouseStaff)</response>
        /// <response code="404">Không tìm thấy kiện hàng hoặc vị trí kho</response>
        [HttpPut("{parcelId}/put-away")]
        [AuthorizeRoles("Admin,ADMIN,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        [ProducesResponseType(typeof(ParcelPutAwayResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> PutAway([FromRoute] Guid parcelId, [FromBody] ParcelPutAwayRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var denied = await DenyOutsideAssignedAsync(request?.WarehouseId);
            if (denied != null) return denied;

            try
            {
                var result = await _parcelService.PutAwayParcelAsync(parcelId, request);
                return Ok(new { message = "Nhập vị trí kho cho kiện hàng thành công.", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Tách một kiện hàng thành nhiều kiện con. Kiện gốc chuyển sang trạng thái SPLIT,
        /// các kiện con kế thừa yêu cầu ký gửi và trỏ về kiện gốc qua parentParcelId.
        /// </summary>
        [HttpPost("{parcelId}/split")]
        [AuthorizeRoles("Admin,WarehouseStaff")]
        public async Task<IActionResult> SplitParcel(Guid parcelId, [FromBody] SplitParcelRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

            var denied = await DenyOutsideAssignedAsync(await _userWarehouses.WarehouseOfParcelAsync(parcelId));
            if (denied != null) return denied;

            try
            {
                var result = await _parcelService.SplitParcelAsync(parcelId, request, userId);
                return Ok(new { message = "Tách kiện hàng thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Gộp nhiều kiện hàng của cùng một yêu cầu ký gửi thành một kiện mới.
        /// Các kiện gốc chuyển sang trạng thái MERGED và trỏ về kiện mới qua parentParcelId.
        /// </summary>
        [HttpPost("merge")]
        [AuthorizeRoles("Admin,WarehouseStaff")]
        public async Task<IActionResult> MergeParcels([FromBody] MergeParcelsRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

            foreach (var sourceId in request?.SourceParcelIds ?? new System.Collections.Generic.List<Guid>())
            {
                var denied = await DenyOutsideAssignedAsync(await _userWarehouses.WarehouseOfParcelAsync(sourceId));
                if (denied != null) return denied;
            }

            try
            {
                var result = await _parcelService.MergeParcelsAsync(request, userId);
                return Ok(new { message = "Gộp kiện hàng thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }
    }
}
