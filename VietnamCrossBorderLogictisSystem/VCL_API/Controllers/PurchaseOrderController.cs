using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Đơn mua NCC của luồng mua hộ: lập → (khách duyệt giá chênh) → Admin duyệt → đặt NCC → tiến độ NCC.
    /// Đặt NCC xong hệ thống sinh đơn kho PURCHASE + phiếu tiếp nhận, từ đó đi như ký gửi.
    /// </summary>
    [ApiController]
    [Authorize]
    public class PurchaseOrderController : ControllerBase
    {
        private readonly IPurchaseOrderService _service;

        public PurchaseOrderController(IPurchaseOrderService service)
        {
            _service = service;
        }

        /// <summary>Sale lập đơn mua NCC (nháp) cho yêu cầu đã trả trước.</summary>
        [HttpPost("api/purchase-requests/{requestId}/purchase-orders")]
        public Task<IActionResult> Create(Guid requestId, [FromBody] SavePurchaseOrderDto dto) =>
            Run((uid, role) => _service.CreateAsync(requestId, uid, role, dto), "Đã lập đơn mua NCC.", created: true);

        /// <summary>Các đơn mua NCC của một yêu cầu (khách chỉ thấy đơn đã gửi, ẩn NCC và giá gốc).</summary>
        [HttpGet("api/purchase-requests/{requestId}/purchase-orders")]
        public Task<IActionResult> ListByRequest(Guid requestId) =>
            Run((uid, role) => _service.ListByRequestAsync(requestId, uid, role));

        /// <summary>Danh sách đơn mua NCC cho nhân viên. <paramref name="status"/> nhận nhiều giá trị, cách nhau dấu phẩy.</summary>
        [HttpGet("api/purchase-orders")]
        public Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search,
            [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
            Run((_, role) => _service.ListAsync(role, status, search, pageNumber, pageSize));

        [HttpGet("api/purchase-orders/{id}")]
        public Task<IActionResult> Get(Guid id) =>
            Run((uid, role) => _service.GetAsync(id, uid, role));

        /// <summary>Sale sửa đơn nháp / bị từ chối.</summary>
        [HttpPut("api/purchase-orders/{id}")]
        public Task<IActionResult> Update(Guid id, [FromBody] SavePurchaseOrderDto dto) =>
            Run((uid, role) => _service.UpdateAsync(id, uid, role, dto), "Đã cập nhật đơn mua.");

        /// <summary>Sale gửi duyệt. Giá thực vượt giá báo quá ngưỡng thì chuyển khách xem trước.</summary>
        [HttpPost("api/purchase-orders/{id}/submit")]
        public Task<IActionResult> Submit(Guid id) =>
            Run((uid, role) => _service.SubmitAsync(id, uid, role), "Đã gửi đơn mua.");

        /// <summary>Khách đồng ý (sinh link trả phần chênh) hoặc không đồng ý giá mới.</summary>
        [HttpPost("api/purchase-orders/{id}/customer-decision")]
        public Task<IActionResult> CustomerDecision(Guid id, [FromBody] PurchaseOrderCustomerDecisionDto dto) =>
            Run((uid, role) => _service.CustomerDecisionAsync(id, uid, role, dto), "Đã ghi nhận lựa chọn của khách.");

        /// <summary>Admin duyệt / từ chối đơn mua.</summary>
        [HttpPost("api/purchase-orders/{id}/decide")]
        public Task<IActionResult> Decide(Guid id, [FromBody] PurchaseOrderDecisionDto dto) =>
            Run((uid, role) => _service.DecideAsync(id, uid, role, dto), "Đã ghi nhận quyết định.");

        /// <summary>Sale xác nhận đã đặt NCC (mã đơn NCC + ảnh PURCHASE_PROOF đã tải).</summary>
        [HttpPost("api/purchase-orders/{id}/place")]
        public Task<IActionResult> Place(Guid id, [FromBody] PlacePurchaseOrderDto dto) =>
            Run((uid, role) => _service.PlaceAsync(id, uid, role, dto), "Đã đặt NCC — đơn kho và phiếu tiếp nhận đã được tạo.");

        /// <summary>Sale cập nhật tiến độ NCC: SUPPLIER_CONFIRMED / SUPPLIER_SHIPPED.</summary>
        [HttpPut("api/purchase-orders/{id}/progress")]
        public Task<IActionResult> Progress(Guid id, [FromBody] PurchaseOrderProgressDto dto) =>
            Run((uid, role) => _service.UpdateProgressAsync(id, uid, role, dto), "Đã cập nhật tiến độ NCC.");

        /// <summary>Huỷ đơn mua (đã đặt NCC thì chỉ Admin, và kho chưa nhận kiện nào).</summary>
        [HttpPost("api/purchase-orders/{id}/cancel")]
        public Task<IActionResult> Cancel(Guid id, [FromBody] CancelPurchaseOrderDto dto) =>
            Run((uid, role) => _service.CancelAsync(id, uid, role, dto), "Đã huỷ đơn mua.");

        /// <summary>
        /// Xác nhận đã hoàn tiền cho khách (giá thực thấp hơn giá báo, hoặc huỷ sau khi đặt NCC).
        /// Bắt buộc gửi mã giao dịch chuyển khoản.
        /// </summary>
        [HttpPost("api/purchase-orders/{id}/refund/complete")]
        public Task<IActionResult> CompleteRefund(Guid id, [FromBody] CompletePurchaseRefundDto dto) =>
            Run((uid, role) => _service.CompleteRefundAsync(id, uid, role, dto), "Đã ghi nhận hoàn tiền cho khách.");

        /// <summary>
        /// Sale / Admin đóng phần không mua được (NCC hết hàng, mua ít hơn khách đặt) và phần NCC giao
        /// thiếu → khoản hoàn = tiền hàng + phí mua hộ + VAT phần phí tương ứng, chi tiết từng sản phẩm.
        /// </summary>
        [HttpPost("api/purchase-requests/{requestId}/close-unfulfilled")]
        public Task<IActionResult> CloseUnfulfilled(Guid requestId, [FromBody] CloseUnfulfilledDto dto) =>
            Run((uid, role) => _service.CloseUnfulfilledAsync(requestId, uid, role, dto), "Đã đóng phần không mua được và lập khoản hoàn.");

        /// <summary>Danh sách khoản hoàn của yêu cầu kèm công thức từng dòng (khách xem yêu cầu của mình).</summary>
        [HttpGet("api/purchase-requests/{requestId}/refunds")]
        public Task<IActionResult> Refunds(Guid requestId) =>
            Run((uid, role) => _service.GetRefundsAsync(requestId, uid, role));

        /// <summary>Kế toán xác nhận đã chuyển một khoản hoàn cụ thể (mã giao dịch bắt buộc, số tiền phải khớp nếu gửi).</summary>
        [HttpPost("api/purchase-requests/{requestId}/refunds/{refundId}/complete")]
        public Task<IActionResult> CompleteRequestRefund(Guid requestId, Guid refundId, [FromBody] CompletePurchaseRefundDto dto) =>
            Run((uid, role) => _service.CompleteRequestRefundAsync(requestId, refundId, uid, role, dto), "Đã ghi nhận hoàn tiền cho khách.");

        private async Task<IActionResult> Run<T>(Func<Guid, string, Task<T>> action, string? message = null, bool created = false)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            try
            {
                var data = await action(userId, role);
                var body = new { message = message ?? "OK", data };
                return created ? StatusCode(201, body) : Ok(body);
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ConflictException ex) { return Conflict(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }
    }
}
