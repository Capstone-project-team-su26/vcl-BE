using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>Giấy tờ đính kèm cho đơn, phiếu xuất kho, kiện trong phiếu và lô vận chuyển.</summary>
    [ApiController]
    [Route("api/attachments")]
    [Authorize]
    public class AttachmentsController : ControllerBase
    {
        private readonly IAttachmentService _service;

        public AttachmentsController(IAttachmentService service)
        {
            _service = service;
        }

        private Guid UserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        private string Role => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>Tải giấy tờ lên (PDF/JPG/PNG/WEBP, tối đa 10 MB).</summary>
        [HttpPost]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> Upload(
            IFormFile file,
            [FromForm] string entityType,
            [FromForm] Guid entityId,
            [FromForm] string documentType,
            [FromForm] string? note)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Chưa chọn file để tải lên." });

            try
            {
                await using var stream = file.OpenReadStream();
                var dto = await _service.UploadAsync(UserId, Role, new UploadAttachmentRequest
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    DocumentType = documentType,
                    Note = note,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Content = stream,
                    Length = file.Length
                });

                return Ok(new { message = "Tải giấy tờ lên thành công.", data = dto });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>Danh sách giấy tờ của một đối tượng.</summary>
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? entityType, [FromQuery] Guid entityId)
        {
            try
            {
                return Ok(new { message = "Lấy danh sách giấy tờ thành công.", data = await _service.ListAsync(UserId, Role, entityType, entityId) });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>Tải file về. File nằm riêng tư trên máy chủ nên đường dẫn này luôn kiểm quyền.</summary>
        [HttpGet("{attachmentId}/download")]
        public async Task<IActionResult> Download(Guid attachmentId)
        {
            try
            {
                var (content, contentType, fileName) = await _service.DownloadAsync(UserId, Role, attachmentId);
                return File(content, contentType, fileName);
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }
    }
}
