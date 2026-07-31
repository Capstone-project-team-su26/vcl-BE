using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/package-configurations")]
    public class PackageConfigurationController : ControllerBase
    {
        private readonly IPackageConfigurationService _packageConfigService;

        public PackageConfigurationController(IPackageConfigurationService packageConfigService)
        {
            _packageConfigService = packageConfigService;
        }

        /// <summary>
        /// Lấy danh sách các thùng đóng gói tiêu chuẩn đang hoạt động.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetActive()
        {
            var configs = await _packageConfigService.GetActiveConfigurationsAsync();
            return Ok(configs);
        }

        /// <summary>
        /// Lấy danh sách tất cả tùy chọn loại kiện đóng gói (kết hợp PackageConfiguration &amp; PricingRule)
        /// phục vụ Mobile UI render danh sách loại kiện xuất kho động thay vì nhập tay.
        /// </summary>
        [HttpGet("options")]
        [Authorize]
        public async Task<IActionResult> GetPackagingOptions()
        {
            var options = await _packageConfigService.GetPackagingOptionsAsync();
            return Ok(options);
        }

        /// <summary>
        /// Gợi ý loại thùng phù hợp nhất dựa trên kích thước thực tế của kiện hàng.
        /// </summary>
        [HttpPost("suggest")]
        [Authorize]
        public async Task<IActionResult> SuggestBox([FromBody] SuggestPackageConfigRequest request)
        {
            if (request == null)
            {
                return BadRequest("Dữ liệu kích thước không hợp lệ.");
            }

            var suggestion = await _packageConfigService.SuggestConfigurationAsync(
                request.Length, request.Width, request.Height, request.Weight);

            return Ok(suggestion);
        }

        /// <summary>
        /// Lấy chi tiết một cấu hình thùng theo ID.
        /// </summary>
        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetById(Guid id)
        {
            try
            {
                var config = await _packageConfigService.GetByIdAsync(id);
                return Ok(config);
            }
            catch (VCL_BLL.Exceptions.NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Admin tạo cấu hình thùng đóng gói mới.
        /// Thùng tiêu chuẩn: packageFee = phí cố định/thùng.
        /// Thùng CUSTOM: packageFee = đơn giá VND cho mỗi 1.000 cm³ thể tích khách nhập.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "ADMIN,Admin")]
        public async Task<IActionResult> Create([FromBody] SavePackageConfigurationRequest request)
        {
            try
            {
                var created = await _packageConfigService.CreateAsync(request);
                return StatusCode(201, new { message = "Tạo cấu hình thùng thành công.", data = created });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Admin cập nhật cấu hình thùng (kích thước, phí đóng gói, trạng thái).
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "ADMIN,Admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] SavePackageConfigurationRequest request)
        {
            try
            {
                var updated = await _packageConfigService.UpdateAsync(id, request);
                return Ok(new { message = "Cập nhật cấu hình thùng thành công.", data = updated });
            }
            catch (VCL_BLL.Exceptions.NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Admin ngưng sử dụng thùng (chuyển INACTIVE — không xóa cứng vì đơn cũ còn tham chiếu).
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "ADMIN,Admin")]
        public async Task<IActionResult> Deactivate(Guid id)
        {
            try
            {
                await _packageConfigService.DeactivateAsync(id);
                return Ok(new { message = "Đã ngưng sử dụng cấu hình thùng." });
            }
            catch (VCL_BLL.Exceptions.NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }
    }

    public class SuggestPackageConfigRequest
    {
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal Weight { get; set; }
    }
}
