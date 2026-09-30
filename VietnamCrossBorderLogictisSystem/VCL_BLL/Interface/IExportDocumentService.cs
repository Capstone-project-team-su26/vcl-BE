using System;
using System.Threading.Tasks;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Sinh chứng từ giấy cho luồng xuất kho quốc tế (phiếu xuất, danh sách soạn hàng, manifest lô).
    /// <para>
    /// Tách khỏi <see cref="IWarehouseReleaseService"/> vì đây là việc "in ra giấy" chứ không phải chuyển trạng thái:
    /// nghiệp vụ xuất kho không được phụ thuộc vào QuestPDF, và ngược lại sửa bố cục chứng từ không
    /// được đụng tới code duyệt/bốc/bàn giao.
    /// </para>
    /// <para>
    /// Mọi hàm đều sinh lại PDF từ dữ liệu hiện tại thay vì đọc bản đã lưu: kho in phiếu nhiều lần
    /// trong ngày (bốc dở, tách phiếu, bỏ kiện) nên bản in phải luôn khớp dữ liệu mới nhất.
    /// </para>
    /// </summary>
    public interface IExportDocumentService
    {
        /// <summary>
        /// Phiếu xuất kho — chứng từ phát hành khi quản lý kho duyệt phiếu, kho và người nhận ký đối chiếu.
        /// Liệt kê mọi kiện còn nằm trong phiếu (bỏ kiện đã gỡ khỏi phiếu, vì chúng không rời kho theo phiếu này).
        /// </summary>
        /// <param name="wroRequestId">Phiếu xuất kho cần in.</param>
        /// <exception cref="VCL_BLL.Exceptions.NotFoundException">Không tìm thấy phiếu.</exception>
        Task<(byte[] Content, string FileName)> BuildReleaseNoteAsync(Guid wroRequestId);

        /// <summary>
        /// Danh sách soạn hàng — bản in nhân viên kho cầm đi bốc hàng, sắp theo mã ô kệ để đi một vòng kho.
        /// Chỉ in kiện chưa rời kho (RESERVED / PICKED) và đánh dấu rõ kiện nào đã bốc, để in lại giữa chừng
        /// vẫn dùng được mà không bốc trùng.
        /// </summary>
        /// <param name="wroRequestId">Phiếu xuất kho cần soạn hàng.</param>
        /// <exception cref="VCL_BLL.Exceptions.NotFoundException">Không tìm thấy phiếu.</exception>
        Task<(byte[] Content, string FileName)> BuildPickingSheetAsync(Guid wroRequestId);

        /// <summary>
        /// Manifest lô vận chuyển — bản kê đi kèm hàng khi bàn giao cho hãng vận chuyển và làm thủ tục,
        /// gom mọi phiếu xuất trong lô cùng kiện của từng phiếu.
        /// </summary>
        /// <param name="shipmentId">Lô vận chuyển cần in manifest.</param>
        /// <exception cref="VCL_BLL.Exceptions.NotFoundException">Không tìm thấy lô.</exception>
        Task<(byte[] Content, string FileName)> BuildShipmentManifestAsync(Guid shipmentId);
    }
}
