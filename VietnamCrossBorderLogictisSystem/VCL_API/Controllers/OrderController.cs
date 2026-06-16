using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.Interface;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,Sale,WarehouseStaff,OperationsManager,Delivery")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        /// <summary>
        /// Staff cập nhật trạng thái yêu cầu ký gửi
        /// </summary>
        /// <param name="orderId">ID của yêu cầu ký gửi</param>
        /// <param name="request">Thông tin trạng thái cần cập nhật và lý do từ chối (nếu có)</param>
        /// <returns>Kết quả cập nhật trạng thái</returns>
        [HttpPut("consignments/{orderId}/status")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateConsignmentStatus(Guid orderId, [FromBody] UpdateConsignmentStatusRequest request)
        {
            try
            {
                await _orderService.UpdateConsignmentStatusAsync(orderId, request);
                return Ok(new { message = "Cập nhật trạng thái thành công." });
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
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
