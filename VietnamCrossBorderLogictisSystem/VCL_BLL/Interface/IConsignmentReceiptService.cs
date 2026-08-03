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
        /// Sinh nội dung PDF của phiếu mà không upload, dùng cho tải trực tiếp.
        /// </summary>
        Task<(byte[] Content, string FileName)> BuildReceiptPdfAsync(Guid orderId, Guid? generatedByUserId);

        /// <summary>
        /// Lấy phiếu theo khoá công khai, dùng cho link mở trên trình duyệt không có token.
        /// </summary>
        Task<(byte[] Content, string FileName)> GetReceiptByPublicKeyAsync(Guid publicKey);
    }
}
