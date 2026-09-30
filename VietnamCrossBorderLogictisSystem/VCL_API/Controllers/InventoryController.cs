using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_BLL.Services;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/inventories")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;
        private readonly UserWarehouseService _userWarehouses;

        public InventoryController(IInventoryService inventoryService, UserWarehouseService userWarehouses)
        {
            _inventoryService = inventoryService;
            _userWarehouses = userWarehouses;
        }

        private Guid? CurrentUserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        private string CurrentRole => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>
        /// Phạm vi kho của danh sách: null = như cũ (lọc theo vùng); có giá trị = kho được gán.
        /// Hỏi đích danh một kho ngoài phạm vi thì trả 403.
        /// </summary>
        private async Task<(IReadOnlyCollection<Guid>? Scope, IActionResult? Denied)> ListScopeAsync(Guid? warehouseId)
        {
            var scope = await _userWarehouses.GetListScopeAsync(CurrentUserId, CurrentRole);
            if (scope != null && warehouseId.HasValue && !scope.Contains(warehouseId.Value))
            {
                try
                {
                    await _userWarehouses.EnsureAssignedWarehouseAsync(CurrentUserId, CurrentRole, warehouseId);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return (scope, StatusCode(403, new { message = this.UserError(ex) }));
                }
            }

            return (scope, null);
        }

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
        /// Ghi nhận tồn kho khả dụng từ kiện hàng đã put-away thành công
        /// </summary>
        /// <param name="parcelId">ID của kiện hàng</param>
        [HttpPost("from-parcel/{parcelId}")]
        [AuthorizeRoles("Admin,WarehouseStaff")]
        public async Task<IActionResult> RecordInventoryFromParcel(Guid parcelId)
        {
            try
            {
                var result = await _inventoryService.RecordInventoryFromParcelAsync(parcelId);
                return Created(string.Empty, new { message = "Ghi nhận tồn kho khả dụng thành công.", data = result });
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
        /// Lấy danh sách hàng tồn kho (loại bỏ các dòng zombie AVAILABLE có số lượng 0).
        /// Mỗi dòng kèm thông tin khách hàng, mã ký gửi, mã kiện, ô kệ, kho,
        /// thời điểm bắt đầu lưu kho, cân nặng và thể tích thực tế.
        /// </summary>
        /// <param name="status">Lọc theo trạng thái tồn kho, ví dụ AVAILABLE</param>
        /// <param name="customerId">Lọc theo khách hàng sở hữu hàng tồn</param>
        /// <param name="warehouseId">Lọc theo kho đang lưu hàng</param>
        /// <param name="regionCode">Lọc theo mã vùng quốc gia của kho (JP, VN, TQ...)</param>
        /// <param name="region">Alias tham số cho regionCode</param>
        [HttpGet]
        public async Task<IActionResult> GetInventories(
            [FromQuery] string? status, 
            [FromQuery] Guid? customerId, 
            [FromQuery] Guid? warehouseId,
            [FromQuery] string? regionCode = null,
            [FromQuery] string? region = null)
        {
            var (scope, denied) = await ListScopeAsync(warehouseId);
            if (denied != null) return denied;

            var effectiveRegion = !string.IsNullOrWhiteSpace(regionCode) ? regionCode : region;
            if (string.IsNullOrWhiteSpace(effectiveRegion) && scope == null)
            {
                var tokenRegion = User.FindFirst("region")?.Value;
                if (!string.IsNullOrWhiteSpace(tokenRegion))
                {
                    effectiveRegion = tokenRegion;
                }
            }

            var result = await _inventoryService.GetInventoriesAsync(status, customerId, warehouseId, effectiveRegion, scope);
            return Ok(new { message = "Lấy danh sách tồn kho thành công.", items = result });
        }

        /// <summary>
        /// Điều chuyển kiện hàng của một bản ghi tồn kho sang ô kệ khác trong cùng kho.
        /// </summary>
        /// <param name="inventoryId">ID bản ghi tồn kho cần điều chuyển</param>
        /// <param name="request">Ô kệ đích và lý do điều chuyển</param>
        [HttpPost("{inventoryId}/relocate")]
        [AuthorizeRoles("Admin,ADMIN,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> RelocateInventory(Guid inventoryId, [FromBody] RelocateInventoryRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

            var denied = await DenyOutsideAssignedAsync(await _userWarehouses.WarehouseOfInventoryAsync(inventoryId));
            if (denied != null) return denied;

            try
            {
                var result = await _inventoryService.RelocateInventoryAsync(inventoryId, request, userId);
                return Ok(new { message = "Điều chuyển vị trí kiện hàng thành công.", data = result });
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
        /// Lấy nhật ký di chuyển kiện hàng trong kho, sắp xếp mới nhất trước.
        /// </summary>
        /// <param name="warehouseId">Lọc theo kho diễn ra thao tác</param>
        /// <param name="parcelId">Lọc theo kiện hàng</param>
        /// <param name="limit">Số dòng tối đa, mặc định 50</param>
        [HttpGet("movements")]
        public async Task<IActionResult> GetMovements([FromQuery] Guid? warehouseId, [FromQuery] Guid? parcelId, [FromQuery] int limit = 50)
        {
            var result = await _inventoryService.GetMovementsAsync(warehouseId, parcelId, limit);
            return Ok(new { message = "Lấy nhật ký di chuyển kiện hàng thành công.", items = result });
        }

        /// <summary>
        /// Danh sách kiện hàng tồn kho vượt ngưỡng cảnh báo thời gian lưu, sắp xếp lâu nhất trước.
        /// </summary>
        /// <param name="warehouseId">Lọc theo kho</param>
        /// <param name="customerId">Lọc theo khách hàng</param>
        /// <param name="minDays">Ngưỡng cảnh báo, mặc định 7 ngày</param>
        /// <param name="criticalDays">Ngưỡng nghiêm trọng, mặc định 30 ngày</param>
        /// <param name="limit">Số dòng tối đa, mặc định 100</param>
        /// <param name="regionCode">Lọc theo mã vùng quốc gia của kho (JP, VN, TQ...)</param>
        /// <param name="region">Alias tham số cho regionCode</param>
        [HttpGet("long-stay-alerts")]
        public async Task<IActionResult> GetLongStayAlerts(
            [FromQuery] Guid? warehouseId,
            [FromQuery] Guid? customerId,
            [FromQuery] int minDays = 7,
            [FromQuery] int criticalDays = 30,
            [FromQuery] int limit = 100,
            [FromQuery] string? regionCode = null,
            [FromQuery] string? region = null)
        {
            var (scope, denied) = await ListScopeAsync(warehouseId);
            if (denied != null) return denied;

            var effectiveRegion = !string.IsNullOrWhiteSpace(regionCode) ? regionCode : region;
            if (string.IsNullOrWhiteSpace(effectiveRegion) && scope == null)
            {
                var tokenRegion = User.FindFirst("region")?.Value;
                if (!string.IsNullOrWhiteSpace(tokenRegion))
                {
                    effectiveRegion = tokenRegion;
                }
            }

            var result = await _inventoryService.GetLongStayAlertsAsync(warehouseId, customerId, minDays, criticalDays, limit, effectiveRegion, scope);
            return Ok(new { message = "Lấy danh sách cảnh báo tồn kho lâu ngày thành công.", items = result });
        }

        /// <summary>
        /// Báo cáo tồn kho tổng hợp theo từng khách hàng, sắp xếp theo số kiện giảm dần.
        /// </summary>
        /// <param name="warehouseId">Lọc theo kho</param>
        /// <param name="customerId">Lọc theo một khách hàng cụ thể</param>
        /// <param name="regionCode">Lọc theo mã vùng quốc gia của kho (JP, VN, TQ...)</param>
        /// <param name="region">Alias tham số cho regionCode</param>
        [HttpGet("storage-report")]
        public async Task<IActionResult> GetCustomerStorageReport(
            [FromQuery] Guid? warehouseId, 
            [FromQuery] Guid? customerId,
            [FromQuery] string? regionCode = null,
            [FromQuery] string? region = null)
        {
            var (scope, denied) = await ListScopeAsync(warehouseId);
            if (denied != null) return denied;

            var effectiveRegion = !string.IsNullOrWhiteSpace(regionCode) ? regionCode : region;
            if (string.IsNullOrWhiteSpace(effectiveRegion) && scope == null)
            {
                var tokenRegion = User.FindFirst("region")?.Value;
                if (!string.IsNullOrWhiteSpace(tokenRegion))
                {
                    effectiveRegion = tokenRegion;
                }
            }

            var result = await _inventoryService.GetCustomerStorageReportAsync(warehouseId, customerId, effectiveRegion, scope);
            return Ok(new { message = "Lấy báo cáo tồn kho theo khách hàng thành công.", items = result });
        }

        /// <summary>
        /// Ghi nhận một lần kiểm kê định kỳ tại ô kệ.
        /// Số kiện hệ thống ghi nhận do server tự đếm, không lấy theo số client gửi lên.
        /// </summary>
        [HttpPost("cycle-counts")]
        [AuthorizeRoles("Admin,WarehouseStaff")]
        public async Task<IActionResult> CreateCycleCount([FromBody] CreateCycleCountRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

            if (request != null)
            {
                var denied = await DenyOutsideAssignedAsync(await _userWarehouses.WarehouseOfBinAsync(request.BinId));
                if (denied != null) return denied;
            }

            try
            {
                var result = await _inventoryService.CreateCycleCountAsync(request, userId);
                return Created(string.Empty, new { message = "Ghi nhận kiểm kê thành công.", data = result });
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
        /// Lấy lịch sử kiểm kê định kỳ, sắp xếp mới nhất trước.
        /// </summary>
        /// <param name="warehouseId">Lọc theo kho</param>
        /// <param name="binId">Lọc theo ô kệ</param>
        /// <param name="status">Lọc theo trạng thái: MATCHED, DISCREPANCY, RESOLVED</param>
        /// <param name="limit">Số dòng tối đa, mặc định 50</param>
        [HttpGet("cycle-counts")]
        public async Task<IActionResult> GetCycleCounts([FromQuery] Guid? warehouseId, [FromQuery] Guid? binId, [FromQuery] string? status, [FromQuery] int limit = 50)
        {
            var result = await _inventoryService.GetCycleCountsAsync(warehouseId, binId, status, limit);
            return Ok(new { message = "Lấy lịch sử kiểm kê thành công.", items = result });
        }

        /// <summary>
        /// Ghi nhận cách xử lý chênh lệch của một phiếu kiểm kê.
        /// </summary>
        [HttpPut("cycle-counts/{cycleCountId}/resolve")]
        [AuthorizeRoles("Admin,WarehouseStaff,OperationsManager")]
        public async Task<IActionResult> ResolveCycleCount(Guid cycleCountId, [FromBody] ResolveCycleCountRequestDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

            try
            {
                var result = await _inventoryService.ResolveCycleCountAsync(cycleCountId, request, userId);
                return Ok(new { message = "Ghi nhận xử lý chênh lệch thành công.", data = result });
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
