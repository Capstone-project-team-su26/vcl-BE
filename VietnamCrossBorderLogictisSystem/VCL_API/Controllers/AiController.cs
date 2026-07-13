using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

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
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
        }
    }
}
