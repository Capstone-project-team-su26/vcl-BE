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
    [Route("api/restricted-items")]
    [ApiController]
    [Authorize]
    public class RestrictedItemController : ControllerBase
    {
        private readonly IRestrictedItemService _restrictedItemService;

        public RestrictedItemController(IRestrictedItemService restrictedItemService)
        {
            _restrictedItemService = restrictedItemService;
        }

        /// <summary>
        /// Xem danh sách mặt hàng cấm/hạn chế
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var items = await _restrictedItemService.GetAllAsync();
            return Ok(items);
        }

        /// <summary>
        /// Lấy chi tiết một mặt hàng cấm/hạn chế
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var item = await _restrictedItemService.GetByIdAsync(id);
                return Ok(item);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm mặt hàng cấm/hạn chế mới
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateRestrictedItemRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _restrictedItemService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Cập nhật thông tin mặt hàng cấm/hạn chế
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRestrictedItemRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _restrictedItemService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật mặt hàng thành công." });
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
        /// Xóa mặt hàng cấm/hạn chế
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _restrictedItemService.DeleteAsync(id);
                return Ok(new { message = "Xóa mặt hàng thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }
}
