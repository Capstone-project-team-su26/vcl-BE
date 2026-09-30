using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Phiếu xuất kho quốc tế: lập phiếu → quản lý kho duyệt → bốc hàng sang khu xuất → vào lô.
    /// Bàn giao cho hãng vận chuyển nằm ở <c>/api/international-shipments</c>.
    /// </summary>
    [EnableCors("AllowAll")]
    [Route("api/warehouse-release-requests")]
    [ApiController]
    [Authorize]
    public class WroController : ControllerBase
    {
        private readonly IWarehouseReleaseService _service;
        private readonly IExportDocumentService _documents;

        public WroController(IWarehouseReleaseService service, IExportDocumentService documents)
        {
            _service = service;
            _documents = documents;
        }

        private Guid UserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        private string Role => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>
        /// Phiếu xuất kho chứa tên khách và hàng của nhiều đơn — chỉ nhân viên nội bộ được đọc.
        /// Khách theo dõi đơn của mình qua GET /api/orders/consignments/{orderId}/tracking.
        /// </summary>
        private bool IsStaff => StaffRoleHelper.IsInternalStaff(Role);

        private IActionResult StaffOnly() =>
            StatusCode(403, new { message = "Chỉ nhân viên mới xem được phiếu xuất kho." });

        /// <summary>Kiện đủ điều kiện xuất ở kho nguồn của tuyến; kèm kiện vướng và mã lý do.</summary>
        [HttpGet("eligible-parcels")]
        public Task<IActionResult> GetEligibleParcels(
            [FromQuery] Guid shippingRouteId,
            [FromQuery] string? search,
            [FromQuery] bool includeBlocked = false) =>
            Run("Lấy danh sách kiện đủ điều kiện xuất thành công.",
                () => _service.GetEligibleParcelsAsync(UserId, Role, shippingRouteId, search, includeBlocked));

        /// <summary>Danh sách phiếu xuất kho.</summary>
        [HttpGet]
        public Task<IActionResult> GetList([FromQuery] WroListQuery query) =>
            !IsStaff ? Task.FromResult(StaffOnly()) : Run("Lấy danh sách phiếu xuất kho thành công.", () => _service.GetListAsync(UserId, Role, query));

        /// <summary>Chi tiết phiếu: kiện, vị trí, giấy tờ đính kèm.</summary>
        [HttpGet("{requestId}")]
        public Task<IActionResult> GetDetail(Guid requestId) =>
            !IsStaff ? Task.FromResult(StaffOnly()) : Run("Lấy chi tiết phiếu xuất kho thành công.", () => _service.GetDetailAsync(UserId, Role, requestId));

        /// <summary>Lập phiếu nháp cho một tuyến, chọn kiện đủ điều kiện.</summary>
        [HttpPost]
        public Task<IActionResult> Create([FromBody] CreateWarehouseReleaseRequest request) =>
            Run("Lập phiếu xuất kho thành công.", () => _service.CreateAsync(UserId, Role, request), 201);

        /// <summary>Sửa phiếu nháp: đổi tuyến, thêm/bớt kiện.</summary>
        [HttpPut("{requestId}")]
        public Task<IActionResult> Update(Guid requestId, [FromBody] CreateWarehouseReleaseRequest request) =>
            Run("Cập nhật phiếu xuất kho thành công.", () => _service.UpdateAsync(UserId, Role, requestId, request));

        /// <summary>Gửi duyệt — khoá hàng và báo quản lý kho.</summary>
        [HttpPost("{requestId}/submit")]
        public Task<IActionResult> Submit(Guid requestId) =>
            Run("Đã gửi phiếu xuất kho chờ duyệt.", () => _service.SubmitAsync(UserId, Role, requestId));

        /// <summary>Rút phiếu về nháp — nhả hàng đang khoá.</summary>
        [HttpPost("{requestId}/withdraw")]
        public Task<IActionResult> Withdraw(Guid requestId) =>
            Run("Đã rút phiếu về nháp.", () => _service.WithdrawAsync(UserId, Role, requestId));

        /// <summary>Huỷ phiếu nháp.</summary>
        [HttpPost("{requestId}/cancel")]
        public Task<IActionResult> Cancel(Guid requestId) =>
            Run("Đã huỷ phiếu xuất kho.", () => _service.CancelAsync(UserId, Role, requestId));

        /// <summary>Quản lý kho duyệt hoặc từ chối phiếu.</summary>
        [HttpPost("{requestId}/decide")]
        public Task<IActionResult> Decide(Guid requestId, [FromBody] DecideWarehouseReleaseRequest request) =>
            Run("Đã ghi nhận quyết định phiếu xuất kho.", () => _service.DecideAsync(UserId, Role, requestId, request));

        /// <summary>Bốc một kiện sang ô ở khu xuất (quét mã kiện + mã ô).</summary>
        [HttpPost("{requestId}/pick")]
        public Task<IActionResult> Pick(Guid requestId, [FromBody] PickParcelRequest request) =>
            Run("Đã bốc kiện sang khu xuất.", () => _service.PickAsync(UserId, Role, requestId, request));

        /// <summary>Bỏ một kiện khỏi phiếu (bắt buộc ảnh + lý do; kiện đã bốc phải trả về ô kệ lưu kho).</summary>
        [HttpPost("{requestId}/parcels/{parcelId}/remove")]
        public Task<IActionResult> RemoveParcel(Guid requestId, Guid parcelId, [FromBody] RemoveWroParcelRequest request) =>
            Run("Đã bỏ kiện khỏi phiếu xuất kho.", () => _service.RemoveParcelAsync(UserId, Role, requestId, parcelId, request));

        /// <summary>Tách phiếu khi container đầy — phiếu con giữ nguyên khoá hàng, không duyệt lại.</summary>
        [HttpPost("{requestId}/split")]
        public Task<IActionResult> Split(Guid requestId, [FromBody] SplitWarehouseReleaseRequest request) =>
            Run("Đã tách phiếu xuất kho.", () => _service.SplitAsync(UserId, Role, requestId, request));

        /// <summary>Phiếu xuất kho bản PDF (sinh khi duyệt).</summary>
        [HttpGet("{requestId}/release-note")]
        public async Task<IActionResult> ReleaseNote(Guid requestId)
        {
            if (!IsStaff) return StaffOnly();

            try
            {
                var (content, fileName) = await _documents.BuildReleaseNoteAsync(requestId);
                return File(content, "application/pdf", fileName);
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        /// <summary>Danh sách soạn hàng để kho cầm đi bốc.</summary>
        [HttpGet("{requestId}/picking-sheet")]
        public async Task<IActionResult> PickingSheet(Guid requestId)
        {
            if (!IsStaff) return StaffOnly();

            try
            {
                var (content, fileName) = await _documents.BuildPickingSheetAsync(requestId);
                return File(content, "application/pdf", fileName);
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        private async Task<IActionResult> Run<T>(string message, Func<Task<T>> action, int successStatus = 200)
        {
            if (UserId == Guid.Empty)
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });

            try
            {
                var data = await action();
                return StatusCode(successStatus, new { message, data });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }
    }
}
