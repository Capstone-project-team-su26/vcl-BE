using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>Nhân viên nội bộ xem mọi đơn; khách chỉ xem đơn của chính mình.</summary>
    public static class OrderAccess
    {
        public static async Task EnsureCanViewAsync(IUnitOfWork unitOfWork, Guid orderId, Guid userId, string? role)
        {
            if (StaffRoleHelper.IsInternalStaff(role)) return;

            var order = await unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng {orderId}.");
            var customer = (await unitOfWork.Repository<Customer>().GetAllAsync()).FirstOrDefault(c => c.UserId == userId);
            if (customer == null || order.CustomerId != customer.Id)
                throw new UnauthorizedAccessException("Bạn chỉ xem được thông tin đơn hàng của mình.");
        }
    }
}
