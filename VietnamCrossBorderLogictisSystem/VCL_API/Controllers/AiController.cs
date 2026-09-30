using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers;

[Route("api/ai")]
[ApiController]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiService _aiService;

    public AiController(IAiService aiService)
    {
        _aiService = aiService;
    }

    /// <summary>
    /// AI gợi ý vị trí xếp kho phù hợp cho kiện hàng
    /// </summary>
    [HttpPost("warehouse/putaway-suggestions")]
    public async Task<IActionResult> GetPutawaySuggestions([FromBody] PutawaySuggestionRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _aiService.GetPutawaySuggestionsAsync(request);
            return Ok(new { message = "Lấy danh sách gợi ý vị trí xếp kho thành công.", data = result });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = this.UserError(ex) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
        }
    }

    /// <summary>
    /// AI hỗ trợ Sales tra cứu tình trạng đơn hàng
    /// </summary>
    [HttpPost("sales/order-status-query")]
    public async Task<IActionResult> QuerySalesOrderStatus([FromBody] SalesOrderStatusQueryRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? 
                       User.FindFirst("role")?.Value;

        var allowedRoles = new[] { "ADMIN", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS" };
        if (string.IsNullOrEmpty(userRole) || !allowedRoles.Contains(userRole.ToUpperInvariant()))
        {
            return StatusCode(403, new { message = "Bạn không có quyền truy xuất dữ liệu tra cứu tình trạng đơn hàng cho Sales." });
        }

        try
        {
            var result = await _aiService.QuerySalesOrderStatusAsync(request);
            return Ok(result);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = this.UserError(ex) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
        }
    }

    /// <summary>
    /// AI Agent trích xuất tự động thông tin tạo đơn Ký gửi (CONSIGNMENT) hoặc Mua hộ (PURCHASE) từ tin nhắn của Khách hàng
    /// </summary>
    [HttpPost("customer/extract-order-intent")]
    public async Task<IActionResult> ExtractCustomerOrderIntent([FromBody] CustomerOrderIntentRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var result = await _aiService.ExtractCustomerOrderIntentAsync(request);
            return Ok(new { message = "Trích xuất thông tin tạo đơn bằng AI thành công.", data = result });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = this.UserError(ex) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
        }
    }
}

