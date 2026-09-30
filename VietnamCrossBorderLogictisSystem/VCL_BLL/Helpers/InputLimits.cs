using System;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Giới hạn dữ liệu khách/Sale gửi lên khi tạo yêu cầu mua hộ và đơn ký gửi — một nguồn cho cả
    /// DataAnnotations trên DTO lẫn phần kiểm trong service. Số khớp với web (MAX_PURCHASE_ITEM_QUANTITY,
    /// PACKAGE_LIMITS.maxQuantity) và với độ dài cột DB (VCLDBContext: PURCHASE_REQUESTS / PURCHASE_REQUEST_ITEMS).
    /// Web chặn trước, backend chặn lại để gọi thẳng API cũng không lọt, và báo 400 tiếng Việt thay vì lỗi SQL 500.
    /// </summary>
    public static class InputLimits
    {
        /// <summary>
        /// MẶC ĐỊNH số dòng sản phẩm tối đa của một yêu cầu mua hộ / số kiện tối đa của một đơn ký gửi.
        /// Giá trị đang áp dụng do Admin chỉnh (SYSTEM_SETTINGS — xem <see cref="OrderLimitSettings"/>).
        /// </summary>
        public const int MaxItemsPerRequest = 50;

        /// <summary>MẶC ĐỊNH số lượng tối đa của một dòng mua hộ — Admin chỉnh được (PURCHASE_MAX_ITEM_QUANTITY).</summary>
        public const int MaxPurchaseItemQuantity = 999;

        /// <summary>MẶC ĐỊNH số sản phẩm tối đa trên một kiện ký gửi — Admin chỉnh được (ORDER_MAX_PARCEL_QUANTITY).</summary>
        public const int MaxConsignmentItemQuantity = 5;

        /// <summary>
        /// TRẦN CỨNG (chặn payload khổng lồ ở tầng DTO) — Admin không nâng được quá các số này.
        /// Hạn mức nghiệp vụ thật do service kiểm theo cấu hình Admin.
        /// </summary>
        public const int HardMaxItemsPerRequest = 500;
        public const int HardMaxPurchaseItemQuantity = 1_000_000;
        public const int HardMaxConsignmentItemQuantity = 100_000;

        /// <summary>Số ảnh tham khảo tối đa của một sản phẩm mua hộ (web: MAX_IMAGES_PER_ITEM).</summary>
        public const int MaxImagesPerPurchaseItem = 5;

        // Độ dài cột DB (PURCHASE_REQUEST_ITEMS)
        public const int ProductLinkLength = 1000;
        public const int SourceWebsiteLength = 500;
        public const int ProductTypeLength = 100;
        public const int ProductNameLength = 255;
        public const int AttributesLength = 500;
        public const int ItemNoteLength = 500;
        public const int ImageUrlsLength = 1000;

        // Độ dài cột DB (PURCHASE_REQUESTS)
        public const int ReceiverNameLength = 255;
        public const int ReceiverPhoneLength = 50;
        public const int ReceiverAddressLength = 500;
        public const int GeneralNoteLength = 1000;
        public const int PricingRuleIdsLength = 1000;

        /// <summary>Link sản phẩm phải là URL tuyệt đối http/https có tên miền.</summary>
        public static bool IsHttpUrl(string? value) =>
            Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host);
    }
}
