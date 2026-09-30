using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Tất toán theo cân đo thực tế tại kho Việt Nam: điều kiện được thu đợt cuối và phần điều
    /// chỉnh cước so với báo giá.
    /// </summary>
    public interface IVnSettlementService
    {
        /// <summary>Xem trước số tiền đợt cuối — khách và Sale thấy cùng một con số trước khi phát hành.</summary>
        Task<SettlementPreviewDto> PreviewAsync(Guid orderId);

        /// <summary>Chặn phát hành đợt cuối khi kho VN chưa kiểm xong hoặc còn sự cố mở.</summary>
        Task EnsureCanIssueAsync(Guid orderId);

        /// <summary>
        /// Ghi / cập nhật dòng điều chỉnh cước (và VAT tương ứng) lên hoá đơn. Trả về phần chênh so
        /// với lần ghi trước, để phát hành lại không cộng dồn.
        /// </summary>
        Task<decimal> ApplyFreightAdjustmentAsync(
            Guid orderId, Invoice invoice, List<InvoiceItem> existingItems, IGenericRepository<InvoiceItem> invoiceItemRepo);
    }
}
