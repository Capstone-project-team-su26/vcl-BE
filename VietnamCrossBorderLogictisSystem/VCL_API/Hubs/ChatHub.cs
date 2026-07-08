using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using VCL_BLL.Interface;

namespace VCL_API.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IConversationService _conversationService;

        public ChatHub(IConversationService conversationService)
        {
            _conversationService = conversationService;
        }

        public async Task JoinConversation(string conversationId)
        {
            if (Guid.TryParse(conversationId, out var conversationGuid))
            {
                var userIdString = Context.UserIdentifier;
                if (Guid.TryParse(userIdString, out var userId))
                {
                    var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
                    var hasAccess = await _conversationService.CheckAccessAsync(conversationGuid, userId, userRole);
                    if (hasAccess)
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
                        return;
                    }
                }
            }

            throw new HubException("Bạn không có quyền tham gia cuộc hội thoại này.");
        }

        public async Task LeaveConversation(string conversationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId);
        }
    }
}
