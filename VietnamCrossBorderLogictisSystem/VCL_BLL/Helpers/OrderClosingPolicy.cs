using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Điều kiện đóng đơn dùng chung cho khách xác nhận, OM chốt tay và tự đóng khi hết hạn khiếu
    /// nại: không còn sự cố mở, bồi thường đã chi, không còn khoản phải thu treo.
    /// </summary>
    public static class OrderClosingPolicy
    {
        public static async Task<List<string>> BlockersAsync(IUnitOfWork unitOfWork, Order order)
        {
            var blockers = new List<string>();

            var incidents = (await unitOfWork.Repository<ParcelIncident>().GetAllAsync())
                .Where(i => i.OrderId == order.Id)
                .ToList();

            var open = incidents.Where(i => !string.Equals(i.Status, IncidentStatuses.Resolved, StringComparison.OrdinalIgnoreCase)).ToList();
            if (open.Count > 0)
                blockers.Add($"Còn {open.Count} sự cố / khiếu nại chưa xử lý: {string.Join(", ", open.Select(i => i.IncidentCode))}.");

            var unpaidCompensation = incidents
                .Where(i => string.Equals(i.Resolution, IncidentResolutions.Compensate, StringComparison.OrdinalIgnoreCase)
                            && !i.CompensationPaidAt.HasValue)
                .ToList();
            if (unpaidCompensation.Count > 0)
                blockers.Add($"Chưa chi bồi thường: {string.Join(", ", unpaidCompensation.Select(i => i.IncidentCode))}.");

            var quotationIds = (await unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == order.Id).Select(q => q.Id).ToHashSet();
            var invoiceIds = (await unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value)).Select(i => i.Id).ToHashSet();
            var pending = (await unitOfWork.Repository<Payment>().GetAllAsync())
                .Where(p => p.InvoiceId.HasValue && invoiceIds.Contains(p.InvoiceId.Value)
                            && (string.Equals(p.PaymentStatus, "PENDING", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(p.PaymentStatus, "PENDING_RECONCILIATION", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (pending.Count > 0)
                blockers.Add($"Còn {pending.Count} khoản thu chưa thanh toán ({string.Join(", ", pending.Select(p => p.InstallmentType).Distinct())}).");

            return blockers;
        }
    }
}
