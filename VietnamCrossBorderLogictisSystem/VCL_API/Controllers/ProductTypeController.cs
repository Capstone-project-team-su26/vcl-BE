using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/product-types")]
    [ApiController]
    public class ProductTypeController : ControllerBase
    {
        private readonly IProductTypeService _productTypeService;

        public ProductTypeController(IProductTypeService productTypeService)
        {
            _productTypeService = productTypeService;
        }

        /// <summary>
        /// Lấy danh sách loại hàng đang hoạt động để FE đổ dropdown.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetProductTypes()
        {
            var data = await _productTypeService.GetActiveProductTypesAsync();
            return Ok(new { message = "Lấy danh sách loại hàng thành công.", data });
        }
    }
}
