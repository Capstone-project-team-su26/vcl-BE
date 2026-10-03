using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class PackageConfigurationResponseDto
    {
        public Guid Id { get; set; }
        public string ConfigCode { get; set; } = string.Empty;
        public string ConfigName { get; set; } = string.Empty;

        /// <summary>
        /// Tên tiếng Việt để hiện cho người dùng, cùng cách gọi với web khách
        /// (SMALL/MEDIUM/LARGE/CUSTOM → "Thùng cỡ nhỏ/vừa/lớn", "Thùng tùy chỉnh").
        /// Mã khác thì dùng tên Admin đặt (ConfigName).
        /// </summary>
        public string DisplayName => GetDisplayName(ConfigCode, ConfigName);

        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal MaxWeight { get; set; }

        public static string GetDisplayName(string? configCode, string? configName)
        {
            switch ((configCode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "SMALL": return "Thùng cỡ nhỏ";
                case "MEDIUM": return "Thùng cỡ vừa";
                case "LARGE": return "Thùng cỡ lớn";
                case "CUSTOM": return "Thùng tùy chỉnh";
            }

            var name = configName?.Trim();
            if (!string.IsNullOrEmpty(name)) return name;

            var code = configCode?.Trim();
            return string.IsNullOrEmpty(code) ? "Thùng" : code;
        }

        /// <summary>
        /// Thùng tiêu chuẩn (S/M/L): phí cố định cho 1 thùng.
        /// Thùng CUSTOM: đơn giá theo thể tích (VND cho mỗi 1.000 cm³, làm tròn lên).
        /// </summary>
        public decimal PackageFee { get; set; }

        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Phí ước tính cho kích thước kiện khách nhập (chỉ trả về từ API suggest).
        /// Thùng tiêu chuẩn = PackageFee; thùng CUSTOM = PackageFee × ceil(thể_tích_cm³ / 1000).
        /// </summary>
        public decimal? EstimatedFee { get; set; }
    }

    /// <summary>Request tạo/cập nhật cấu hình thùng đóng gói (Admin).</summary>
    public class SavePackageConfigurationRequest
    {
        [Required(ErrorMessage = "Mã thùng (configCode) không được để trống.")]
        public string ConfigCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tên thùng (configName) không được để trống.")]
        public string ConfigName { get; set; } = string.Empty;

        [Range(0.01, 100000, ErrorMessage = "Chiều dài (cm) phải lớn hơn 0.")]
        public decimal Length { get; set; }

        [Range(0.01, 100000, ErrorMessage = "Chiều rộng (cm) phải lớn hơn 0.")]
        public decimal Width { get; set; }

        [Range(0.01, 100000, ErrorMessage = "Chiều cao (cm) phải lớn hơn 0.")]
        public decimal Height { get; set; }

        [Range(0.01, 100000, ErrorMessage = "Cân nặng tối đa (kg) phải lớn hơn 0.")]
        public decimal MaxWeight { get; set; }

        /// <summary>Thùng tiêu chuẩn: phí cố định/thùng. Thùng CUSTOM: đơn giá VND / 1.000 cm³.</summary>
        [Range(0, double.MaxValue, ErrorMessage = "Phí đóng gói không được âm.")]
        public decimal PackageFee { get; set; }

        /// <summary>ACTIVE | INACTIVE. Bỏ trống = ACTIVE.</summary>
        public string? Status { get; set; }
    }

    /// <summary>Tùy chọn loại kiện đóng gói trả về cho Mobile UI render động.</summary>
    public class PackagingOptionDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // PACKAGE_CONFIG | PRICING_RULE | SYSTEM
        public decimal? Fee { get; set; }
        public string? Description { get; set; }
    }
}
