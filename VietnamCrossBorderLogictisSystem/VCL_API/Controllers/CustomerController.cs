using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin,Sale")]
    public class CustomerController : ControllerBase
    {
        private readonly ICustomerService _customerService;

        public CustomerController(ICustomerService customerService)
        {
            _customerService = customerService;
        }

        /// <summary>
        /// Lấy danh sách khách hàng. Hỗ trợ tìm kiếm theo tên, mã, email, SĐT, địa chỉ, công ty.
        /// </summary>
        [HttpGet("api/customers")]
        public async Task<IActionResult> GetAllCustomers([FromQuery] string? search)
        {
            var result = await _customerService.GetAllCustomersAsync(search);
            return Ok(new
            {
                items = result
            });
        }

        /// <summary>
        /// Lấy thông tin chi tiết một khách hàng.
        /// </summary>
        [HttpGet("api/customers/{id}")]
        public async Task<IActionResult> GetCustomerById(Guid id)
        {
            try
            {
                var result = await _customerService.GetCustomerByIdAsync(id);
                return Ok(result);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo hồ sơ khách hàng mới.
        /// </summary>
        [HttpPost("api/customers")]
        public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerRequestDto request)
        {
            try
            {
                var result = await _customerService.CreateCustomerAsync(request);
                return CreatedAtAction(nameof(GetCustomerById), new { id = result.Id }, new
                {
                    message = "Tạo hồ sơ khách hàng thành công.",
                    customer = result
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật hồ sơ khách hàng.
        /// </summary>
        [HttpPut("api/customers/{id}")]
        public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] UpdateCustomerRequestDto request)
        {
            try
            {
                var result = await _customerService.UpdateCustomerAsync(id, request);
                return Ok(new
                {
                    message = "Cập nhật hồ sơ khách hàng thành công.",
                    customer = result
                });
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

        /// <summary>
        /// Vô hiệu hóa (ngừng hoạt động) hồ sơ khách hàng — soft-delete để giữ lịch sử đơn.
        /// </summary>
        [HttpDelete("api/customers/{id}")]
        public async Task<IActionResult> DeactivateCustomer(Guid id)
        {
            try
            {
                await _customerService.DeactivateCustomerAsync(id);
                return Ok(new { message = "Vô hiệu hóa hồ sơ khách hàng thành công." });
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
    }
}
