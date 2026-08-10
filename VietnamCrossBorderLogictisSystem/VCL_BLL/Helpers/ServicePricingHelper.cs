using System;
using System.Collections.Generic;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Bảng <c>SERVICE_PRICINGS</c> kiêm hai việc: vừa giữ đơn giá các loại phí, vừa là nguồn
    /// của ô "phương án vận chuyển" và ô "tuyến" mà khách chọn khi tạo đơn
    /// (<c>GetShippingOptionsAsync</c> trả về <c>DISTINCT service_type</c>).
    /// <para>
    /// Vì vậy mọi loại phí KHÔNG phải phương án vận chuyển — phí lưu kho chẳng hạn — phải khai
    /// ở đây để hai ô đó không hiện ra lựa chọn vô nghĩa cho khách.
    /// </para>
    /// </summary>
    public static class ServicePricingHelper
    {
        /// <summary>Đơn giá phí lưu kho tại kho đích, tính theo kiện mỗi ngày.</summary>
        public const string StorageServiceType = "STORAGE";

        private static readonly HashSet<string> NonShippingServiceTypes =
            new(StringComparer.OrdinalIgnoreCase) { StorageServiceType };

        /// <summary>
        /// Dòng giá này có phải một phương án vận chuyển khách được chọn hay không.
        /// </summary>
        public static bool IsShippingOption(string? serviceType) =>
            !string.IsNullOrWhiteSpace(serviceType) && !NonShippingServiceTypes.Contains(serviceType.Trim());
    }
}
