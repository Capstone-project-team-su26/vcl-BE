using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Thông tin chi tiết của một tin nhắn.
    /// </summary>
    public class MessageDto
    {
        /// <summary>
        /// ID duy nhất của tin nhắn.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// ID của cuộc hội thoại chứa tin nhắn này.
        /// </summary>
        public Guid ConversationId { get; set; }

        /// <summary>
        /// ID của người gửi tin nhắn (CustomerId hoặc SalesId/AdminId).
        /// </summary>
        public Guid SenderId { get; set; }

        /// <summary>
        /// Vai trò của người gửi (ví dụ: "Customer", "Sale", "Admin").
        /// </summary>
        public string SenderRole { get; set; } = null!;

        /// <summary>
        /// Nội dung tin nhắn. Bắt buộc từ 1 đến 2000 ký tự.
        /// </summary>
        public string Content { get; set; } = null!;

        /// <summary>
        /// Đường dẫn tệp đính kèm (Cloudinary URL) nếu có.
        /// </summary>
        public string? AttachmentUrl { get; set; }

        /// <summary>
        /// Trạng thái đã đọc tin nhắn bởi đối phương hay chưa.
        /// </summary>
        public bool IsRead { get; set; }

        /// <summary>
        /// Thời điểm tin nhắn được gửi đi.
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Thông tin chi tiết phòng trò chuyện (Conversation).
    /// </summary>
    public class ConversationDto
    {
        /// <summary>
        /// ID duy nhất của cuộc hội thoại.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// ID của Khách hàng (Customer) tham gia phòng chat.
        /// </summary>
        public Guid CustomerId { get; set; }

        /// <summary>
        /// Tên đầy đủ của Khách hàng.
        /// </summary>
        public string CustomerName { get; set; } = null!;

        /// <summary>
        /// Mã hiển thị của Khách hàng (ví dụ: KH-12345).
        /// </summary>
        public string CustomerCode { get; set; } = null!;

        /// <summary>
        /// ID của nhân viên Sales được phân công phụ trách phòng chat này. Sẽ bằng null nếu chưa có ai nhận.
        /// </summary>
        public Guid? SalesId { get; set; }

        /// <summary>
        /// Tên đầy đủ của nhân viên Sales phụ trách. Sẽ bằng null nếu chưa phân công.
        /// </summary>
        public string? SalesName { get; set; }

        /// <summary>
        /// Loại thực thể liên kết với cuộc hội thoại (CONSIGNMENT, PURCHASE_REQUEST, QUOTATION).
        /// </summary>
        public string? RelatedType { get; set; }

        /// <summary>
        /// ID của thực thể liên kết (OrderId hoặc QuotationId).
        /// </summary>
        public Guid? RelatedId { get; set; }

        /// <summary>
        /// Mã hiển thị của thực thể liên kết (ví dụ: mã ký gửi VCL-20260708-XXXXXX).
        /// </summary>
        public string? RelatedCode { get; set; }

        /// <summary>
        /// Trạng thái phòng chat ("OPEN" hoặc "CLOSED").
        /// </summary>
        public string Status { get; set; } = null!;

        /// <summary>
        /// Thời điểm phòng chat được tạo ra.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Thời điểm có cập nhật mới nhất (tin nhắn mới gửi hoặc thay đổi trạng thái).
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Số lượng tin nhắn chưa đọc đối với tài khoản đang đăng nhập hiện tại.
        /// </summary>
        public int UnreadCount { get; set; }

        /// <summary>
        /// Danh sách lịch sử tin nhắn trong phòng chat.
        /// </summary>
        public List<MessageDto> Messages { get; set; } = new List<MessageDto>();
    }

    /// <summary>
    /// DTO yêu cầu khởi tạo cuộc trò chuyện mới từ Khách hàng.
    /// </summary>
    public class CreateConversationRequestDto
    {
        /// <summary>
        /// Loại thực thể muốn liên kết trò chuyện (chọn một trong: CONSIGNMENT, PURCHASE_REQUEST, QUOTATION). Để null nếu muốn chat chung.
        /// </summary>
        [RegularExpression("^(CONSIGNMENT|PURCHASE_REQUEST|QUOTATION)$", ErrorMessage = "Chỉ liên kết được với đơn ký gửi, yêu cầu mua hộ hoặc báo giá.")]
        public string? RelatedType { get; set; }

        /// <summary>
        /// ID của thực thể liên kết (ConsignmentId, PurchaseRequestId, hoặc QuotationId). Để null nếu không có thực thể liên kết.
        /// </summary>
        public Guid? RelatedId { get; set; }

        /// <summary>
        /// Nội dung tin nhắn khởi đầu phòng chat. Bắt buộc dài từ 1 đến 2000 ký tự.
        /// </summary>
        [Required(ErrorMessage = "Nội dung tin nhắn không được để trống.")]
        [MaxLength(2000, ErrorMessage = "Nội dung tin nhắn không được quá 2000 ký tự.")]
        public string Message { get; set; } = null!;

        /// <summary>
        /// Đường dẫn tệp đính kèm / hình ảnh (ví dụ: Cloudinary URL) nếu có. Tối đa 500 ký tự.
        /// </summary>
        [MaxLength(500, ErrorMessage = "Đường dẫn tệp đính kèm không quá 500 ký tự.")]
        public string? AttachmentUrl { get; set; }
    }

    /// <summary>
    /// DTO yêu cầu gửi tin nhắn mới trong phòng chat.
    /// </summary>
    public class CreateMessageRequestDto
    {
        /// <summary>
        /// Nội dung tin nhắn gửi đi. Bắt buộc dài từ 1 đến 2000 ký tự.
        /// </summary>
        [Required(ErrorMessage = "Nội dung tin nhắn không được để trống.")]
        [MaxLength(2000, ErrorMessage = "Nội dung tin nhắn không được quá 2000 ký tự.")]
        public string Content { get; set; } = null!;

        /// <summary>
        /// Đường dẫn tệp đính kèm / hình ảnh (ví dụ: Cloudinary URL) nếu có. Tối đa 500 ký tự.
        /// </summary>
        [MaxLength(500, ErrorMessage = "Đường dẫn tệp đính kèm không quá 500 ký tự.")]
        public string? AttachmentUrl { get; set; }
    }
}
