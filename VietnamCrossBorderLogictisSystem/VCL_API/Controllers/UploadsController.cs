using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/uploads")]
    [ApiController]
    [Authorize]
    public class UploadsController : ControllerBase
    {
        private readonly IFileUploadService _uploadService;

        private static readonly string[] AllowedContentTypes = { "image/jpeg", "image/png", "image/webp" };
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

        public UploadsController(IFileUploadService uploadService)
        {
            _uploadService = uploadService;
        }

        /// <summary>
        /// Upload ảnh từ máy người dùng lên cloud, trả về URL để FE gắn vào referenceUrl khi tạo ký gửi.
        /// </summary>
        /// <param name="file">File ảnh (JPG/PNG/WEBP, tối đa 5MB)</param>
        /// <returns>URL công khai của ảnh</returns>
        /// <response code="200">Upload thành công, trả về url</response>
        /// <response code="400">File không hợp lệ (rỗng/sai định dạng/quá lớn)</response>
        /// <response code="401">Chưa đăng nhập</response>
        [HttpPost("image")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Vui lòng chọn một file ảnh." });

            if (!AllowedContentTypes.Contains(file.ContentType))
                return BadRequest(new { message = "Chỉ chấp nhận ảnh JPG, PNG hoặc WEBP." });

            if (file.Length > MaxFileSizeBytes)
                return BadRequest(new { message = "Ảnh vượt quá dung lượng tối đa 5MB." });

            try
            {
                using var stream = file.OpenReadStream();
                var url = await _uploadService.UploadImageAsync(stream, file.FileName);
                return Ok(new { message = "Upload ảnh thành công.", url });
            }
            catch (InvalidOperationException ex)
            {
                // Cloudinary chưa được cấu hình
                return StatusCode(500, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
