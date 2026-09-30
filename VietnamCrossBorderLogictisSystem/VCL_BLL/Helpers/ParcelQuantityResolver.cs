using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Lượng hàng nằm trong một kiện, dùng khi ghi tồn kho cho kiện.
    /// </summary>
    /// <remarks>
    /// Quy ước hiện hành: mỗi dòng hàng khai báo (ORDER_ITEMS) là một kiện khi check-in, nên lượng
    /// của kiện là lượng của dòng hàng gắn với kiện. Chỉ khi kiện không gắn dòng hàng (dữ liệu cũ)
    /// và là kiện duy nhất của đơn mới lấy tổng lượng cả đơn; còn lại ghi 1 để không nhân tồn.
    /// </remarks>
    public static class ParcelQuantityResolver
    {
        public static async Task<(Guid? OrderItemId, int Quantity)> ResolveAsync(IUnitOfWork unitOfWork, Parcel parcel, Guid orderId)
        {
            var orderItems = (await unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == orderId)
                .ToList();

            if (parcel.OrderItemId.HasValue)
            {
                var item = orderItems.FirstOrDefault(i => i.Id == parcel.OrderItemId.Value);
                if (item != null)
                {
                    return (item.Id, Math.Max(1, item.Quantity ?? 1));
                }
            }

            if (orderItems.Count == 1)
            {
                return (orderItems[0].Id, Math.Max(1, orderItems[0].Quantity ?? 1));
            }

            var liveParcelCount = (await unitOfWork.Repository<Parcel>().GetAllAsync())
                .Count(p => p.ConsignmentId == orderId
                            && !string.Equals(p.PackageStatus, "SPLIT", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(p.PackageStatus, "MERGED", StringComparison.OrdinalIgnoreCase));

            if (liveParcelCount <= 1)
            {
                return (orderItems.FirstOrDefault()?.Id, Math.Max(1, orderItems.Sum(i => i.Quantity ?? 1)));
            }

            return (null, 1);
        }
    }
}
