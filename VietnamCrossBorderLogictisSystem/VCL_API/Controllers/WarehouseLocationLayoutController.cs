using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers;

[Route("api/warehouses/{warehouseId}/layout")]
[ApiController]
[Authorize]
public class WarehouseLocationLayoutController : ControllerBase
{
    private readonly IWarehouseLocationLayoutService _layoutService;

    public WarehouseLocationLayoutController(IWarehouseLocationLayoutService layoutService)
    {
        _layoutService = layoutService;
    }

    /// <summary>
    /// Lấy sơ đồ layout của một kho
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLayout(Guid warehouseId)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        try
        {
            var result = await _layoutService.GetLayoutByWarehouseIdAsync(warehouseId, userRole);
            return Ok(new { message = "Lấy sơ đồ layout kho thành công.", data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
        }
    }

    /// <summary>
    /// Lấy sơ đồ layout phân tầng theo từng Zone
    /// </summary>
    [HttpGet("zones")]
    public async Task<IActionResult> GetZonesLayout(Guid warehouseId)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        try
        {
            var result = await _layoutService.GetHierarchicalLayoutAsync(warehouseId, userRole);
            return Ok(new { message = "Lấy sơ đồ layout kho theo zone thành công.", data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
        }
    }

    /// <summary>
    /// Tạo ô/vị trí hiển thị trên sơ đồ kho
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateLayout(Guid warehouseId, [FromBody] CreateWarehouseLocationLayoutDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        try
        {
            var result = await _layoutService.CreateLayoutAsync(warehouseId, userRole, dto);
            return Created(string.Empty, new { message = "Tạo vị trí sơ đồ kho thành công.", data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
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

    /// <summary>
    /// Cập nhật vị trí hiển thị hoặc nhãn của ô sơ đồ kho
    /// </summary>
    [HttpPut("{layoutId}")]
    public async Task<IActionResult> UpdateLayout(Guid warehouseId, Guid layoutId, [FromBody] UpdateWarehouseLocationLayoutDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        try
        {
            var result = await _layoutService.UpdateLayoutAsync(warehouseId, layoutId, userRole, dto);
            return Ok(new { message = "Cập nhật vị trí sơ đồ kho thành công.", data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
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

    /// <summary>
    /// Xóa một ô sơ đồ kho (chỉ khi không có hàng tồn kho ở ô chứa tương ứng)
    /// </summary>
    [HttpDelete("{layoutId}")]
    public async Task<IActionResult> DeleteLayout(Guid warehouseId, Guid layoutId)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        try
        {
            await _layoutService.DeleteLayoutAsync(warehouseId, layoutId, userRole);
            return Ok(new { message = "Xóa vị trí sơ đồ kho thành công." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
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

    /// <summary>
    /// Lấy trạng thái sơ đồ kho theo tồn kho thực tế
    /// GET /api/warehouses/{warehouseId}/layout/status
    /// </summary>
    [HttpGet("status")]
    [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF")]
    public async Task<IActionResult> GetLayoutStatus(Guid warehouseId)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        try
        {
            var result = await _layoutService.GetLayoutStatusByWarehouseIdAsync(warehouseId, userRole);
            return Ok(new { message = "Lấy trạng thái sơ đồ kho thành công.", data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
        }
    }
}
