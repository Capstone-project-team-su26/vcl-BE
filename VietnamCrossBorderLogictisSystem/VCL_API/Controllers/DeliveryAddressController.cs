using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/delivery-addresses")]
    [ApiController]
    [Authorize]
    public class DeliveryAddressController : ControllerBase
    {
        private readonly IDeliveryAddressService _service;

        public DeliveryAddressController(IDeliveryAddressService service)
        {
            _service = service;
        }

        private bool TryGetUserId(out Guid userId)
        {
            userId = Guid.Empty;
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out userId);
        }

        /// <summary>Lấy sổ địa chỉ nhận hàng của khách đang đăng nhập.</summary>
        [HttpGet]
        [AuthorizeRoles("Customer,Admin,Sale,WarehouseStaff,OperationsManager,Delivery,customer,sale,admin,staff")]
        public async Task<IActionResult> GetMyAddresses()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                var data = await _service.GetMyAddressesAsync(userId);
                return Ok(data);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Nhân viên xem sổ địa chỉ nhận hàng của một khách hàng cụ thể.
        /// Dùng khi cần lấy địa chỉ để bàn giao cho đơn vị vận chuyển thứ 3 (GHN/GHTK...)
        /// </summary>
        /// <param name="customerId">ID khách hàng (lấy từ chi tiết đơn hoặc danh sách khách).</param>
        /// <response code="200">Lấy danh sách địa chỉ thành công</response>
        /// <response code="403">Không phải nhân viên nội bộ</response>
        /// <response code="404">Không tìm thấy khách hàng</response>
        [HttpGet("~/api/customers/{customerId}/delivery-addresses")]
        // Nhân viên kho dùng chung một role "WarehouseStaff"; kho nào thì phân biệt bằng
        // User.Region, không tách thành role riêng.
        [AuthorizeRoles("Admin,Sale,WarehouseStaff,OperationsManager")]
        public async Task<IActionResult> GetByCustomerId(Guid customerId)
        {
            try
            {
                var data = await _service.GetAddressesByCustomerIdAsync(customerId);
                return Ok(new { message = "Lấy sổ địa chỉ của khách hàng thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Nhân viên thêm địa chỉ nhận hàng HỘ một khách hàng.
        /// Dùng ở màn "Sale tạo đơn hộ khách" khi khách chưa có địa chỉ nào trong sổ.
        /// </summary>
        /// <param name="customerId">ID khách hàng (lấy từ ô tìm khách của màn tạo đơn).</param>
        /// <response code="200">Thêm thành công, trả về địa chỉ vừa tạo</response>
        /// <response code="400">Địa chỉ để trống</response>
        /// <response code="403">Không phải nhân viên nội bộ</response>
        /// <response code="404">Không tìm thấy khách hàng</response>
        [HttpPost("~/api/customers/{customerId}/delivery-addresses")]
        [Authorize(Roles = "Admin,Sale,OperationsManager")]
        public async Task<IActionResult> CreateForCustomer(Guid customerId, [FromBody] CreateDeliveryAddressRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            try
            {
                var created = await _service.CreateForCustomerAsync(customerId, request);
                return Ok(new { message = "Thêm địa chỉ cho khách hàng thành công.", data = created });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                /* BLL chỉ ném ArgumentException cho ô rỗng — dự án chưa có BadRequestException. */
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>Thêm địa chỉ nhận hàng mới.</summary>
        [HttpPost]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Create([FromBody] CreateDeliveryAddressRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                var created = await _service.CreateAsync(userId, request);
                return Ok(new { message = "Thêm địa chỉ thành công.", data = created });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
        }

        /// <summary>Cập nhật một địa chỉ nhận hàng.</summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDeliveryAddressRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                await _service.UpdateAsync(userId, id, request);
                return Ok(new { message = "Cập nhật địa chỉ thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
        }

        /// <summary>Xóa một địa chỉ nhận hàng.</summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });
            try
            {
                await _service.DeleteAsync(userId, id);
                return Ok(new { message = "Xóa địa chỉ thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
        }
    }
}
