using System;
using System.Threading.Tasks;

namespace VCL_BLL.Interface
{
    public interface IConsignmentReceiptService
    {
        /// <summary>
        /// Sinh phiếu xác nhận ký gửi dạng PDF cho đơn, upload lên cloud và lưu link vào đơn.
        /// Gọi khi Sale xác nhận yêu cầu; kho mở link này để đối chiếu lúc check-in.
        /// </summary>
        /// <param name="orderId">Đơn ký gửi cần sinh phiếu.</param>
        /// <param name="generatedByUserId">User bấm xác nhận, dùng cho tên file và ô người lập phiếu.</param>
        /// <returns>URL phiếu PDF, hoặc null nếu không sinh được.</returns>
        Task<string?> GenerateAndStoreReceiptAsync(Guid orderId, Guid? generatedByUserId);

        /// <summary>
        /// Sinh nội dung PDF của phiếu mà không upload, dùng lúc phát hành phiếu.
        /// </summary>
        /// <param name="orderId">Đơn cần sinh phiếu.</param>
        /// <param name="generatedByUserId">
        /// Người PHÁT HÀNH phiếu, không phải người đang mở xem — giá trị này in vào ô người lập
        /// phiếu và tên file. Muốn xem lại phiếu đã phát hành thì gọi
        /// <see cref="GetReceiptByOrderIdAsync"/>.
        /// </param>
        Task<(byte[] Content, string FileName)> BuildReceiptPdfAsync(Guid orderId, Guid? generatedByUserId);

        /// <summary>
        /// Xem lại phiếu của một đơn. Người lập phiếu lấy từ đơn nên ai mở cũng ra cùng một bản,
        /// giống hệt bản mở bằng link công khai.
        /// </summary>
        Task<(byte[] Content, string FileName)> GetReceiptByOrderIdAsync(Guid orderId);

        /// <summary>
        /// Lấy phiếu theo khoá công khai, dùng cho link mở trên trình duyệt không có token.
        /// </summary>
        Task<(byte[] Content, string FileName)> GetReceiptByPublicKeyAsync(Guid publicKey);
    }
}
