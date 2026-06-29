using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/inventories")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>
        /// Ghi nhận tồn kho khả dụng từ kiện hàng đã put-away thành công
        /// </summary>
        /// <param name="parcelId">ID của kiện hàng</param>
        [HttpPost("from-parcel/{parcelId}")]
        [Authorize(Roles = "Admin,WarehouseStaff")]
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
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }
    }
}
