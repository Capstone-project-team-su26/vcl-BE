using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_BLL.Services;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [Route("api/international-shipments")]
    [ApiController]
    [Authorize]
    public class InternationalShipmentsController : ControllerBase
    {
        private readonly IInternationalShipmentService _shipmentService;
        private readonly IExportShipmentService _exportService;
        private readonly IExportDocumentService _documents;
        private readonly IShipmentTrackingService _trackingService;
        private readonly UserWarehouseService _userWarehouses;

        public InternationalShipmentsController(
            IInternationalShipmentService shipmentService,
            IExportShipmentService exportService,
            IExportDocumentService documents,
            IShipmentTrackingService trackingService,
            UserWarehouseService userWarehouses)
        {
            _shipmentService = shipmentService;
            _exportService = exportService;
            _documents = documents;
            _trackingService = trackingService;
            _userWarehouses = userWarehouses;
        }

        /// <summary>Lô chứa kiện và tên khách của nhiều đơn — chỉ nhân viên nội bộ được xem.</summary>
        private bool IsStaff => StaffRoleHelper.IsInternalStaff(CurrentRole);

        /// <summary>
        /// Hàng đợi của Sale: lô đã bàn giao mà chưa về kho đích. Lô quá ngày dự kiến hoặc lâu
        /// không có mốc mới lên đầu. <c>attentionOnly=true</c> chỉ lấy lô cần xử lý.
        /// </summary>
        [HttpGet("tracking-queue")]
        public Task<IActionResult> GetTrackingQueue([FromQuery] Guid? originWarehouseId = null, [FromQuery] bool attentionOnly = false) =>
            RunExport("Lấy hàng đợi theo dõi lô thành công.",
                () => _trackingService.GetInTransitQueueAsync(CurrentUserId, CurrentRole, originWarehouseId, attentionOnly));

        /// <summary>
        /// Dòng thời gian của lô cho Sale: mọi mốc (ai ghi, ghi chú nội bộ, lời nhắn cho khách),
        /// các mốc được bấm tiếp và giấy tờ còn thiếu, các đơn bị ảnh hưởng, giấy tờ đã đính kèm.
        /// </summary>
        [HttpGet("{shipmentId}/timeline")]
        public Task<IActionResult> GetTimeline(Guid shipmentId) =>
            RunExport("Lấy hành trình lô thành công.",
                () => _trackingService.GetShipmentTimelineAsync(CurrentUserId, CurrentRole, shipmentId));

        private Guid CurrentUserId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
        private string CurrentRole => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>Phiếu xuất kho đã bốc xong, chờ đưa vào lô.</summary>
        [HttpGet("ready-wro-requests")]
        public Task<IActionResult> GetReadyWroRequests([FromQuery] Guid? originWarehouseId = null, [FromQuery] string? search = null) =>
            !IsStaff ? Task.FromResult<IActionResult>(StatusCode(403, new { message = "Chỉ nhân viên mới xem được lô vận chuyển." })) :
            RunExport("Lấy danh sách phiếu xuất kho chờ gom lô thành công.",
                () => _exportService.GetReadyWroRequestsAsync(CurrentUserId, CurrentRole, originWarehouseId, search));

        /// <summary>Lập lô nháp từ các phiếu đã bốc xong (cùng kho nguồn, cùng kho đích).</summary>
        [HttpPost]
        public Task<IActionResult> CreateShipment([FromBody] CreateExportShipmentRequest request) =>
            RunExport("Lập lô vận chuyển thành công.",
                () => _exportService.CreateAsync(CurrentUserId, CurrentRole, request), 201);

        /// <summary>Thêm phiếu vào lô nháp.</summary>
        [HttpPost("{shipmentId}/wro-requests")]
        public Task<IActionResult> AddWroRequests(Guid shipmentId, [FromBody] AddWroRequestsToShipmentRequest request) =>
            RunExport("Đã thêm phiếu vào lô.",
                () => _exportService.AddWroRequestsAsync(CurrentUserId, CurrentRole, shipmentId, request));

        /// <summary>Gỡ một phiếu khỏi lô nháp — phiếu về lại trạng thái chờ lập lô.</summary>
        [HttpDelete("{shipmentId}/wro-requests/{wroRequestId}")]
        public Task<IActionResult> RemoveWroRequest(Guid shipmentId, Guid wroRequestId) =>
            RunExport("Đã gỡ phiếu khỏi lô.",
                () => _exportService.RemoveWroRequestAsync(CurrentUserId, CurrentRole, shipmentId, wroRequestId));

        /// <summary>Huỷ lô nháp — mọi phiếu về lại trạng thái chờ lập lô.</summary>
        [HttpPost("{shipmentId}/cancel")]
        public Task<IActionResult> CancelShipment(Guid shipmentId) =>
            RunExport("Đã huỷ lô vận chuyển.", () => _exportService.CancelAsync(CurrentUserId, CurrentRole, shipmentId));

        /// <summary>
        /// Bàn giao lô cho hãng vận chuyển: hàng rời kho, trừ tồn, phiếu và kiện chuyển sang
        /// đã bàn giao. Cần biên bản bàn giao + vận đơn đã đính kèm vào lô.
        /// </summary>
        [HttpPost("{shipmentId}/handover")]
        public Task<IActionResult> Handover(Guid shipmentId, [FromBody] ShipmentHandoverRequest request) =>
            RunExport("Bàn giao lô thành công.",
                () => _exportService.HandoverAsync(CurrentUserId, CurrentRole, shipmentId, request));

        /// <summary>Ghi mốc hành trình sau bàn giao (Sale).</summary>
        [HttpPut("{shipmentId}/status")]
        public Task<IActionResult> UpdateMilestone(Guid shipmentId, [FromBody] ShipmentMilestoneRequest request) =>
            RunExport("Cập nhật hành trình lô thành công.",
                () => _exportService.UpdateMilestoneAsync(CurrentUserId, CurrentRole, shipmentId, request));

        /// <summary>Manifest lô bản PDF.</summary>
        [HttpGet("{shipmentId}/manifest")]
        public async Task<IActionResult> Manifest(Guid shipmentId)
        {
            if (!IsStaff)
                return StatusCode(403, new { message = "Chỉ nhân viên mới xem được lô vận chuyển." });

            try
            {
                var (content, fileName) = await _documents.BuildShipmentManifestAsync(shipmentId);
                return File(content, "application/pdf", fileName);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        private async Task<IActionResult> RunExport<T>(string message, Func<Task<T>> action, int successStatus = 200)
        {
            if (CurrentUserId == Guid.Empty)
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

        /// <summary>
        /// Danh sách lô vận chuyển quốc tế, phục vụ màn Vận chuyển trên web và mobile.
        /// </summary>
        /// <param name="statusTab">Lọc theo tab: PREPARING · IN_TRANSIT · CUSTOMS · ARRIVED · ISSUE</param>
        /// <param name="status">Lọc theo trạng thái cụ thể (CUSTOMS_EXPORT_PENDING, IN_TRANSIT…)</param>
        /// <param name="originWarehouseId">Lọc theo kho đi</param>
        /// <param name="search">Tìm theo mã lô</param>
        [HttpGet]
        [ProducesResponseType(typeof(PaginatedResponse<InternationalShipmentResponseDto>), 200)]
        public async Task<IActionResult> GetInternationalShipments(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? statusTab = null,
            [FromQuery] string? status = null,
            [FromQuery] Guid? originWarehouseId = null,
            [FromQuery] string? search = null)
        {
            if (!IsStaff)
                return StatusCode(403, new { message = "Chỉ nhân viên mới xem được lô vận chuyển." });

            try
            {
                var result = await _shipmentService.GetListAsync(pageNumber, pageSize, statusTab, status, originWarehouseId, search);
                return Ok(new { message = "Lấy danh sách lô vận chuyển thành công.", data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = this.UserError(ex) });
            }
        }


        /// <summary>
        /// Chi tiết một lô vận chuyển quốc tế: kho đi/đến, tuyến, danh sách WRO, kiện hàng,
        /// hồ sơ PDF và chứng từ thông quan.
        /// </summary>
        [HttpGet("{shipmentId}")]
        [ProducesResponseType(typeof(InternationalShipmentResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetInternationalShipmentById(Guid shipmentId)
        {
            if (!IsStaff)
                return StatusCode(403, new { message = "Chỉ nhân viên mới xem được lô vận chuyển." });

            try
            {
                var result = await _shipmentService.GetByIdAsync(shipmentId);
                return Ok(new { message = "Lấy chi tiết lô vận chuyển thành công.", data = result });
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
        /// Staff/Operations xác nhận lô vận chuyển quốc tế đã đến kho đích (chỉ khi đang IN_TRANSIT).
        /// Cập nhật lô + các kiện sang ARRIVED_DESTINATION để sẵn sàng cho quy trình nhập kho.
        /// </summary>
        /// <summary>
        /// Biên bản kiểm đếm kiện tại kho VN — dùng cho màn theo dõi chênh lệch của OM.
        /// </summary>
        /// <param name="onlyDiscrepancy">Mặc định true: chỉ trả biên bản có lệch khai báo.</param>
        /// <param name="shipmentId">Lọc theo một lô cụ thể (tuỳ chọn).</param>
        /// <remarks>
        /// Hệ thống chỉ GHI NHẬN chênh lệch. Việc đòi hãng vận chuyển hay đền khách do OM xử lý
        /// bên ngoài, nên ở đây không có thao tác duyệt hay đóng hồ sơ.
        /// </remarks>
        /// <summary>
        /// Kho lưu kết quả đối chiếu một kiện ngay khi cân đếm xong.
        /// </summary>
        /// <remarks>
        /// Không cần đợi xác nhận cả lô: đếm tới đâu lưu tới đó, rời màn hình cũng không mất,
        /// và OM thấy được chênh lệch ngay cả khi lô chưa chốt. Gọi lại cho cùng một kiện thì
        /// ghi đè bản cũ chứ không đẻ thêm dòng.
        /// </remarks>
        [HttpPut("~/api/parcels/{parcelId}/inspection")]
        [AuthorizeRoles("Admin,ADMIN,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        [ProducesResponseType(typeof(ParcelInspectionDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> SaveParcelInspection(
            Guid parcelId, [FromBody] SaveParcelInspectionRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _shipmentService.SaveParcelInspectionAsync(
                    parcelId, request ?? new SaveParcelInspectionRequest(), userId, userRole);
                return Ok(new { message = "Đã lưu kết quả đối chiếu kiện.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        [HttpGet("~/api/parcel-inspections")]
        [AuthorizeRoles("Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Staff,STAFF,WarehouseStaff,WAREHOUSESTAFF")]
        [ProducesResponseType(typeof(ParcelInspectionListDto), 200)]
        public async Task<IActionResult> GetParcelInspections(
            [FromQuery] bool onlyDiscrepancy = true,
            [FromQuery] Guid? shipmentId = null)
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            Guid? userId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;

            try
            {
                var result = await _shipmentService.GetParcelInspectionsAsync(onlyDiscrepancy, shipmentId, userRole, userId);
                return Ok(new { message = "Lấy biên bản kiểm đếm thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
        }

        [HttpPut("{shipmentId}/arrive")]
        public async Task<IActionResult> ConfirmArrival(Guid shipmentId, [FromBody] ConfirmShipmentArrivalDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                // Nhân viên kho đã được gán kho chỉ nhận lô ở đúng kho đích được gán.
                await _userWarehouses.EnsureAssignedWarehouseAsync(
                    userId, userRole, await _userWarehouses.DestinationWarehouseOfShipmentAsync(shipmentId));
                var result = await _shipmentService.ConfirmArrivalAsync(shipmentId, userId, userRole, request ?? new ConfirmShipmentArrivalDto());
                return Ok(new { message = "Xác nhận lô đến kho đích thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
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
        /// Warehouse Staff/Staff/Operations xác nhận nhận kiện từ lô tại kho đích (chỉ khi lô đã ARRIVED_DESTINATION).
        /// Cập nhật các Parcel sang RECEIVED_AT_DESTINATION để tiếp tục nhập kho / put-away.
        /// </summary>
        [HttpPut("{shipmentId}/destination-receiving")]
        public async Task<IActionResult> ConfirmDestinationReceiving(Guid shipmentId, [FromBody] DestinationReceivingDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                // Nhân viên kho đã được gán kho chỉ nhận lô ở đúng kho đích được gán.
                await _userWarehouses.EnsureAssignedWarehouseAsync(
                    userId, userRole, await _userWarehouses.DestinationWarehouseOfShipmentAsync(shipmentId));
                var result = await _shipmentService.ConfirmDestinationReceivingAsync(shipmentId, userId, userRole, request ?? new DestinationReceivingDto());
                return Ok(new { message = "Xác nhận nhận kiện tại kho đích thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
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
    }
}
