using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

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
        /// <response code="200">Trả về file PDF, hiển thị ngay trên trình duyệt</response>
        /// <response code="404">Khoá không tồn tại hoặc đơn chưa sinh phiếu</response>
        [HttpGet("api/public/receipts/{publicKey:guid}")]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetReceipt(Guid publicKey)
        {
            try
            {
                var (content, fileName) = await _consignmentReceiptService.GetReceiptByPublicKeyAsync(publicKey);

                // inline để trình duyệt mở trực tiếp thay vì tải về rồi mới xem được.
                Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");

                return File(content, "application/pdf");
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Không thể mở phiếu xác nhận ký gửi.", error = ex.Message });
            }
        }
    }
}
