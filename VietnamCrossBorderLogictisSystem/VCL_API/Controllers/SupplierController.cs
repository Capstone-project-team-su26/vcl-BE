using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/suppliers")]
    [ApiController]
    [Authorize]
    public class SupplierController : ControllerBase
    {
        private readonly ISupplierService _supplierService;

        public SupplierController(ISupplierService supplierService)
        {
            _supplierService = supplierService;
        }

        /// <summary>
        /// Danh sách nhà cung cấp dịch vụ, ví dụ đơn vị trung chuyển và đơn vị lấy hàng.
        /// </summary>
        /// <param name="supplierType">Lọc theo loại: TRANSIT, PICKUP, GOODS, SERVICE</param>
        /// <param name="isActive">Lọc theo trạng thái đang hoạt động</param>
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? supplierType, [FromQuery] bool? isActive)
        {
            var data = await _supplierService.GetAllAsync(supplierType, isActive);
            return Ok(new { message = "Lấy danh sách nhà cung cấp thành công.", data });
        }

        /// <summary>
        /// Lấy chi tiết một nhà cung cấp.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var data = await _supplierService.GetByIdAsync(id);
                return Ok(new { message = "Lấy chi tiết nhà cung cấp thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm nhà cung cấp mới.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateSupplierRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _supplierService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new { message = "Thêm nhà cung cấp thành công.", data = created });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Cập nhật thông tin nhà cung cấp.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSupplierRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _supplierService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật nhà cung cấp thành công." });
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
        /// Xóa nhà cung cấp. Nhà cung cấp đang được gán cho tuyến vận chuyển
        /// sẽ được chuyển sang ngừng sử dụng thay vì xóa vật lý.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _supplierService.DeleteAsync(id);
                return Ok(new { message = "Xóa nhà cung cấp thành công." });
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
    }
}
