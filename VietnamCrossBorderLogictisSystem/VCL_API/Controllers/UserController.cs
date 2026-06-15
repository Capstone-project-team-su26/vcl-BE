using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Tạo tài khoản nhân viên nội bộ (Admin only)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateEmployee([FromBody] RegisterRequest request)
        {
            try
            {
                var userId = await _userService.CreateEmployeeAsync(request);
                return CreatedAtAction(nameof(GetUserById), new { id = userId }, new { id = userId });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách tất cả người dùng (Admin only)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userService.GetAllUsersAsync();
            return Ok(users);
        }

        /// <summary>
        /// Lấy thông tin người dùng theo ID (Admin only)
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                return Ok(user);
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật Role của người dùng (Admin only)
        /// </summary>
        [HttpPut("{id}/role")]
        public async Task<IActionResult> UpdateUserRole(Guid id, [FromBody] UpdateUserRoleRequest request)
        {
            try
            {
                await _userService.UpdateUserRoleAsync(id, request.Role);
                return Ok(new { message = "Cập nhật role thành công." });
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Khóa tài khoản người dùng (Admin only)
        /// </summary>
        [HttpPut("{id}/lock")]
        public async Task<IActionResult> LockUser(Guid id)
        {
            try
            {
                await _userService.LockUserAsync(id);
                return Ok(new { message = "Khóa tài khoản thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Mở khóa tài khoản người dùng (Admin only)
        /// </summary>
        [HttpPut("{id}/unlock")]
        public async Task<IActionResult> UnlockUser(Guid id)
        {
            try
            {
                await _userService.UnlockUserAsync(id);
                return Ok(new { message = "Mở khóa tài khoản thành công." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
