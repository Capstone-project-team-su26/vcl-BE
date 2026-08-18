using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_DLL.Models;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Một chỗ duy nhất trả lời câu "đơn này khách đã trả xong tiền chưa".
    /// </summary>
    /// <remarks>
    /// Câu hỏi này trước đây được trả lời ở ba nơi (hàng đợi của Sale, chốt chặn lập phiếu giao,
    /// và sắp tới là cổng nhập kho) với ba luật đã lệch nhau — có nơi coi đơn mua hộ mới trả cọc
    /// là đã tất toán. Tiền thì không được phép có hai định nghĩa, nên gom về đây.
    /// </remarks>
    public interface ISettlementQueryService
    {
        /// <summary>Đơn đã thu đủ tiền chưa. Tự phân nhánh ký gửi / mua hộ.</summary>
        Task<bool> IsSettledAsync(Order order);

        /// <summary>
        /// Bản hỏi hàng loạt cho màn danh sách — tránh gọi <see cref="IsSettledAsync"/> trong
        /// vòng lặp rồi quét lại toàn bảng thanh toán cho từng đơn.
        /// </summary>
        Task<HashSet<Guid>> GetSettledOrderIdsAsync(IReadOnlyCollection<Order> orders);
    }
}
