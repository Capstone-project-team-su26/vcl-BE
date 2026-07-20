using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
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
        public async Task<IActionResult> UploadImage(IFormFile file)
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

        private const int MaxFilesPerRequest = 10;

        /// <summary>
        /// Upload NHIỀU ảnh cùng lúc, trả về danh sách URL theo đúng thứ tự file gửi lên.
        /// FE gắn danh sách này vào referenceUrls của từng mặt hàng khi tạo ký gửi.
        /// </summary>
        /// <param name="files">Danh sách file ảnh (JPG/PNG/WEBP, mỗi file tối đa 5MB, tối đa 10 file/lần)</param>
        /// <response code="200">Upload thành công, trả về mảng urls</response>
        /// <response code="400">Có file không hợp lệ (rỗng/sai định dạng/quá lớn/quá số lượng)</response>
        /// <response code="401">Chưa đăng nhập</response>
        [HttpPost("images")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> UploadImages(List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "Vui lòng chọn ít nhất một file ảnh." });

            if (files.Count > MaxFilesPerRequest)
                return BadRequest(new { message = $"Chỉ được upload tối đa {MaxFilesPerRequest} ảnh mỗi lần." });

            // Kiểm tra hết toàn bộ trước, không upload dở dang rồi mới báo lỗi
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null || f.Length == 0)
                    return BadRequest(new { message = $"File thứ {i + 1} rỗng." });
                if (!AllowedContentTypes.Contains(f.ContentType))
                    return BadRequest(new { message = $"File thứ {i + 1} ({f.FileName}): chỉ chấp nhận ảnh JPG, PNG hoặc WEBP." });
                if (f.Length > MaxFileSizeBytes)
                    return BadRequest(new { message = $"File thứ {i + 1} ({f.FileName}): vượt quá dung lượng tối đa 5MB." });
            }

            try
            {
                var urls = new List<string>();
                foreach (var f in files)
                {
                    using var stream = f.OpenReadStream();
                    var url = await _uploadService.UploadImageAsync(stream, f.FileName);
                    urls.Add(url);
                }
                return Ok(new { message = $"Upload {urls.Count} ảnh thành công.", urls });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
