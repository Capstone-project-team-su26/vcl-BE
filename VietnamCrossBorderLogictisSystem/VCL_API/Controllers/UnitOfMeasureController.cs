using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/units-of-measure")]
    [ApiController]
    public class UnitOfMeasureController : ControllerBase
    {
        private readonly IUnitOfMeasureService _unitOfMeasureService;

        public UnitOfMeasureController(IUnitOfMeasureService unitOfMeasureService)
        {
            _unitOfMeasureService = unitOfMeasureService;
        }

        /// <summary>
        /// Lấy danh sách đơn vị tính đang hoạt động để FE đổ dropdown khi khai báo hàng hoá.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetUnits()
        {
            var data = await _unitOfMeasureService.GetActiveUnitsAsync();
            return Ok(new { message = "Lấy danh sách đơn vị tính thành công.", data });
        }

        /// <summary>
        /// Lấy toàn bộ đơn vị tính cho màn quản trị danh mục, gồm cả loại đã ngừng sử dụng.
        /// </summary>
        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllUnits()
        {
            var data = await _unitOfMeasureService.GetAllUnitsAsync();
            return Ok(new { message = "Lấy danh sách đơn vị tính thành công.", data });
        }

        /// <summary>
        /// Lấy chi tiết một đơn vị tính.
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var data = await _unitOfMeasureService.GetByIdAsync(id);
                return Ok(new { message = "Lấy chi tiết đơn vị tính thành công.", data });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Thêm đơn vị tính mới.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateUnitOfMeasureRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var created = await _unitOfMeasureService.CreateAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new { message = "Thêm đơn vị tính thành công.", data = created });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật đơn vị tính.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUnitOfMeasureRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _unitOfMeasureService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật đơn vị tính thành công." });
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
        /// Xóa đơn vị tính. Đơn vị đã phát sinh trong đơn sẽ được chuyển sang ngừng sử dụng
        /// thay vì xóa vật lý, để không làm hỏng dữ liệu lịch sử.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _unitOfMeasureService.DeleteAsync(id);
                return Ok(new { message = "Xóa đơn vị tính thành công." });
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
