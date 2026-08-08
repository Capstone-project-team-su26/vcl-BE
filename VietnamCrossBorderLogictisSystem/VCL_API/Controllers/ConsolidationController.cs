using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Quản lý gom hàng (Consolidation) - Đóng gói nhiều bao hàng/đơn hàng thành lô hàng lớn để vận chuyển chéo biên giới.
    /// </summary>
    [Route("api/consolidation")]
    [ApiController]
    [Authorize(Roles = "Staff,STAFF,WarehouseStaff,WAREHOUSESTAFF,Admin,ADMIN,Administrator,ADMINISTRATOR,OperationsManager,OPERATIONSMANAGER")]
    public class ConsolidationController : ControllerBase
    {
        private readonly IConsolidationService _consolidationService;

        public ConsolidationController(IConsolidationService consolidationService)
        {
            _consolidationService = consolidationService;
        }

        /// <summary>
        /// Tạo một lô gom hàng mới (Consolidation) từ các bao hàng/đơn hàng được chọn.
        /// </summary>
        /// <param name="request">Dữ liệu yêu cầu tạo lô gom hàng</param>
        /// <returns>Thông tin chi tiết của lô gom hàng vừa tạo</returns>
        /// <response code="201">Tạo thành công lô gom hàng</response>
        /// <response code="400">Dữ liệu yêu cầu không hợp lệ hoặc không đủ điều kiện</response>
        /// <response code="404">Không tìm thấy bao hàng hoặc đơn hàng tương ứng</response>
        /// <response code="500">Lỗi hệ thống</response>
        [HttpPost]
        [ProducesResponseType(typeof(ConsolidationResponseDto), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Create([FromBody] CreateConsolidationRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _consolidationService.CreateConsolidationAsync(request);
                return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống.", details = ex.Message });
            }
        }

        /// <summary>
        /// Lấy thông tin chi tiết của một lô gom hàng bằng ID.
        /// </summary>
        /// <param name="id">ID của lô gom hàng</param>
        /// <returns>Thông tin chi tiết lô gom hàng</returns>
        /// <response code="200">Tìm thấy thông tin lô gom hàng</response>
        /// <response code="404">Không tìm thấy lô gom hàng với ID đã cung cấp</response>
        /// <response code="500">Lỗi hệ thống</response>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ConsolidationResponseDto), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var result = await _consolidationService.GetConsolidationByIdAsync(id);
                return Ok(result);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống.", details = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách toàn bộ các lô gom hàng (hỗ trợ lọc theo trạng thái và tìm kiếm).
        /// </summary>
        /// <param name="status">Trạng thái cần lọc (ví dụ: DRAFT, CONSOLIDATED, v.v.)</param>
        /// <param name="search">Từ khóa tìm kiếm theo mã gom hàng hoặc thông tin liên quan</param>
        /// <returns>Danh sách các lô gom hàng</returns>
        /// <response code="200">Lấy danh sách thành công</response>
        /// <response code="500">Lỗi hệ thống</response>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ConsolidationResponseDto>), 200)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? search)
        {
            try
            {
                var result = await _consolidationService.GetConsolidationsAsync(status, search);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống.", details = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật thông tin của một lô gom hàng (thay đổi trạng thái, thêm/bớt bao hàng/đơn hàng).
        /// </summary>
        /// <param name="id">ID của lô gom hàng cần cập nhật</param>
        /// <param name="request">Dữ liệu yêu cầu cập nhật</param>
        /// <returns>Thông tin lô gom hàng sau khi cập nhật thành công</returns>
        /// <response code="200">Cập nhật thành công</response>
        /// <response code="400">Dữ liệu đầu vào không hợp lệ</response>
        /// <response code="404">Không tìm thấy lô gom hàng hoặc bao hàng tương ứng</response>
        /// <response code="500">Lỗi hệ thống</response>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(ConsolidationResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConsolidationRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _consolidationService.UpdateConsolidationAsync(id, request);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống.", details = ex.Message });
            }
        }

        /// <summary>
        /// Hủy/Xóa một lô gom hàng bằng ID.
        /// </summary>
        /// <param name="id">ID của lô gom hàng cần hủy/xóa</param>
        /// <returns>Thông báo kết quả hủy thành công</returns>
        /// <response code="200">Hủy lô gom hàng thành công</response>
        /// <response code="404">Không tìm thấy lô gom hàng cần hủy</response>
        /// <response code="500">Lỗi hệ thống</response>
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _consolidationService.DeleteConsolidationAsync(id);
                return Ok(new { message = "Hủy gom hàng thành công." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Lỗi hệ thống.", details = ex.Message });
            }
        }
    }
}
