using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class ConversationService : IConversationService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ConversationService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<ConversationDto> CreateAsync(Guid customerUserId, CreateConversationRequestDto request)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var customers = await customerRepo.GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == customerUserId);

            if (customer == null)
            {
                throw new ForbiddenException("Chỉ khách hàng mới được quyền tạo cuộc trao đổi.");
            }

            // Validate related entity if provided
            if (request.RelatedId.HasValue && !string.IsNullOrEmpty(request.RelatedType))
            {
                if (string.Equals(request.RelatedType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase))
                {
                    var orderRepo = _unitOfWork.Repository<Order>();
                    var order = await orderRepo.GetByIdAsync(request.RelatedId.Value);
                    if (order == null)
                    {
                        throw new ArgumentException("Không tìm thấy yêu cầu ký gửi tương ứng.");
                    }
                }
                else if (string.Equals(request.RelatedType, "PURCHASE_REQUEST", StringComparison.OrdinalIgnoreCase))
                {
                    var prRepo = _unitOfWork.Repository<PurchaseRequest>();
                    var pr = await prRepo.GetByIdAsync(request.RelatedId.Value);
                    if (pr == null)
                    {
                        throw new ArgumentException("Không tìm thấy yêu cầu mua hộ tương ứng.");
                    }
                }
                else if (string.Equals(request.RelatedType, "QUOTATION", StringComparison.OrdinalIgnoreCase))
                {
                    var quoteRepo = _unitOfWork.Repository<Quotation>();
                    var quote = await quoteRepo.GetByIdAsync(request.RelatedId.Value);
                    if (quote == null)
                    {
                        throw new ArgumentException("Không tìm thấy báo giá tương ứng.");
                    }
                }
            }

            var conversationRepo = _unitOfWork.Repository<Conversation>();
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                SalesId = null,
                RelatedType = request.RelatedType?.ToUpper().Trim(),
                RelatedId = request.RelatedId,
                Status = "OPEN",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await conversationRepo.AddAsync(conversation);

            var messageRepo = _unitOfWork.Repository<Message>();
            var firstMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                SenderId = customerUserId,
                SenderRole = "Customer",
                Content = request.Message.Trim(),
                AttachmentUrl = request.AttachmentUrl?.Trim(),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            await messageRepo.AddAsync(firstMessage);
            await _unitOfWork.SaveChangesAsync();

            return await MapToDto(conversation, customerUserId, "Customer");
        }

        public async Task<IEnumerable<ConversationDto>> GetUserConversationsAsync(Guid userId, string role)
        {
            var conversationRepo = _unitOfWork.Repository<Conversation>();
            var conversations = await conversationRepo.GetAllAsync();
            IEnumerable<Conversation> filteredConversations;

            if (string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var customers = await customerRepo.GetAllAsync();
                var customer = customers.FirstOrDefault(c => c.UserId == userId);

                if (customer == null)
                {
                    return Enumerable.Empty<ConversationDto>();
                }

                filteredConversations = conversations.Where(c => c.CustomerId == customer.Id);
            }
            else if (string.Equals(role, "Sale", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                filteredConversations = conversations.Where(c => c.SalesId == null || c.SalesId == userId);
            }
            else
            {
                throw new ForbiddenException("Bạn không có quyền truy cập chức năng này.");
            }

            var result = new List<ConversationDto>();
            foreach (var conv in filteredConversations.OrderByDescending(c => c.UpdatedAt))
            {
                result.Add(await MapToDto(conv, userId, role));
            }

            return result;
        }

        public async Task<ConversationDto> GetByIdAsync(Guid conversationId, Guid userId, string role)
        {
            var conversationRepo = _unitOfWork.Repository<Conversation>();
            var conversation = await conversationRepo.GetByIdAsync(conversationId);

            if (conversation == null)
            {
                throw new NotFoundException($"Không tìm thấy cuộc trao đổi với ID: {conversationId}");
            }

            await VerifyAccessAsync(conversation, userId, role);

            return await MapToDto(conversation, userId, role, includeMessages: true);
        }

        public async Task<MessageDto> AddMessageAsync(Guid conversationId, Guid userId, string role, CreateMessageRequestDto request)
        {
            var conversationRepo = _unitOfWork.Repository<Conversation>();
            var conversation = await conversationRepo.GetByIdAsync(conversationId);

            if (conversation == null)
            {
                throw new NotFoundException($"Không tìm thấy cuộc trao đổi với ID: {conversationId}");
            }

            await VerifyAccessAsync(conversation, userId, role);

            // If Sales is replying to an unassigned conversation, assign it to them
            if ((string.Equals(role, "Sale", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) && conversation.SalesId == null)
            {
                conversation.SalesId = userId;
            }

            var messageRepo = _unitOfWork.Repository<Message>();
            var newMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                SenderId = userId,
                SenderRole = string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase) ? "Customer" : "Sale",
                Content = request.Content.Trim(),
                AttachmentUrl = request.AttachmentUrl?.Trim(),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            conversation.UpdatedAt = DateTime.UtcNow;

            await messageRepo.AddAsync(newMessage);
            conversationRepo.Update(conversation);
            await _unitOfWork.SaveChangesAsync();

            return new MessageDto
            {
                Id = newMessage.Id,
                ConversationId = newMessage.ConversationId,
                SenderId = newMessage.SenderId,
                SenderRole = newMessage.SenderRole,
                Content = newMessage.Content,
                AttachmentUrl = newMessage.AttachmentUrl,
                IsRead = newMessage.IsRead,
                CreatedAt = newMessage.CreatedAt
            };
        }

        public async Task MarkAsReadAsync(Guid conversationId, Guid userId, string role)
        {
            var conversationRepo = _unitOfWork.Repository<Conversation>();
            var conversation = await conversationRepo.GetByIdAsync(conversationId);

            if (conversation == null)
            {
                throw new NotFoundException($"Không tìm thấy cuộc trao đổi với ID: {conversationId}");
            }

            await VerifyAccessAsync(conversation, userId, role);

            var messageRepo = _unitOfWork.Repository<Message>();
            var allMessages = await messageRepo.GetAllAsync();
            var unreadMessages = allMessages.Where(m => m.ConversationId == conversationId && !m.IsRead);

            bool hasChanges = false;
            foreach (var msg in unreadMessages)
            {
                if (string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase) && string.Equals(msg.SenderRole, "Sale", StringComparison.OrdinalIgnoreCase))
                {
                    msg.IsRead = true;
                    messageRepo.Update(msg);
                    hasChanges = true;
                }
                else if ((string.Equals(role, "Sale", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) && string.Equals(msg.SenderRole, "Customer", StringComparison.OrdinalIgnoreCase))
                {
                    msg.IsRead = true;
                    messageRepo.Update(msg);
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                await _unitOfWork.SaveChangesAsync();
            }
        }

        public async Task<bool> CheckAccessAsync(Guid conversationId, Guid userId, string role)
        {
            var conversationRepo = _unitOfWork.Repository<Conversation>();
            var conversation = await conversationRepo.GetByIdAsync(conversationId);

            if (conversation == null)
            {
                return false;
            }

            try
            {
                await VerifyAccessAsync(conversation, userId, role);
                return true;
            }
            catch (ForbiddenException)
            {
                return false;
            }
        }

        public async Task<bool> CheckAccessAsync(Guid conversationId, Guid userId)
        {
            // Resolve role from database or fallback if role isn't passed (standard overload)
            var userRepo = _unitOfWork.Repository<User>();
            var user = await userRepo.GetByIdAsync(userId);
            if (user == null) return false;

            return await CheckAccessAsync(conversationId, userId, user.Role);
        }

        private async Task VerifyAccessAsync(Conversation conversation, Guid userId, string role)
        {
            if (string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var customers = await customerRepo.GetAllAsync();
                var customer = customers.FirstOrDefault(c => c.UserId == userId);

                if (customer == null || conversation.CustomerId != customer.Id)
                {
                    throw new ForbiddenException("Bạn không có quyền truy cập cuộc trao đổi này.");
                }
            }
            else if (string.Equals(role, "Sale", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                if (conversation.SalesId != null && conversation.SalesId != userId)
                {
                    throw new ForbiddenException("Cuộc trao đổi này đã được phân công cho Sales khác.");
                }
            }
            else
            {
                throw new ForbiddenException("Bạn không có quyền truy cập chức năng này.");
            }
        }

        private async Task<ConversationDto> MapToDto(Conversation conversation, Guid currentUserId, string role, bool includeMessages = false)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = await customerRepo.GetByIdAsync(conversation.CustomerId);
            
            var userRepo = _unitOfWork.Repository<User>();
            var salesUser = conversation.SalesId.HasValue ? await userRepo.GetByIdAsync(conversation.SalesId.Value) : null;

            var messageRepo = _unitOfWork.Repository<Message>();
            var allMessages = await messageRepo.GetAllAsync();
            var convMessages = allMessages.Where(m => m.ConversationId == conversation.Id).OrderBy(m => m.CreatedAt).ToList();

            // Calculate unread count for current user
            int unreadCount = 0;
            if (string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                unreadCount = convMessages.Count(m => string.Equals(m.SenderRole, "Sale", StringComparison.OrdinalIgnoreCase) && !m.IsRead);
            }
            else if (string.Equals(role, "Sale", StringComparison.OrdinalIgnoreCase) || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                unreadCount = convMessages.Count(m => string.Equals(m.SenderRole, "Customer", StringComparison.OrdinalIgnoreCase) && !m.IsRead);
            }

            // Resolve related entity code
            string relatedCode = null;
            if (conversation.RelatedId.HasValue && !string.IsNullOrEmpty(conversation.RelatedType))
            {
                if (string.Equals(conversation.RelatedType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase))
                {
                    var orderRepo = _unitOfWork.Repository<Order>();
                    var order = await orderRepo.GetByIdAsync(conversation.RelatedId.Value);
                    relatedCode = order?.ConsignmentCode;
                }
                else if (string.Equals(conversation.RelatedType, "PURCHASE_REQUEST", StringComparison.OrdinalIgnoreCase))
                {
                    var prRepo = _unitOfWork.Repository<PurchaseRequest>();
                    var pr = await prRepo.GetByIdAsync(conversation.RelatedId.Value);
                    relatedCode = pr?.PurchaseCode;
                }
                else if (string.Equals(conversation.RelatedType, "QUOTATION", StringComparison.OrdinalIgnoreCase))
                {
                    // Quotation doesn't have an alternate text code, using first 8 chars of ID
                    relatedCode = "QT-" + conversation.RelatedId.Value.ToString().Substring(0, 8).ToUpper();
                }
            }

            var dto = new ConversationDto
            {
                Id = conversation.Id,
                CustomerId = conversation.CustomerId,
                CustomerName = customer?.FullName ?? "Unknown",
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                SalesId = conversation.SalesId,
                SalesName = salesUser?.FullName,
                RelatedType = conversation.RelatedType,
                RelatedId = conversation.RelatedId,
                RelatedCode = relatedCode,
                Status = conversation.Status,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt,
                UnreadCount = unreadCount
            };

            if (includeMessages)
            {
                dto.Messages = convMessages.Select(m => new MessageDto
                {
                    Id = m.Id,
                    ConversationId = m.ConversationId,
                    SenderId = m.SenderId,
                    SenderRole = m.SenderRole,
                    Content = m.Content,
                    AttachmentUrl = m.AttachmentUrl,
                    IsRead = m.IsRead,
                    CreatedAt = m.CreatedAt
                }).ToList();
            }

            return dto;
        }
    }
}
