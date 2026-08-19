using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/international-shipments")]
    [ApiController]
    [Authorize]
    public class InternationalShipmentsController : ControllerBase
    {
        private readonly IInternationalShipmentService _shipmentService;

        public InternationalShipmentsController(IInternationalShipmentService shipmentService)
        {
            _shipmentService = shipmentService;
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
            try
            {
                var result = await _shipmentService.GetListAsync(pageNumber, pageSize, statusTab, status, originWarehouseId, search);
                return Ok(new { message = "Lấy danh sách lô vận chuyển thành công.", data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Phiếu xuất kho còn gom lô được: kho đã bốc xong và phiếu còn kiện chưa lên chuyến nào.
        /// </summary>
        /// <remarks>
        /// Màn gom lô không tự lọc được bằng trạng thái phiếu: gom lô xong phiếu vẫn đứng nguyên
        /// ở <c>PICKED</c> vì mọi mốc sau đó thuộc về lô. Endpoint này trừ sẵn phần đã lên chuyến
        /// nên màn hình khỏi phải tải toàn bộ lô về rồi tự đối chiếu.
        /// </remarks>
        /// <param name="originWarehouseId">Chỉ lấy phiếu đang giữ hàng ở kho này</param>
        /// <param name="search">Tìm theo mã phiếu, tên khách hoặc tên người nhận</param>
        [HttpGet("available-wro-requests")]
        [ProducesResponseType(typeof(List<AvailableWroForShipmentDto>), 200)]
        public async Task<IActionResult> GetAvailableWroRequests(
            [FromQuery] Guid? originWarehouseId = null,
            [FromQuery] string? search = null)
        {
            try
            {
                var result = await _shipmentService.GetAvailableWroRequestsAsync(originWarehouseId, search);
                return Ok(new { message = "Lấy danh sách phiếu xuất kho chờ gom lô thành công.", data = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
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
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Nhân viên Kho Quốc tế (hoặc Staff/Operations) gom KIỆN đã bàn giao thành lô vận chuyển quốc tế
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateInternationalShipment([FromBody] CreateInternationalShipmentDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _shipmentService.CreateAsync(userId, userRole, request);
                return Created(string.Empty, new { message = "Tạo lô vận chuyển quốc tế thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật trạng thái lô: CREATED → READY_TO_SHIP → IN_TRANSIT → ARRIVED_VN → ARRIVED_DESTINATION.
        /// </summary>
        /// <remarks>
        /// Hai mốc đầu là của kho (gom xong / đối tác đã lấy hàng), hai mốc sau của Sale
        /// (đã về VN / đã về tới kho). ARRIVED_DESTINATION đi qua đúng thân xử lý của
        /// <c>PUT /{shipmentId}/arrive</c> nên cập nhật cả lô lẫn từng kiện.
        /// </remarks>
        [HttpPut("{shipmentId}/status")]
        public async Task<IActionResult> UpdateStatus(Guid shipmentId, [FromBody] UpdateInternationalShipmentStatusDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Không thể xác định danh tính người dùng." });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _shipmentService.UpdateStatusAsync(shipmentId, userId, userRole, request);
                return Ok(new { message = "Cập nhật trạng thái lô vận chuyển thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
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
        [Authorize(Roles = "Admin,ADMIN,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
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
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("~/api/parcel-inspections")]
        [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS,Staff,STAFF,WarehouseStaff,WAREHOUSESTAFF")]
        [ProducesResponseType(typeof(ParcelInspectionListDto), 200)]
        public async Task<IActionResult> GetParcelInspections(
            [FromQuery] bool onlyDiscrepancy = true,
            [FromQuery] Guid? shipmentId = null)
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _shipmentService.GetParcelInspectionsAsync(onlyDiscrepancy, shipmentId, userRole);
                return Ok(new { message = "Lấy biên bản kiểm đếm thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
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
                var result = await _shipmentService.ConfirmArrivalAsync(shipmentId, userId, userRole, request ?? new ConfirmShipmentArrivalDto());
                return Ok(new { message = "Xác nhận lô đến kho đích thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
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
                var result = await _shipmentService.ConfirmDestinationReceivingAsync(shipmentId, userId, userRole, request ?? new DestinationReceivingDto());
                return Ok(new { message = "Xác nhận nhận kiện tại kho đích thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }
    }
}
