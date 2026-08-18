using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Hàng giao không thành công và quay về kho.
    /// <para>
    /// Hồ sơ thường do webhook hãng vận chuyển tự mở. Các endpoint ở đây là cho người thật:
    /// kho xác nhận đã cầm được hàng, OM chốt hướng xử lý, Sale và khách xem tình hình.
    /// </para>
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/parcel-returns")]
    public class ParcelReturnsController : ControllerBase
    {
        private readonly IParcelReturnService _service;

        public ParcelReturnsController(IParcelReturnService service)
        {
            _service = service;
        }

        private Guid CurrentUserId =>
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

        private string CurrentRole => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        /// <summary>
        /// Danh sách hồ sơ hàng hoàn kèm số liệu tổng.
        /// </summary>
        /// <param name="status">
        /// Lọc theo trạng thái: PENDING_RETURN · IN_TRANSIT_BACK · RECEIVED_AT_WAREHOUSE ·
        /// RESOLVED · LOST. Bỏ trống hoặc <c>ALL</c> là lấy tất cả.
        /// </param>
        /// <param name="warehouseId">Chỉ lấy hồ sơ của một kho. App kho truyền kho mình phụ trách.</param>
        [HttpGet]
        [Authorize(Roles = "Admin,ADMIN,Sale,SALE,Sales,SALES,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        [ProducesResponseType(typeof(ParcelReturnListDto), 200)]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? warehouseId)
        {
            var result = await _service.ListAsync(status, warehouseId, CurrentRole);
            return Ok(new { message = "Lấy danh sách hàng hoàn thành công.", data = result });
        }

        [HttpGet("{returnId}")]
        [Authorize(Roles = "Admin,ADMIN,Sale,SALE,Sales,SALES,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        [ProducesResponseType(typeof(ParcelReturnDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Detail(Guid returnId)
        {
            try
            {
                var result = await _service.GetDetailAsync(returnId);
                return Ok(new { message = "Lấy chi tiết hồ sơ hàng hoàn thành công.", data = result });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        /// <summary>
        /// Mở hồ sơ bằng tay — dùng khi hãng chưa kịp báo hoặc báo sai, hoặc khách tự mang trả.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,ADMIN,Sale,SALE,Sales,SALES,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> Open([FromBody] OpenParcelReturnRequest request)
        {
            try
            {
                var result = await _service.OpenManuallyAsync(request, CurrentUserId, CurrentRole);
                return Ok(new { message = "Đã mở hồ sơ hàng hoàn.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>
        /// Kho VN xác nhận đã cầm được kiện hoàn về, kèm xếp kệ nếu có chỗ.
        /// </summary>
        /// <remarks>
        /// Bỏ trống <c>binId</c> nghĩa là kiện để khu tạm chờ OM quyết. Có xếp kệ thì hệ thống
        /// ghi một lượt nhập kho mới, phí lưu kho của quãng hoàn về tính từ mốc đó.
        /// </remarks>
        [HttpPut("{returnId}/receive")]
        [Authorize(Roles = "Admin,ADMIN,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> Receive(Guid returnId, [FromBody] ReceiveParcelReturnRequest request)
        {
            try
            {
                var result = await _service.ConfirmReceivedAsync(returnId, request, CurrentUserId, CurrentRole);
                return Ok(new { message = "Đã xác nhận nhận lại hàng hoàn.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>
        /// OM chốt hướng xử lý: giao lại, khách tự lấy, thanh lý, hay bồi thường.
        /// </summary>
        /// <remarks>
        /// Chọn giao lại thì bắt buộc nói rõ ai chịu cước. Chốt xong kiện quay về diện sẵn sàng
        /// xuất kho để Sale lập được phiếu giao mới.
        /// </remarks>
        [HttpPut("{returnId}/resolve")]
        [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> Resolve(Guid returnId, [FromBody] ResolveParcelReturnRequest request)
        {
            try
            {
                var result = await _service.ResolveAsync(returnId, request, CurrentUserId, CurrentRole);
                return Ok(new { message = "Đã chốt hướng xử lý hàng hoàn.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>
        /// Hồ sơ hàng hoàn của một đơn. Khách xem đường này nên chỉ trả hồ sơ của chính đơn đó.
        /// </summary>
        [HttpGet("~/api/orders/{orderId}/parcel-returns")]
        [ProducesResponseType(typeof(ParcelReturnListDto), 200)]
        public async Task<IActionResult> ByOrder(Guid orderId)
        {
            try
            {
                var result = await _service.ListByOrderAsync(orderId, CurrentUserId, CurrentRole);
                return Ok(new { message = "Lấy hồ sơ hàng hoàn của đơn thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }
    }
}
