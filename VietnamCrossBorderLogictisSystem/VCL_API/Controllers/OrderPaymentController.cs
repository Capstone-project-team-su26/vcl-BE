using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [ApiController]
    [Authorize]
    public class OrderPaymentController : ControllerBase
    {
        private readonly IConsignmentPaymentService _consignmentPaymentService;
        private readonly IStorageFeeService _storageFeeService;

        public OrderPaymentController(
            IConsignmentPaymentService consignmentPaymentService,
            IStorageFeeService storageFeeService)
        {
            _consignmentPaymentService = consignmentPaymentService;
            _storageFeeService = storageFeeService;
        }

        /// <summary>
        /// Bảng giá lưu kho để khách cân nhắc trước khi tick "gửi lại kho VN" lúc tạo đơn.
        /// </summary>
        /// <remarks>
        /// Chưa gắn với đơn nào nên không cần quyền gì ngoài đăng nhập: trả về số ngày miễn phí,
        /// số ngày ân hạn, đơn giá đang áp dụng và vài mốc ngày mẫu. Con số ở đây lấy chung một
        /// chỗ tính với hoá đơn thật nên khách thấy sao thì trả vậy.
        /// </remarks>
        [HttpGet("api/storage-fee/estimate")]
        [ProducesResponseType(typeof(StorageFeeEstimateDto), 200)]
        public async Task<IActionResult> GetStorageFeeEstimate()
        {
            var result = await _storageFeeService.GetEstimateAsync();
            return Ok(new { message = "Lấy bảng phí lưu kho thành công.", data = result });
        }

        /// <summary>
        /// Xem trước phí lưu kho tại kho đích của một đơn, kèm chi tiết từng kiện.
        /// </summary>
        /// <remarks>
        /// Miễn phí 7 ngày đầu kể từ lúc kiện được xếp vào ô kệ ở kho đích; sau đó tính theo
        /// kiện/ngày với đơn giá lấy từ SERVICE_PRICINGS (<c>ServiceType = STORAGE</c>).
        /// Ngừng đếm khi kiện được lấy ra khỏi ô kệ để đóng gói giao đi.
        /// Endpoint này chỉ đọc — số tiền chỉ thực sự vào hoá đơn khi tạo thanh toán cuối.
        /// </remarks>
        [HttpGet("api/orders/{orderId}/storage-fee")]
        [ProducesResponseType(typeof(StorageFeeDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetStorageFee(Guid orderId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                    return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
                await _storageFeeService.EnsureCanViewAsync(orderId, userId, User.FindFirst(ClaimTypes.Role)?.Value);

                var result = await _storageFeeService.CalculateForOrderAsync(orderId);
                return Ok(new { message = "Lấy phí lưu kho thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Chốt phí lưu kho và phát hành mã thanh toán để thu một lần lúc khách đến lấy hàng.
        /// <para>
        /// Kho bấm khi chuẩn bị xuất: hệ thống chốt số ngày lưu thực tế, trừ phần đã thu ở đợt
        /// tất toán, rồi sinh mã cho khách quét ngay tại quầy. Trả xong mới xuất được hàng.
        /// Không phát sinh thêm phí thì trả về <c>isFreeOfCharge</c> và cho xuất luôn.
        /// </para>
        /// </summary>
        [HttpPost("api/orders/{orderId}/payments/storage-fee")]
        [AuthorizeRoles("Admin,ADMIN,Sale,SALE,Sales,SALES,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        public async Task<IActionResult> CreateStorageFeePayment(
            Guid orderId, [FromBody] CreateStorageFeePaymentRequest? request = null)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _consignmentPaymentService.CreateStorageFeePaymentAsync(
                    orderId, userId, userRole, request ?? new CreateStorageFeePaymentRequest());

                var message = result.IsFreeOfCharge
                    ? "Không phát sinh phí lưu kho, có thể xuất hàng."
                    : "Chốt phí lưu kho thành công. Mời khách quét mã thanh toán.";

                return Ok(new { message, data = result });
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
        /// Sale tạo thanh toán cuối (phần còn lại + phí phát sinh) cho đơn ký gửi
        /// khi kiện hàng đã về đến kho đích (RECEIVED_AT_DESTINATION).
        /// </summary>
        /// <remarks>
        /// Chốt một lần: đơn đã có đợt cuối chờ khách trả (hoặc đã trả xong) trả 409. Muốn tính lại
        /// phí lưu kho cho đợt cuối đang chờ thì gửi <c>reissue: true</c>.
        /// </remarks>
        [HttpPost("api/orders/{orderId}/payments/final")]
        public async Task<IActionResult> CreateFinalPayment(Guid orderId, [FromBody] CreateFinalPaymentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _consignmentPaymentService.CreateFinalPaymentAsync(orderId, userId, userRole, request);
                return Ok(new { message = "Tạo thanh toán cuối thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (ConflictException ex)
            {
                // Đơn đã chốt phí cuối / đã tất toán: bấm lại không tạo thêm khoản thu nào.
                return Conflict(new { message = ex.Message });
            }
            catch (DbUpdateConcurrencyException)
            {
                // Hai lần bấm "Chốt" tới cùng lúc: lần tới sau vấp row_version của đơn ngay ở bước khoá
                // dòng đơn (lần trước đã ghi xong) — báo 409 thay vì lỗi hệ thống 500.
                return Conflict(new { message = "Đơn này vừa được chốt phí cuối ở một thao tác khác, đang chờ khách thanh toán — không chốt lại lần nữa. Vui lòng tải lại danh sách." });
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
        /// Lấy lịch sử/danh sách các đợt thanh toán của đơn ký gửi (cọc + thanh toán cuối).
        /// </summary>
        [HttpGet("api/orders/{orderId}/payments")]
        public async Task<IActionResult> GetOrderPayments(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _consignmentPaymentService.GetOrderPaymentsAsync(orderId, userId, userRole);
                return Ok(new { message = "Lấy danh sách thanh toán của đơn thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Lấy lịch sử thanh toán chi tiết của đơn ký gửi (kèm thông tin Customer, báo giá,
        /// từng đợt thanh toán với trạng thái PENDING/SUCCESS/FAILED/CANCELED). Customer/Sales/Admin.
        /// </summary>
        [HttpGet("api/orders/{orderId}/payments/history")]
        public async Task<IActionResult> GetPaymentHistory(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _consignmentPaymentService.GetPaymentHistoryAsync(orderId, userId, userRole);
                return Ok(new { message = "Lấy lịch sử thanh toán của đơn thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }
    }
}
