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
    }
}
