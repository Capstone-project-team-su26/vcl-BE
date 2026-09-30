using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Đơn giao xong khi mọi kiện còn hiệu lực đã tới tay khách. Kiện đã huỷ theo sự cố không còn
    /// phải giao nên không được giữ đơn lại mãi ở "đang giao".
    /// </summary>
    public static class OrderDeliveryProgress
    {
        private static readonly string[] Retired = { "SPLIT", "MERGED", VnParcelStatuses.Disposed };

        /// <summary>Chuyển đơn sang DELIVERED nếu đủ điều kiện. Trả về true khi vừa chuyển.</summary>
        public static async Task<bool> RefreshAsync(IUnitOfWork unitOfWork, Guid orderId, DateTime now)
        {
            var order = await unitOfWork.Repository<Order>().GetByIdAsync(orderId);
            if (order == null) return false;

            var status = order.Status?.Trim().ToUpperInvariant();
            if (status is "DELIVERED" or "CUSTOMER_CONFIRMED" or "COMPLETED" or "CANCELLED") return false;

            var parcels = (await unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == orderId
                            && !Retired.Contains(p.PackageStatus?.Trim().ToUpperInvariant() ?? string.Empty))
                .ToList();

            if (parcels.Count == 0) return false;
            if (!parcels.All(p => string.Equals(p.PackageStatus, VnParcelStatuses.Delivered, StringComparison.OrdinalIgnoreCase)))
                return false;

            var before = order.Status;
            order.Status = "DELIVERED";
            order.DeliveredAt ??= now;
            unitOfWork.Repository<Order>().Update(order);
            await OrderHistoryWriter.RecordAsync(unitOfWork, order.Id, before, order.Status,
                "ORDER_DELIVERED", "Mọi kiện đã giao tới khách.", null);
            return true;
        }
    }
}
