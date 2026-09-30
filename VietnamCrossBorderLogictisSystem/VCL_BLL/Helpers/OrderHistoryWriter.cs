using System;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Ghi một dòng lịch sử cho đơn. Chỉ thêm vào unit of work, lưu cùng lần SaveChanges của
    /// thao tác nghiệp vụ để lịch sử và trạng thái không bao giờ lệch nhau.
    /// </summary>
    public static class OrderHistoryWriter
    {
        public static Task RecordAsync(
            IUnitOfWork unitOfWork,
            Guid orderId,
            string? fromStatus,
            string? toStatus,
            string eventCode,
            string? note = null,
            Guid? actorId = null)
        {
            return unitOfWork.Repository<OrderStatusHistory>().AddAsync(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                EventCode = eventCode,
                Note = Truncate(note, 1000),
                ActorId = actorId,
                CreatedAt = DateTime.UtcNow
            });
        }

        private static string? Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
    }
}
