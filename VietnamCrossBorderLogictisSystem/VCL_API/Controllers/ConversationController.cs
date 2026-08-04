using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_API.Hubs;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/conversations")]
    [Authorize]
    public class ConversationController : ControllerBase
    {
        private readonly IConversationService _conversationService;
        private readonly IHubContext<ChatHub> _hubContext;

        public ConversationController(IConversationService conversationService, IHubContext<ChatHub> hubContext)
        {
            _conversationService = conversationService;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Customer tạo mới cuộc trao đổi với Sales.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> Create([FromBody] CreateConversationRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var (userId, role) = GetCurrentUser();
                var created = await _conversationService.CreateAsync(userId, request);

                // Broadcast to Sales that a new conversation has been created
                await _hubContext.Clients.All.SendAsync("NewConversationCreated", created);

                return CreatedAtAction(nameof(GetById), new { conversationId = created.Id }, created);
            }
            catch (ForbiddenException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Xem danh sách các cuộc trao đổi.
        /// Customer chỉ xem được của mình, Sales xem cuộc trao đổi chưa phân công hoặc đã nhận.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Customer,Sale,Admin")]
        public async Task<IActionResult> GetConversations()
        {
            try
            {
                var (userId, role) = GetCurrentUser();
                var conversations = await _conversationService.GetUserConversationsAsync(userId, role);
                return Ok(conversations);
            }
            catch (ForbiddenException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy chi tiết lịch sử tin nhắn của cuộc trao đổi theo ID.
        /// </summary>
        [HttpGet("{conversationId}")]
        [Authorize(Roles = "Customer,Sale,Admin")]
        public async Task<IActionResult> GetById(Guid conversationId)
        {
            try
            {
                var (userId, role) = GetCurrentUser();
                var conversation = await _conversationService.GetByIdAsync(conversationId, userId, role);
                return Ok(conversation);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ForbiddenException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Gửi thêm tin nhắn trong cuộc trao đổi.
        /// </summary>
        [HttpPost("{conversationId}/messages")]
        [Authorize(Roles = "Customer,Sale,Admin")]
        public async Task<IActionResult> AddMessage(Guid conversationId, [FromBody] CreateMessageRequestDto request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var (userId, role) = GetCurrentUser();
                var message = await _conversationService.AddMessageAsync(conversationId, userId, role, request);

                // Broadcast message to conversation channel group in real-time
                await _hubContext.Clients.Group(conversationId.ToString()).SendAsync("ReceiveMessage", message);

                return Ok(message);
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ForbiddenException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
        }

        /// <summary>
        /// Đánh dấu đã đọc các tin nhắn của đối phương trong cuộc trao đổi.
        /// </summary>
        [HttpPut("{conversationId}/read")]
        [Authorize(Roles = "Customer,Sale,Admin")]
        public async Task<IActionResult> MarkAsRead(Guid conversationId)
        {
            try
            {
                var (userId, role) = GetCurrentUser();
                await _conversationService.MarkAsReadAsync(conversationId, userId, role);

                // Notify chat channel that reader has read the messages
                await _hubContext.Clients.Group(conversationId.ToString()).SendAsync("ConversationRead", new 
                { 
                    conversationId = conversationId, 
                    readerId = userId 
                });

                return Ok(new { message = "Đã đánh dấu đọc tin nhắn." });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ForbiddenException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
        }

        private (Guid UserId, string Role) GetCurrentUser()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var roleStr = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                throw new UnauthorizedAccessException("Người dùng chưa được xác thực.");
            }

            return (userId, roleStr ?? string.Empty);
        }
    }
}
