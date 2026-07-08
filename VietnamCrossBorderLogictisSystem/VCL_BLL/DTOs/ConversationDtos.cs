using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class MessageDto
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }
        public string SenderRole { get; set; } = null!;
        public string Content { get; set; } = null!;
        public string? AttachmentUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ConversationDto
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = null!;
        public string CustomerCode { get; set; } = null!;
        public Guid? SalesId { get; set; }
        public string? SalesName { get; set; }
        public string? RelatedType { get; set; }
        public Guid? RelatedId { get; set; }
        public string? RelatedCode { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int UnreadCount { get; set; }
        public List<MessageDto> Messages { get; set; } = new List<MessageDto>();
    }

    public class CreateConversationRequestDto
    {
        [RegularExpression("^(CONSIGNMENT|PURCHASE_REQUEST|QUOTATION)$", ErrorMessage = "Loại liên kết chỉ nhận: CONSIGNMENT, PURCHASE_REQUEST, QUOTATION.")]
        public string? RelatedType { get; set; }

        public Guid? RelatedId { get; set; }

        [Required(ErrorMessage = "Nội dung tin nhắn không được để trống.")]
        [MaxLength(2000, ErrorMessage = "Nội dung tin nhắn không được quá 2000 ký tự.")]
        public string Message { get; set; } = null!;

        [MaxLength(500, ErrorMessage = "Đường dẫn tệp đính kèm không quá 500 ký tự.")]
        public string? AttachmentUrl { get; set; }
    }

    public class CreateMessageRequestDto
    {
        [Required(ErrorMessage = "Nội dung tin nhắn không được để trống.")]
        [MaxLength(2000, ErrorMessage = "Nội dung tin nhắn không được quá 2000 ký tự.")]
        public string Content { get; set; } = null!;

        [MaxLength(500, ErrorMessage = "Đường dẫn tệp đính kèm không quá 500 ký tự.")]
        public string? AttachmentUrl { get; set; }
    }
}
