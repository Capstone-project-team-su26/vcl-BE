using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// "Đơn đã có cọc" đọc từ khoản thu thật (cổng thanh toán đã xác thực hoặc Admin xác nhận),
    /// không đọc từ trạng thái đơn — trạng thái đơn còn đổi theo tiến độ kho nên không dùng làm
    /// điều kiện tiền được.
    /// </summary>
    public static class DepositQuery
    {
        public static async Task<bool> HasConfirmedDepositAsync(IUnitOfWork unitOfWork, Guid orderId)
        {
            var quotationIds = (await unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId)
                .Select(q => q.Id)
                .ToHashSet();

            if (quotationIds.Count == 0) return false;

            var invoiceIds = (await unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();

            return (await unitOfWork.Repository<Payment>().GetAllAsync())
                .Any(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value)
                          && string.Equals(p.InstallmentType, "DEPOSIT", StringComparison.OrdinalIgnoreCase)
                          && string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase));
        }
    }
}
