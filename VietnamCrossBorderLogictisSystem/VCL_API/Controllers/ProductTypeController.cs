using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/product-types")]
    [ApiController]
    public class ProductTypeController : ControllerBase
    {
        private readonly IProductTypeService _productTypeService;

        public ProductTypeController(IProductTypeService productTypeService)
        {
            _productTypeService = productTypeService;
        }

        /// <summary>
        /// Lấy danh sách loại hàng đang hoạt động để FE đổ dropdown.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetProductTypes()
        {
            var data = await _productTypeService.GetActiveProductTypesAsync();
            return Ok(new { message = "Lấy danh sách loại hàng thành công.", data });
        }

        /// <summary>
        /// Lấy toàn bộ loại hàng cho màn quản trị danh mục, gồm cả loại đã ngừng sử dụng.
        /// </summary>
        [HttpGet("all")]
        [Authorize(Roles = "Admin,ADMIN")]
        public async Task<IActionResult> GetAllProductTypes()
        {
            var data = await _productTypeService.GetAllProductTypesAsync();
            return Ok(new { message = "Lấy danh sách loại hàng thành công.", data });
        }

        /// <summary>
        /// Lấy chi tiết một loại hàng.
        /// </summary>
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var data = await _productTypeService.GetByIdAsync(id);
                return Ok(new { message = "Lấy chi tiết loại hàng thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm loại hàng mới.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateProductTypeRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _productTypeService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new { message = "Thêm loại hàng thành công.", data = created });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật loại hàng.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductTypeRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _productTypeService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật loại hàng thành công." });
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
        /// Xóa loại hàng. Loại hàng đã phát sinh trong đơn sẽ được chuyển sang ngừng sử dụng
        /// thay vì xóa vật lý, để không làm hỏng dữ liệu lịch sử.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _productTypeService.DeleteAsync(id);
                return Ok(new { message = "Xóa loại hàng thành công." });
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
