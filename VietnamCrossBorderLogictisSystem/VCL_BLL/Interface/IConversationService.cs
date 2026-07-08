using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IConversationService
    {
        Task<ConversationDto> CreateAsync(Guid customerUserId, CreateConversationRequestDto request);
        Task<IEnumerable<ConversationDto>> GetUserConversationsAsync(Guid userId, string role);
        Task<ConversationDto> GetByIdAsync(Guid conversationId, Guid userId, string role);
        Task<MessageDto> AddMessageAsync(Guid conversationId, Guid userId, string role, CreateMessageRequestDto request);
        Task MarkAsReadAsync(Guid conversationId, Guid userId, string role);
        Task<bool> CheckAccessAsync(Guid conversationId, Guid userId);
        Task<bool> CheckAccessAsync(Guid conversationId, Guid userId, string role);
    }
}
