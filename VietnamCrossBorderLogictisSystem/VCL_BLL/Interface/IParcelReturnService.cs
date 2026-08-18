using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Nghiệp vụ hàng giao không thành công và quay về kho.
    /// <para>
    /// Ba vai chạm vào một hồ sơ: hệ thống mở hồ sơ khi hãng báo hỏng, kho VN xác nhận đã cầm
    /// được hàng, OM chốt xử lý tiếp. Sale và khách chỉ xem.
    /// </para>
    /// </summary>
    public interface IParcelReturnService
    {
        /// <summary>
        /// Mở hoặc cập nhật hồ sơ theo tin từ hãng vận chuyển.
        /// <para>
        /// Gọi từ webhook nên phải chịu được gọi lại nhiều lần cùng một trạng thái: kiện đã có
        /// hồ sơ đang mở thì cập nhật chứ không đẻ thêm hồ sơ mới.
        /// </para>
        /// </summary>
        /// <remarks>
        /// KHÔNG tự gọi <c>SaveChangesAsync</c> — bên gọi (webhook) đang gom nhiều kiện trong
        /// một lần lưu, tự lưu ở đây là cắt đôi lô cập nhật đó.
        /// </remarks>
        /// <returns>Mã hồ sơ vừa mở hoặc vừa cập nhật, null nếu trạng thái không phải diện hàng hoàn.</returns>
        Task<string?> SyncFromCarrierAsync(
            Guid parcelId, string packageStatus, string? carrierStatusText, string? trackingCode);

        /// <summary>Nhân viên mở hồ sơ bằng tay khi hãng chưa báo hoặc báo sai.</summary>
        Task<ParcelReturnDto> OpenManuallyAsync(OpenParcelReturnRequest request, Guid userId, string userRole);

        /// <summary>
        /// Danh sách hồ sơ kèm số liệu tổng. <paramref name="status"/> bỏ trống là lấy tất cả.
        /// Vai kho chỉ thấy hồ sơ của kho mình.
        /// </summary>
        Task<ParcelReturnListDto> ListAsync(string? status, Guid? warehouseId, string userRole);

        Task<ParcelReturnDto> GetDetailAsync(Guid returnId);

        /// <summary>
        /// Hồ sơ hàng hoàn của một đơn — Sale và khách xem qua đường này.
        /// Khách chỉ xem được đơn của chính mình; thiếu chốt đó là đổi id trên URL xem được
        /// tên, số điện thoại và địa chỉ nhà của người khác.
        /// </summary>
        Task<ParcelReturnListDto> ListByOrderAsync(Guid orderId, Guid userId, string userRole);

        /// <summary>Kho VN xác nhận đã cầm được kiện, kèm xếp kệ nếu có.</summary>
        Task<ParcelReturnDto> ConfirmReceivedAsync(
            Guid returnId, ReceiveParcelReturnRequest request, Guid userId, string userRole);

        /// <summary>OM chốt hướng xử lý. Chỉ chốt được sau khi kho đã nhận lại hàng.</summary>
        Task<ParcelReturnDto> ResolveAsync(
            Guid returnId, ResolveParcelReturnRequest request, Guid userId, string userRole);
    }
}
