using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GoshipController : ControllerBase
    {
        private readonly IGoshipService _goshipService;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public GoshipController(
            IGoshipService goshipService,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            _goshipService = goshipService;
            _configuration = configuration;
            _environment = environment;
        }

        /// <summary>Token dùng chung cho các endpoint tra danh mục.</summary>
        private string? ResolveToken(string? token) =>
            !string.IsNullOrWhiteSpace(token) ? token : _configuration["GoshipSettings:Token"];

        /// <summary>
        /// Danh mục tỉnh/thành của đơn vị vận chuyển.
        /// <para>
        /// FE dùng ba endpoint danh mục này để dựng dropdown phụ thuộc nhau cho màn lập yêu cầu
        /// giao hàng. Chọn từ danh mục thì địa chỉ luôn khớp mã GoShip, khỏi phụ thuộc vào việc
        /// đoán tên do người dùng gõ tay.
        /// </para>
        /// </summary>
        [HttpGet("cities")]
        [Authorize]
        public async Task<IActionResult> GetCities([FromQuery] string? token = null)
        {
            var effectiveToken = ResolveToken(token);
            if (string.IsNullOrEmpty(effectiveToken))
            {
                return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
            }

            try
            {
                var cities = await _goshipService.GetCitiesAsync(effectiveToken);
                return Ok(new { message = "Lấy danh sách tỉnh/thành thành công.", items = cities });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không lấy được danh sách tỉnh/thành.", error = ex.Message });
            }
        }

        /// <summary>Quận/huyện thuộc một tỉnh/thành.</summary>
        [HttpGet("cities/{cityId}/districts")]
        [Authorize]
        public async Task<IActionResult> GetDistricts(string cityId, [FromQuery] string? token = null)
        {
            var effectiveToken = ResolveToken(token);
            if (string.IsNullOrEmpty(effectiveToken))
            {
                return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
            }

            try
            {
                var districts = await _goshipService.GetDistrictsAsync(cityId, effectiveToken);
                return Ok(new { message = "Lấy danh sách quận/huyện thành công.", items = districts });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không lấy được danh sách quận/huyện.", error = ex.Message });
            }
        }

        /// <summary>Phường/xã thuộc một quận/huyện.</summary>
        [HttpGet("districts/{districtId}/wards")]
        [Authorize]
        public async Task<IActionResult> GetWards(string districtId, [FromQuery] string? token = null)
        {
            var effectiveToken = ResolveToken(token);
            if (string.IsNullOrEmpty(effectiveToken))
            {
                return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
            }

            try
            {
                var wards = await _goshipService.GetWardsAsync(districtId, effectiveToken);
                return Ok(new { message = "Lấy danh sách phường/xã thành công.", items = wards });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Không lấy được danh sách phường/xã.", error = ex.Message });
            }
        }

        /// <summary>
        /// Ước tính và so sánh phí giao hàng của tất cả các hãng vận chuyển qua GoShip Express.
        /// </summary>
        [HttpPost("rates")]
        [AllowAnonymous]
        public async Task<IActionResult> GetRates([FromBody] GoshipRateRequest request, [FromQuery] string? token = null)
        {
            try
            {
                var userRegion = User.FindFirst("region")?.Value;
                if (string.IsNullOrEmpty(userRegion) && Request.Headers.TryGetValue("x-region", out var headerRegion))
                {
                    userRegion = headerRegion.ToString();
                }

                if (!string.IsNullOrEmpty(userRegion) && !userRegion.Equals("VN", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = $"GoShip Express chỉ hỗ trợ dịch vụ vận chuyển chặng nội địa tại Việt Nam (VN). Khu vực kho/người dùng hiện tại ('{userRegion}') không được hỗ trợ." });
                }

                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GoshipSettings:Token"];
                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
                }

                var rates = await _goshipService.GetRatesAsync(request, effectiveToken);
                return Ok(new { message = "Lấy danh sách bảng giá cước GoShip thành công.", data = rates });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Đăng đơn tự động sang GoShip Express.
        /// </summary>
        [HttpPost("orders")]
        [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,Sale,SALE,Customer,CUSTOMER")]
        public async Task<IActionResult> CreateOrder([FromBody] GoshipOrderRequest request, [FromQuery] string? token = null)
        {
            try
            {
                var userRegion = User.FindFirst("region")?.Value;
                if (string.IsNullOrEmpty(userRegion) && Request.Headers.TryGetValue("x-region", out var headerRegion))
                {
                    userRegion = headerRegion.ToString();
                }

                if (!string.IsNullOrEmpty(userRegion) && !userRegion.Equals("VN", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = $"GoShip Express chỉ hỗ trợ dịch vụ vận chuyển chặng nội địa tại Việt Nam (VN). Khu vực kho/người dùng hiện tại ('{userRegion}') không được hỗ trợ." });
                }

                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GoshipSettings:Token"];
                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
                }

                var order = await _goshipService.CreateOrderAsync(request, effectiveToken);
                return Ok(new { message = "Tạo đơn hàng GoShip thành công.", data = order });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Nhân viên kho bấm đồng bộ trạng thái vận đơn với GoShip.
        /// </summary>
        /// <remarks>
        /// Sandbox GoShip không bắn webhook nên đây là đường duy nhất để biết hàng đã tới đâu.
        /// Vận đơn phải được tạo qua <c>POST /api/goship/orders</c> kèm <c>orderId</c> thì mới
        /// có bản ghi trong hệ thống để đồng bộ.
        /// </remarks>
        [HttpPost("shipments/{trackingCode}/sync")]
        [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF,Sale,SALE")]
        public async Task<IActionResult> SyncShipmentStatus(string trackingCode, [FromQuery] string? token = null)
        {
            try
            {
                var effectiveToken = !string.IsNullOrEmpty(token) ? token : _configuration["GoshipSettings:Token"];
                if (string.IsNullOrEmpty(effectiveToken))
                {
                    return BadRequest(new { message = "Chưa cấu hình Goship Access Token trong hệ thống." });
                }

                var result = await _goshipService.SyncShipmentStatusAsync(trackingCode, effectiveToken);
                return Ok(new { message = "Đồng bộ trạng thái vận đơn GoShip thành công.", data = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Đặt tay trạng thái một vận đơn, dùng để chạy thử luồng giao hàng.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Sandbox GoShip giữ đơn ở "Đơn mới" và không bắn webhook, nên không có cách nào chạy
        /// hết chặng giao bằng dữ liệu thật. Endpoint này đi đúng đường mà webhook đi — cùng
        /// hàm áp trạng thái, cùng quy tắc đổi trạng thái kiện và đơn — chỉ khác là người bấm
        /// thay vì hãng vận chuyển gọi.
        /// </para>
        /// <para>
        /// Khoá ở môi trường Production: ở đó trạng thái phải do hãng vận chuyển quyết, đặt tay
        /// là ghi đè sự thật. Muốn mở (ví dụ môi trường staging dùng cấu hình Production) thì
        /// bật <c>GoshipSettings:AllowStatusSimulation</c>.
        /// </para>
        /// </remarks>
        [HttpPost("shipments/{trackingCode}/simulate")]
        [Authorize(Roles = "Admin,ADMIN,OperationsManager,OPERATIONSMANAGER,Sale,SALE,Sales,SALES,WarehouseStaff,WAREHOUSESTAFF,Staff,STAFF")]
        public async Task<IActionResult> SimulateShipmentStatus(
            string trackingCode, [FromBody] SimulateGoshipStatusDto request)
        {
            var explicitlyAllowed = _configuration.GetValue<bool>("GoshipSettings:AllowStatusSimulation");
            if (_environment.IsProduction() && !explicitlyAllowed)
            {
                return BadRequest(new
                {
                    message = "Không đặt tay trạng thái vận đơn ở môi trường thật. "
                        + "Trạng thái phải do đơn vị vận chuyển cập nhật."
                });
            }

            if (request == null || request.StatusCode <= 0)
            {
                return BadRequest(new { message = "Thiếu mã trạng thái GoShip cần đặt." });
            }

            try
            {
                var applied = await _goshipService.ProcessWebhookAsync(new GoshipWebhookPayload
                {
                    Code = trackingCode,
                    Status = request.StatusCode,
                    StatusText = request.StatusText ?? string.Empty,
                });

                if (!applied)
                {
                    return NotFound(new
                    {
                        message = $"Không tìm thấy vận đơn {trackingCode} trong hệ thống."
                    });
                }

                return Ok(new
                {
                    message = "Đã đặt trạng thái vận đơn.",
                    data = new { trackingCode, request.StatusCode, request.StatusText }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Webhook tiếp nhận tự động cập nhật trạng thái đơn từ GoShip Express.
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> ProcessWebhook(
            [FromBody] GoshipWebhookPayload payload,
            [FromQuery] string? secret = null)
        {
            // Webhook mở cho mọi người gọi được, mà nội dung của nó lật trạng thái kiện sang
            // DELIVERED. Có cấu hình secret thì bắt buộc khớp; chưa cấu hình thì vẫn cho qua để
            // môi trường đang chạy không đứt, nhưng ghi log cảnh báo.
            var expectedSecret = _configuration["GoshipSettings:WebhookSecret"];
            if (!string.IsNullOrWhiteSpace(expectedSecret))
            {
                var provided = !string.IsNullOrWhiteSpace(secret)
                    ? secret
                    : Request.Headers["X-Goship-Secret"].ToString();

                if (!string.Equals(provided, expectedSecret, StringComparison.Ordinal))
                {
                    return Unauthorized(new { code = 401, status = "error", message = "Webhook secret không hợp lệ." });
                }
            }

            try
            {
                await _goshipService.ProcessWebhookAsync(payload);
                return Ok(new { code = 200, status = "success", message = "Webhook processed successfully" });
            }
            catch (Exception ex)
            {
                // Trước đây lỗi bị nuốt rồi vẫn trả 200 "success": GoShip tưởng đã nhận xong nên
                // không gửi lại, còn mình thì mất luôn cập nhật mà không ai biết. Trả 500 để bên
                // kia retry và để lỗi hiện ra trong log.
                return StatusCode(500, new { code = 500, status = "error", message = ex.Message });
            }
        }
    }
}
