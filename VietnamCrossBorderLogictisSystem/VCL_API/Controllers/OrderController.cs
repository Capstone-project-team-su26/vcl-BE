using VCL_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Interface;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_API.Infrastructure;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/orders")]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly IConsignmentReceiptService _consignmentReceiptService;
        private readonly IExportHoldService _exportHoldService;
        private readonly IShipmentTrackingService _trackingService;

        public OrderController(
            IOrderService orderService,
            IConsignmentReceiptService consignmentReceiptService,
            IExportHoldService exportHoldService,
            IShipmentTrackingService trackingService)
        {
            _orderService = orderService;
            _consignmentReceiptService = consignmentReceiptService;
            _exportHoldService = exportHoldService;
            _trackingService = trackingService;
        }

        /// <summary>
        /// Các đơn đang có hàng ở kho hoặc đang trên đường, kèm chặng hiện tại. Khách chỉ thấy đơn
        /// của mình; nhân viên thấy tất cả. Mặc định bỏ đơn đã về tới kho VN.
        /// </summary>
        [HttpGet("consignments/tracking")]
        public async Task<IActionResult> GetTrackedOrders([FromQuery] TrackedOrderQuery query)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            var data = await _trackingService.GetTrackedOrdersAsync(userId, role, query);
            return Ok(new { message = "Lấy danh sách đơn đang theo dõi thành công.", data });
        }

        /// <summary>
        /// Hành trình một đơn: chặng hiện tại, từng kiện, chuyến chở, mã vận đơn và dòng thời gian.
        /// Khách chỉ xem được đơn của mình; chỉ thấy lời nhắn dành cho khách, không thấy ghi chú nội bộ.
        /// </summary>
        [HttpGet("consignments/{orderId}/tracking")]
        public async Task<IActionResult> GetOrderTracking(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
            try
            {
                var data = await _trackingService.GetOrderTrackingAsync(userId, role, orderId);
                return Ok(new { message = "Lấy hành trình đơn hàng thành công.", data });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Bật/tắt giữ hàng tại kho nguồn. Hàng ký gửi mặc định đi theo chuyến gần nhất; bật cờ
        /// này thì kho không đưa kiện của đơn vào phiếu xuất mới.
        /// </summary>
        [HttpPut("consignments/{orderId}/export-hold")]
        public async Task<IActionResult> SetExportHold(Guid orderId, [FromBody] SetExportHoldRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var data = await _exportHoldService.SetHoldAsync(userId, role, orderId, request?.Hold ?? false, request?.Reason);
                return Ok(new { message = (request?.Hold ?? false) ? "Đã bật giữ hàng." : "Đã tắt giữ hàng.", data });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Khách hàng tạo yêu cầu ký gửi (A1 + A2).
        /// Tạo Order + OrderItems, đối chiếu hàng cấm/hạn chế, và sinh báo giá tạm tính ngay.
        /// </summary>
        /// <param name="request">Thông tin mặt hàng và phương án vận chuyển TQ → VN</param>
        /// <returns>Thông tin yêu cầu ký gửi kèm báo giá tạm tính và cảnh báo (nếu có)</returns>
        /// <response code="201">Tạo yêu cầu ký gửi thành công</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc chứa hàng cấm</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <summary>
        /// ƯỚC TÍNH chi phí đơn ký gửi TRƯỚC KHI TẠO — không tạo đơn, không ghi gì.
        /// </summary>
        /// <remarks>
        /// Nhận ĐÚNG payload của POST /api/orders/consignments và chạy ĐÚNG phép tính sinh ra
        /// báo giá tạm tính lúc tạo đơn, nên con số ở màn xác nhận bằng đúng con số trên hoá
        /// đơn hệ thống phát hành ngay sau đó.
        ///
        /// Vẫn kiểm đầy đủ như lúc tạo thật (tuyến, phương án vận chuyển, giới hạn kiện, hàng
        /// cấm, bắt buộc chọn cỡ thùng khi tick đóng thùng gỗ) — sai thì trả 400 ngay ở bước
        /// xem trước, khách không phải bấm tạo đơn mới biết.
        /// </remarks>
        /// <response code="200">Trả bảng ước tính: cước, phí dịch vụ, phụ phí theo kiện, thuế</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc chứa hàng cấm</response>
        [HttpPost("consignments/preview")]
        [Authorize(Roles = "Customer")]
        [ProducesResponseType(typeof(EstimateQuotationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> PreviewConsignment([FromBody] CreateConsignmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _orderService.PreviewConsignmentAsync(request, userId);
                return Ok(new { message = "Ước tính chi phí đơn ký gửi thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = this.UserError(ex) }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        [HttpPost("consignments")]
        [Authorize(Roles = "Customer")]
        [ProducesResponseType(typeof(object), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> CreateConsignment([FromBody] CreateConsignmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _orderService.CreateConsignmentAsync(request, userId);
                return CreatedAtAction(nameof(GetConsignments), new { },
                    new { message = "Tạo yêu cầu ký gửi thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Sales tạo yêu cầu ký gửi thay cho Customer.
        /// </summary>
        /// <param name="request">Thông tin mặt hàng, khách hàng và phương án vận chuyển</param>
        /// <returns>Thông tin yêu cầu ký gửi kèm báo giá tạm tính và cảnh báo (nếu có)</returns>
        /// <response code="201">Tạo yêu cầu ký gửi thành công</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc chứa hàng cấm</response>
        /// <response code="401">Chưa đăng nhập hoặc không phải nhân viên Sales</response>
        /// <response code="404">Không tìm thấy Customer</response>
        /// <summary>
        /// ƯỚC TÍNH chi phí đơn Sale tạo hộ khách, TRƯỚC KHI tạo — không tạo đơn, không ghi gì.
        /// </summary>
        /// <remarks>
        /// Cùng payload và cùng phép tính với POST /api/staff/consignments, nên con số Sale
        /// đọc cho khách nghe ở màn xác nhận bằng đúng con số trên báo giá hệ thống phát hành.
        /// </remarks>
        /// <response code="200">Trả bảng ước tính</response>
        /// <response code="400">Dữ liệu không hợp lệ hoặc chứa hàng cấm</response>
        /// <response code="404">Không tìm thấy khách hàng</response>
        [HttpPost("~/api/staff/consignments/preview")]
        [Authorize(Roles = "Sale")]
        [ProducesResponseType(typeof(EstimateQuotationResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> PreviewConsignmentByStaff([FromBody] CreateConsignmentByStaffRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var staffUserId))
                return Unauthorized(new { message = "Không thể xác định người dùng nhân viên." });

            try
            {
                var result = await _orderService.PreviewConsignmentByStaffAsync(request, staffUserId);
                return Ok(new { message = "Ước tính chi phí đơn ký gửi thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = this.UserError(ex) }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        [HttpPost("~/api/staff/consignments")]
        [Authorize(Roles = "Sale")]
        [ProducesResponseType(typeof(CreateConsignmentResponse), 201)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CreateConsignmentByStaff([FromBody] CreateConsignmentByStaffRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var staffUserId))
                return Unauthorized(new { message = "Không thể xác định người dùng nhân viên." });

            try
            {
                var result = await _orderService.CreateConsignmentByStaffAsync(request, staffUserId);
                return CreatedAtAction(nameof(GetConsignmentDetail), new { orderId = result.OrderId },
                    new { message = "Tạo yêu cầu ký gửi thay khách hàng thành công.", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Kiểm tra danh sách hàng hóa với danh mục hàng cấm/hạn chế (chỉ kiểm tra, KHÔNG tạo Order).
        /// Dùng để cảnh báo sớm khi khách đang nhập hàng. Hàng cấm → canCreate=false; hàng hạn chế → vẫn cho tạo nhưng cảnh báo.
        /// </summary>
        /// <param name="request">Danh sách mặt hàng cần kiểm tra</param>
        /// <returns>canCreate + danh sách cảnh báo theo từng mặt hàng (BANNED/RESTRICTED/WARNING)</returns>
        /// <response code="200">Kiểm tra thành công (kể cả khi có hàng cấm — xem canCreate)</response>
        /// <response code="400">Danh sách mặt hàng rỗng</response>
        /// <response code="401">Chưa đăng nhập</response>
        [HttpPost("consignments/validate-items")]
        [AuthorizeRoles("Customer,Admin,Sale,WarehouseStaff,OperationsManager,Delivery")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> ValidateItems([FromBody] ValidateItemsRequest request)
        {
            try
            {
                var result = await _orderService.ValidateConsignmentItemsAsync(request);
                return Ok(new { message = "Kiểm tra hàng hóa thành công.", data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Khách hàng hủy yêu cầu ký gửi của chính mình.
        /// Chỉ hủy được khi hàng chưa vào kho VN (PENDING_REVIEW, APPROVED, WAITING_FOR_PARCEL).
        /// Tự động hủy các báo giá liên quan.
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <param name="request">Lý do hủy (tùy chọn)</param>
        /// <returns>Kết quả hủy</returns>
        /// <response code="200">Hủy thành công</response>
        /// <response code="400">Trạng thái không cho phép hủy</response>
        /// <response code="401">Chưa đăng nhập hoặc không phải chủ đơn</response>
        /// <response code="404">Không tìm thấy yêu cầu ký gửi</response>
        [HttpPut("consignments/{orderId}/cancel")]
        [Authorize(Roles = "Customer")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CancelConsignment(Guid orderId, [FromBody] CancelConsignmentRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _orderService.CancelConsignmentAsync(orderId, userId, request ?? new CancelConsignmentRequest());
                return Ok(new
                {
                    message = "Hủy yêu cầu ký gửi thành công.",
                    status = result.Status,
                    consignmentCode = result.ConsignmentCode,
                    data = result
                });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Khách xác nhận đã nhận đủ hàng (bước 10 của luồng ký gửi).
        /// </summary>
        /// <remarks>
        /// Chỉ gọi được khi hãng giao nội địa đã báo giao xong (đơn ở <c>DELIVERED</c>).
        /// Xác nhận xong đơn sang <c>CUSTOMER_CONFIRMED</c>, chờ OM chốt hoàn thành.
        /// </remarks>
        [HttpPut("consignments/{orderId}/customer-confirm")]
        [Authorize(Roles = "Customer")]
        [ProducesResponseType(typeof(OrderClosingResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ConfirmReceivedByCustomer(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _orderService.ConfirmReceivedByCustomerAsync(orderId, userId);
                return Ok(new { message = "Đã ghi nhận xác nhận nhận hàng của khách.", data = result });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// OM chốt đơn hoàn thành (bước 11 — bước cuối của luồng ký gửi).
        /// </summary>
        /// <remarks>
        /// Cần khách xác nhận trước. Khách không bấm gì thì sau 1 ngày kể từ lúc giao xong,
        /// OM được chốt thay.
        /// </remarks>
        [HttpPut("consignments/{orderId}/complete")]
        [Authorize(Roles = "Admin,OperationsManager,Operations,Administrator,Manager")]
        [ProducesResponseType(typeof(OrderClosingResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CompleteConsignment(Guid orderId, [FromBody] CompleteConsignmentRequestDto? request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _orderService.CompleteConsignmentAsync(orderId, userId, userRole, request);
                return Ok(new { message = "Đơn hàng đã hoàn thành.", data = result });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Sale từ chối đơn vì khách quá hạn không thanh toán đợt cuối. Tiền cọc không hoàn lại.
        /// </summary>
        /// <remarks>
        /// Giao dịch cọc giữ nguyên trạng thái đã thanh toán vì tiền đã thực sự về tài khoản;
        /// đợt cuối đang treo (nếu có) sẽ bị huỷ để khách không quét mã trả sau khi đơn đã đóng.
        /// </remarks>
        [HttpPut("consignments/{orderId}/reject-unpaid")]
        [Authorize(Roles = "Admin,Administrator,Sale,Sales,Staff,OperationsManager,Operations")]
        [ProducesResponseType(typeof(OrderClosingResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RejectUnpaidConsignment(Guid orderId, [FromBody] RejectUnpaidConsignmentRequestDto? request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _orderService.RejectUnpaidConsignmentAsync(orderId, userId, userRole, request);
                return Ok(new { message = "Đã từ chối đơn do quá hạn thanh toán.", data = result });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Hàng đợi "Hàng chờ tất toán" của Sale: đơn kho đã kiểm xong, khách chưa trả nốt.
        /// </summary>
        /// <remarks>Gồm cả đơn ký gửi lẫn đơn mua hộ.</remarks>
        [HttpGet("awaiting-settlement")]
        [Authorize]
        [ProducesResponseType(typeof(SettlementQueueResponse), 200)]
        public async Task<IActionResult> GetAwaitingSettlement()
        {
            try
            {
                var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
                Guid? actorId = Guid.TryParse(
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;
                var result = await _orderService.GetSettlementQueueAsync(role, settled: false, actorId);
                return Ok(new { message = "Lấy danh sách hàng chờ tất toán thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Hàng đợi "Đơn hàng cần xử lý": mỗi dòng một việc, tách theo nhóm kiện.
        /// </summary>
        /// <remarks>
        /// Đơn vừa có kiện giao thẳng vừa có kiện gửi kho sẽ ra hai dòng với hai nút khác nhau.
        /// </remarks>
        [HttpGet("action-queue")]
        [Authorize(Roles = "Admin,ADMIN,Sale,SALE,Sales,SALES,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS")]
        [ProducesResponseType(typeof(OrderActionQueueResponse), 200)]
        public async Task<IActionResult> GetActionQueue()
        {
            try
            {
                var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
                var result = await _orderService.GetActionQueueAsync(role);
                return Ok(new { message = "Lấy danh sách đơn cần xử lý thành công.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Sale báo cho kho rằng đơn này có hàng khách xin gửi lại, kho vào lập phiếu nhập kho.
        /// </summary>
        /// <remarks>
        /// Đứng sau bước khách tất toán. Kho chỉ lập được phiếu nhập kho sau khi có báo này.
        /// Đơn mua hộ không dùng endpoint này — hàng mua hộ vào kho qua bước OM duyệt riêng.
        /// </remarks>
        [HttpPut("consignments/{orderId}/notify-warehouse")]
        [Authorize(Roles = "Admin,ADMIN,Sale,SALE,Sales,SALES")]
        [ProducesResponseType(typeof(NotifyWarehouseResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> NotifyWarehouse(
            Guid orderId, [FromBody] NotifyWarehouseRequest request)
        {
            var actorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(actorIdClaim, out var actorUserId))
            {
                return Unauthorized(new { message = "Không xác định được danh tính người dùng." });
            }

            try
            {
                var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
                var result = await _orderService.NotifyWarehouseAsync(
                    orderId, actorUserId, role, request ?? new NotifyWarehouseRequest());
                return Ok(new { message = result.AlreadyNotified ? "Đơn này đã được báo cho kho từ trước." : "Đã thông báo cho kho.", data = result });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = this.UserError(ex) }); }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = this.UserError(ex) }); }
        }

        /// <summary>
        /// Staff cập nhật trạng thái yêu cầu ký gửi
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <param name="request">Thông tin trạng thái cần cập nhật và lý do từ chối (nếu có)</param>
        /// <returns>Kết quả cập nhật trạng thái</returns>
        [HttpPut("consignments/{orderId}/status")]
        [Authorize(Roles = "Admin,ADMIN,Sale,SALE,Sales,SALES")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateConsignmentStatus(Guid orderId, [FromBody] UpdateConsignmentStatusRequest request)
        {
            try
            {
                var actorIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                Guid? actorUserId = Guid.TryParse(actorIdClaim, out var parsedActorId) ? parsedActorId : null;

                var result = await _orderService.UpdateConsignmentStatusAsync(orderId, request, actorUserId);
                return Ok(new 
                { 
                    message = "Cập nhật trạng thái thành công.",
                    status = result.Status,
                    consignmentCode = result.ConsignmentCode,
                    receiptPdfUrl = result.ReceiptPdfUrl
                });
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
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Tải phiếu xác nhận ký gửi (PDF) của đơn. Dùng khi cần xem lại hoặc sinh lại phiếu
        /// mà không phải đổi trạng thái đơn.
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <response code="200">Trả về file PDF</response>
        /// <response code="401">Chưa đăng nhập</response>
        /// <response code="404">Không tìm thấy yêu cầu ký gửi</response>
        [HttpGet("consignments/{orderId}/receipt")]
        [AuthorizeRoles("Admin,Sale,WarehouseStaff,OperationsManager")]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DownloadConsignmentReceipt(Guid orderId)
        {
            try
            {
                // Không truyền user đang đăng nhập vào đây: người lập phiếu là người đã phát hành
                // phiếu, đọc từ đơn. Truyền người đang xem thì kho tải phiếu về sẽ thấy tên mình
                // ở ô người lập, lệch với bản khách và kho nhận qua link công khai.
                var (content, fileName) = await _consignmentReceiptService.GetReceiptByOrderIdAsync(orderId);
                return File(content, "application/pdf", fileName);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Không thể tạo phiếu xác nhận ký gửi.", error = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Xem chi tiết một yêu cầu ký gửi.
        /// Customer chỉ xem được đơn của chính mình; các role nội bộ xem được mọi đơn.
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <returns>Chi tiết đầy đủ: header + khách hàng + mặt hàng + báo giá mới nhất</returns>
        /// <response code="200">Lấy chi tiết thành công</response>
        /// <response code="401">Chưa đăng nhập hoặc không phải chủ đơn</response>
        /// <response code="404">Không tìm thấy yêu cầu ký gửi</response>
        [HttpGet("consignments/{orderId}/timeline")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetOrderTimeline(Guid orderId)
        {
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized(new { message = "Không thể xác định người dùng." });

            try
            {
                var result = await _orderService.GetOrderTimelineAsync(orderId, userId, User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty);
                return Ok(new { message = "Lấy lịch sử đơn thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = this.UserError(ex) });
            }
        }

        [HttpGet("consignments/{orderId}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetConsignmentDetail(Guid orderId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedId))
                userId = parsedId;

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _orderService.GetConsignmentDetailAsync(orderId, userId, role);
                return Ok(new { message = "Lấy chi tiết yêu cầu ký gửi thành công.", data = result });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Lấy danh sách yêu cầu ký gửi
        /// </summary>
        /// <param name="pageNumber">Số trang (mặc định 1)</param>
        /// <param name="pageSize">Số lượng item mỗi trang (mặc định 10)</param>
        /// <param name="status">Trạng thái yêu cầu (tuỳ chọn)</param>
        /// <param name="searchCode">Mã yêu cầu ký gửi để tìm kiếm (tuỳ chọn)</param>
        /// <param name="orderType">
        /// Lọc theo loại đơn kho: <c>CONSIGNMENT</c> hoặc <c>PURCHASE</c>. Bỏ trống trả về cả hai.
        /// Màn ký gửi phải truyền <c>CONSIGNMENT</c>, không thì đơn mua hộ (mã PUR-) hiện lẫn vào danh sách.
        /// </param>
        /// <returns>Danh sách yêu cầu ký gửi phân trang</returns>
        [HttpGet("consignments")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetConsignments(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null,
            [FromQuery] string? searchCode = null,
            [FromQuery] string? orderType = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var parsedId))
            {
                userId = parsedId;
            }

            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            try
            {
                var result = await _orderService.GetConsignmentsAsync(userId, role, pageNumber, pageSize, status, searchCode, orderType);
                return Ok(new
                {
                    message = "Lấy danh sách yêu cầu ký gửi thành công.",
                    data = result
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = this.UserError(ex) });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Lấy danh sách tuyến hàng (route) để FE đổ dropdown khi tạo yêu cầu ký gửi.
        /// </summary>
        [HttpGet("consignments/routes")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        public async Task<IActionResult> GetConsignmentRoutes()
        {
            var data = await _orderService.GetConsignmentRoutesAsync();
            return Ok(new { message = "Lấy danh sách tuyến hàng thành công.", data });
        }

        /// <summary>
        /// Lấy danh sách phương án vận chuyển (shippingOption) để FE đổ dropdown.
        /// Có thể lọc theo route qua query param.
        /// </summary>
        [HttpGet("consignments/shipping-options")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        public async Task<IActionResult> GetShippingOptions([FromQuery] string? route = null)
        {
            var data = await _orderService.GetShippingOptionsAsync(route);
            return Ok(new { message = "Lấy danh sách phương án vận chuyển thành công.", data });
        }

        /// <summary>
        /// Dịch vụ khách chọn cho từng kiện khi tạo đơn ký gửi (kiểm hàng, bảo hiểm, đóng lại carton...).
        /// Gửi ID vào <c>items[].services[].pricingRuleId</c>. Thùng gỗ không nằm ở đây — chọn qua
        /// <c>items[].packageConfigurationId</c>.
        /// </summary>
        /// <param name="route">Tuyến hàng (tuỳ chọn, đi cùng shippingOption)</param>
        /// <param name="shippingOption">Phương án vận chuyển (tuỳ chọn, đi cùng route)</param>
        [HttpGet("consignments/item-services")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<ItemServiceDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> GetItemServices([FromQuery] string? route = null, [FromQuery] string? shippingOption = null)
        {
            try
            {
                var data = await _orderService.GetItemServicesAsync(route, shippingOption);
                return Ok(new { message = "Lấy danh sách dịch vụ theo kiện thành công.", data });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = this.UserError(ex) });
            }
        }

        /// <summary>
        /// Lấy danh sách toàn bộ trạng thái yêu cầu ký gửi (code + nhãn) để FE lọc/hiển thị.
        /// </summary>
        [HttpGet("consignments/statuses")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(object), 200)]
        public IActionResult GetConsignmentStatuses()
        {
            var data = _orderService.GetConsignmentStatuses();
            return Ok(new { message = "Lấy danh sách trạng thái yêu cầu ký gửi thành công.", data });
        }

    }
}
