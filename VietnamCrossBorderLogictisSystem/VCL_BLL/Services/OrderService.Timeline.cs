using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class OrderService
    {
        /// <summary>
        /// Lịch sử chuyển trạng thái của đơn, cũ nhất trước. Khách chỉ xem đơn của mình.
        /// </summary>
        public async Task<List<OrderTimelineEntryDto>> GetOrderTimelineAsync(Guid orderId, Guid userId, string role)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");

            if (!StaffRoleHelper.IsInternalStaff(role))
            {
                var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .FirstOrDefault(c => c.UserId == userId);
                if (customer == null || order.CustomerId != customer.Id)
                    throw new UnauthorizedAccessException("Bạn không có quyền xem lịch sử của đơn này.");
            }

            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);

            return (await _unitOfWork.Repository<OrderStatusHistory>().GetAllAsync())
                .Where(h => h.OrderId == orderId)
                .OrderBy(h => h.CreatedAt)
                .Select(h => new OrderTimelineEntryDto
                {
                    Event = h.EventCode,
                    FromStatus = h.FromStatus,
                    ToStatus = h.ToStatus,
                    Note = h.Note,
                    ActorName = h.ActorId.HasValue && users.TryGetValue(h.ActorId.Value, out var u)
                        ? u.FullName ?? string.Empty
                        : "Hệ thống",
                    At = h.CreatedAt
                })
                .ToList();
        }
    }
}
