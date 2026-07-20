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
    }

    public class SuggestPackageConfigRequest
    {
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal Weight { get; set; }
    }
}
