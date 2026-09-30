using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Hàng về Việt Nam: khách chọn giao ngay / gửi kho cho từng kiện, xem trước tiền tất toán
    /// theo cân đo tại kho VN, mở khiếu nại sau khi nhận hàng.
    /// </summary>
    [ApiController]
    [Authorize]
    public class VnArrivalController : ControllerBase
    {
        private readonly IVnArrivalService _arrival;
        private readonly IParcelIncidentService _incidents;

        public VnArrivalController(IVnArrivalService arrival, IParcelIncidentService incidents)
        {
            _arrival = arrival;
            _incidents = incidents;
        }

        private Guid UserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        private string Role => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>Hướng xử lý từng kiện của đơn và còn đổi được không.</summary>
        [HttpGet("api/orders/consignments/{orderId}/destination-handling")]
        public Task<IActionResult> GetHandling(Guid orderId) =>
            Run("Lấy hướng xử lý kiện thành công.", () => _arrival.GetHandlingAsync(UserId, Role, orderId));

        /// <summary>Khách (hoặc Sale) chọn DIRECT_DELIVERY / STORE_AT_VN cho từng kiện.</summary>
        [HttpPut("api/orders/consignments/{orderId}/destination-handling")]
        public Task<IActionResult> SetHandling(Guid orderId, [FromBody] SetDestinationHandlingRequest request) =>
            Run("Đã cập nhật hướng xử lý kiện.", () => _arrival.SetHandlingAsync(UserId, Role, orderId, request));

        /// <summary>Xem trước đợt tất toán: điều chỉnh cước theo cân đo VN, phí lưu kho, số còn phải trả, vướng mắc.</summary>
        [HttpGet("api/orders/{orderId}/settlement-preview")]
        public Task<IActionResult> SettlementPreview(Guid orderId) =>
            Run("Lấy xem trước tất toán thành công.", () => _arrival.GetSettlementPreviewAsync(UserId, Role, orderId));

        /// <summary>Khách khiếu nại sau khi nhận hàng (trong hạn khiếu nại).</summary>
        [HttpPost("api/orders/consignments/{orderId}/complaints")]
        public Task<IActionResult> Complain(Guid orderId, [FromBody] CreateComplaintRequest request) =>
            Run("Đã ghi nhận khiếu nại.", () => _incidents.CreateComplaintAsync(UserId, Role, orderId, request), 201);

        private async Task<IActionResult> Run<T>(string message, Func<Task<T>> action, int status = 200)
        {
            if (UserId == Guid.Empty) return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            try
            {
                return StatusCode(status, new { message, data = await action() });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }
    }

    /// <summary>Sự cố hàng hoá ở chặng Việt Nam.</summary>
    [ApiController]
    [Route("api/parcel-incidents")]
    [Authorize]
    public class ParcelIncidentsController : ControllerBase
    {
        private readonly IParcelIncidentService _service;

        public ParcelIncidentsController(IParcelIncidentService service)
        {
            _service = service;
        }

        private Guid UserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        private string Role => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>Nhân viên kho mở sự cố cho kiện đang ở kho VN.</summary>
        [HttpPost]
        public Task<IActionResult> Create([FromBody] CreateParcelIncidentRequest request) =>
            Run("Đã mở sự cố.", () => _service.CreateAsync(UserId, Role, request), 201);

        /// <summary>Danh sách sự cố (khách chỉ thấy của đơn mình).</summary>
        [HttpGet]
        public Task<IActionResult> List([FromQuery] ParcelIncidentQuery query) =>
            Run("Lấy danh sách sự cố thành công.", () => _service.GetListAsync(UserId, Role, query));

        [HttpGet("{incidentId}")]
        public Task<IActionResult> Detail(Guid incidentId) =>
            Run("Lấy chi tiết sự cố thành công.", () => _service.GetDetailAsync(UserId, Role, incidentId));

        /// <summary>Khách chọn cách xử lý: ACCEPT / COMPENSATE / DISPOSE.</summary>
        [HttpPost("{incidentId}/customer-response")]
        public Task<IActionResult> Respond(Guid incidentId, [FromBody] IncidentCustomerResponseRequest request) =>
            Run("Đã ghi nhận lựa chọn của khách.", () => _service.RespondAsync(UserId, Role, incidentId, request));

        /// <summary>Quản lý kho quyết định cách xử lý.</summary>
        [HttpPost("{incidentId}/resolve")]
        public Task<IActionResult> Resolve(Guid incidentId, [FromBody] ResolveIncidentRequest request) =>
            Run("Đã xử lý sự cố.", () => _service.ResolveAsync(UserId, Role, incidentId, request));

        /// <summary>Admin ghi nhận đã chi bồi thường (kèm chứng từ).</summary>
        [HttpPost("{incidentId}/compensation-paid")]
        public Task<IActionResult> CompensationPaid(Guid incidentId, [FromBody] CompensationPaidRequest request) =>
            Run("Đã ghi nhận chi bồi thường.", () => _service.MarkCompensationPaidAsync(UserId, Role, incidentId, request));

        private async Task<IActionResult> Run<T>(string message, Func<Task<T>> action, int status = 200)
        {
            if (UserId == Guid.Empty) return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            try
            {
                return StatusCode(status, new { message, data = await action() });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }
    }
}
