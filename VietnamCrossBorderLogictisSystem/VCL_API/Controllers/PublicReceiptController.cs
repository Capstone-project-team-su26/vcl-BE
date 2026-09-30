using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Phục vụ phiếu xác nhận ký gửi qua link công khai. Kho mở link bằng trình duyệt
    /// trên điện thoại nên không gửi kèm được token; link được bảo vệ bằng khoá GUID khó đoán.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class PublicReceiptController : ControllerBase
    {
        private readonly IConsignmentReceiptService _consignmentReceiptService;

        public PublicReceiptController(IConsignmentReceiptService consignmentReceiptService)
        {
            _consignmentReceiptService = consignmentReceiptService;
        }

        /// <summary>
        /// Mở phiếu xác nhận ký gửi nhập kho (PDF) bằng khoá công khai.
        /// </summary>
        /// <param name="publicKey">Khoá lấy từ trường receiptPdfUrl của đơn.</param>
        /// <param name="download">true để tải file về; mặc định false, mở ngay trong trình duyệt.</param>
        /// <response code="200">Trả về file PDF, hiển thị ngay trên trình duyệt</response>
        /// <response code="404">Khoá không tồn tại hoặc đơn chưa sinh phiếu</response>
        [HttpGet("api/public/receipts/{publicKey:guid}")]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetReceipt(Guid publicKey, [FromQuery] bool download = false)
        {
            try
            {
                var (content, fileName) = await _consignmentReceiptService.GetReceiptByPublicKeyAsync(publicKey);

                // Gán chứ không Append: Append tạo header thứ hai nếu đã có sẵn,
                // Safari gặp Content-Disposition trùng thì bỏ qua và tải file về thay vì mở.
                var disposition = download ? "attachment" : "inline";
                Response.Headers["Content-Disposition"] = $"{disposition}; filename=\"{fileName}\"";

                // Safari/iOS cần Content-Length để hiển thị PDF ngay trong tab;
                // thiếu nó trình duyệt hay chuyển sang tải file.
                Response.Headers["Content-Length"] = content.Length.ToString();
                Response.Headers["Cache-Control"] = "private, max-age=60";
                Response.Headers["X-Content-Type-Options"] = "nosniff";

                return File(content, "application/pdf");
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Không thể mở phiếu xác nhận ký gửi.", error = this.UserError(ex) });
            }
        }
    }
}
