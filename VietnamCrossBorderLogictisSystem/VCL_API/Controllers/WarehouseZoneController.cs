using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.Exceptions;
using VCL_BLL.Services;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>Loại khu của kho (khu nhận / cách ly / lưu kho / xuất) và kiện đang nằm sai khu.</summary>
    [ApiController]
    [Authorize]
    public class WarehouseZoneController : ControllerBase
    {
        private readonly WarehouseZoneService _service;

        public WarehouseZoneController(WarehouseZoneService service)
        {
            _service = service;
        }

        /// <summary>Danh sách khu của kho kèm loại khu, trạng thái, số kệ/ô/kiện.</summary>
        [HttpGet("api/warehouses/{warehouseId}/zones")]
        [AuthorizeRoles("Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF")]
        public async Task<IActionResult> GetZones(Guid warehouseId)
        {
            try
            {
                return Ok(new { message = "Lấy danh sách khu kho thành công.", data = await _service.GetZonesAsync(warehouseId) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Khai loại khu (RECEIVING · QUARANTINE · STORAGE · OUTBOUND), tên, mã và trạng thái.
        /// Khu đang chứa hàng lưu kho không đổi loại/ngừng dùng được.
        /// </summary>
        [HttpPut("api/warehouse-zones/{zoneId}")]
        [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> UpdateZone(Guid zoneId, [FromBody] UpdateWarehouseZoneRequest request)
        {
            try
            {
                return Ok(new { message = "Cập nhật khu kho thành công.", data = await _service.UpdateZoneAsync(zoneId, request) });
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

        /// <summary>
        /// Kiện đang nằm ở ô không hợp lệ cho hàng lưu kho (khu nhận/xuất/cách ly, khu chưa phân loại,
        /// khu hoặc ô ngừng dùng). Kho chuyển về khu lưu kho qua POST /api/inventories/{id}/relocate.
        /// </summary>
        [HttpGet("api/warehouses/{warehouseId}/misplaced-parcels")]
        [AuthorizeRoles("Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF")]
        public async Task<IActionResult> GetMisplacedParcels(Guid warehouseId)
        {
            try
            {
                return Ok(new { message = "Lấy danh sách kiện nằm sai khu thành công.", data = await _service.GetMisplacedParcelsAsync(warehouseId) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
