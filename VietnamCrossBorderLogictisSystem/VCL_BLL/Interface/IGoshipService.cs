using System.Collections.Generic;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IGoshipService
    {
        Task<List<GoshipRateOption>> GetRatesAsync(GoshipRateRequest request, string token);
        Task<GoshipOrderResponseData> CreateOrderAsync(GoshipOrderRequest request, string token);
        Task<bool> ProcessWebhookAsync(GoshipWebhookPayload payload);

        /// <summary>
        /// Kho bấm đồng bộ: hỏi thẳng GoShip trạng thái vận đơn rồi cập nhật vào hệ thống.
        /// Sandbox GoShip không bắn webhook nên đây là đường duy nhất để biết hàng đã tới đâu.
        /// </summary>
        Task<GoshipSyncResultDto> SyncShipmentStatusAsync(string trackingCode, string token);

        /// <summary>
        /// Đổi tên hành chính (tỉnh · quận · phường) sang mã số của GoShip.
        /// <para>
        /// GoShip chỉ nhận mã (<c>700000</c> = Hồ Chí Minh), trong khi phiếu giao của mình lưu
        /// tên do sale gõ tay. Không có bước đổi này thì mọi lời gọi đều bị từ chối với thông
        /// báo "thiếu thông tin tỉnh/TP" dù dữ liệu đã điền đủ.
        /// </para>
        /// </summary>
        Task<GoshipAddressCodes> ResolveAddressCodesAsync(
            string cityName, string districtName, string wardName, string token);

        /// <summary>
        /// Danh mục tỉnh/thành của đơn vị vận chuyển, để FE cho người dùng chọn thay vì gõ tay.
        /// Chọn từ danh mục thì khỏi phải đoán tên, và lưu được mã chuẩn ngay từ đầu.
        /// </summary>
        Task<List<GoshipLocationOption>> GetCitiesAsync(string token);

        /// <summary>Quận/huyện thuộc một tỉnh/thành.</summary>
        Task<List<GoshipLocationOption>> GetDistrictsAsync(string cityId, string token);

        /// <summary>Phường/xã thuộc một quận/huyện.</summary>
        Task<List<GoshipLocationOption>> GetWardsAsync(string districtId, string token);
    }
}
