using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Tính phí lưu kho tại kho đích cho đơn ký gửi.
    /// <para>
    /// Tách riêng khỏi dịch vụ thanh toán vì có hai nơi cần: thanh toán cuối (cộng vào hoá đơn)
    /// và màn hình xem trước của nhân viên kho. Hai nơi phải ra cùng một con số nên dùng chung
    /// một chỗ tính.
    /// </para>
    /// </summary>
    public interface IStorageFeeService
    {
        Task<StorageFeeDto> CalculateForOrderAsync(Guid orderId);

        /// <summary>
        /// Bảng phí ước tính để khách cân nhắc trước khi chọn gửi lại kho.
        /// <para>
        /// Dùng lúc tạo đơn, khi chưa có kiện nào để tính thật. Lấy đúng đơn giá và số ngày miễn
        /// phí đang áp dụng nên con số khách thấy khớp với hoá đơn sau này.
        /// </para>
        /// </summary>
        Task<StorageFeeEstimateDto> GetEstimateAsync();
    }
}
