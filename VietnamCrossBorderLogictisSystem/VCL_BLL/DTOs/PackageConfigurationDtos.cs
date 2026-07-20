using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class PackageConfigurationResponseDto
    {
        public Guid Id { get; set; }
        public string ConfigCode { get; set; } = string.Empty;
        public string ConfigName { get; set; } = string.Empty;
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal MaxWeight { get; set; }

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
}
