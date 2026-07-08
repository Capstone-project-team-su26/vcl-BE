using System;

namespace VCL_DLL.Models
{
    public partial class Message
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }
        public string SenderRole { get; set; }
        public string Content { get; set; }
        public string? AttachmentUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual Conversation Conversation { get; set; }
    }
}
