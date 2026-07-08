using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// DTO chứa thông tin chi tiết của một đơn vị vận chuyển.
    /// </summary>
    public class CarrierDto
    {
        /// <summary>
        /// ID duy nhất của đơn vị vận chuyển.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Tên đơn vị vận chuyển (ví dụ: Viettel Post, DHL, FedEx).
        /// </summary>
        public string CarrierName { get; set; } = null!;

        /// <summary>
        /// Mã định danh duy nhất của đơn vị vận chuyển (chữ, số, gạch dưới/gạch ngang).
        /// </summary>
        public string CarrierCode { get; set; } = null!;

        /// <summary>
        /// Phân loại (ví dụ: TRUCK, AIR, SEA, COURIER).
        /// </summary>
        public string CarrierType { get; set; } = null!;

        /// <summary>
        /// Đường dẫn API tích hợp tra cứu vận đơn nếu có.
        /// </summary>
        public string? ApiUrl { get; set; }

        /// <summary>
        /// Email liên hệ của đơn vị vận chuyển.
        /// </summary>
        public string? ContactEmail { get; set; }

        /// <summary>
        /// Số điện thoại liên hệ của đơn vị vận chuyển.
        /// </summary>
        public string? ContactPhone { get; set; }

        /// <summary>
        /// Phương thức vận chuyển hỗ trợ (ví dụ: Hỏa tốc, Tiết kiệm, Đường bộ, Đường hàng không).
        /// </summary>
        public string? SupportedShippingMethods { get; set; }

        /// <summary>
        /// Khu vực/Quốc gia hỗ trợ vận chuyển.
        /// </summary>
        public string? SupportedRegions { get; set; }

        /// <summary>
        /// Trạng thái hoạt động (true: đang hoạt động, false: tạm dừng).
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Ghi chú nội bộ dành cho Admin/Staff.
        /// </summary>
        public string? InternalNotes { get; set; }
    }

    /// <summary>
    /// DTO yêu cầu tạo mới đơn vị vận chuyển.
    /// </summary>
    public class CreateCarrierRequestDto
    {
        /// <summary>
        /// Tên đơn vị vận chuyển. Bắt buộc, tối đa 255 ký tự.
        /// </summary>
        [Required(ErrorMessage = "Tên đơn vị vận chuyển không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên đơn vị vận chuyển không quá 255 ký tự.")]
        public string CarrierName { get; set; } = null!;

        /// <summary>
        /// Mã định danh duy nhất của đơn vị vận chuyển. Bắt buộc, tối đa 100 ký tự. Chỉ nhận chữ, số, gạch dưới (_) và gạch ngang (-).
        /// </summary>
        [Required(ErrorMessage = "Mã đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã đơn vị vận chuyển không quá 100 ký tự.")]
        [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "Mã đơn vị vận chuyển chỉ được chứa ký tự chữ, số, gạch dưới và gạch ngang.")]
        public string CarrierCode { get; set; } = null!;

        /// <summary>
        /// Phân loại đơn vị vận chuyển (ví dụ: TRUCK, AIR, SEA, COURIER). Bắt buộc, tối đa 100 ký tự.
        /// </summary>
        [Required(ErrorMessage = "Loại đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại đơn vị vận chuyển không quá 100 ký tự.")]
        public string CarrierType { get; set; } = null!;

        /// <summary>
        /// Đường dẫn API tích hợp tra cứu vận đơn nếu có. Định dạng URL hợp lệ.
        /// </summary>
        [Url(ErrorMessage = "Đường dẫn API không hợp lệ.")]
        public string? ApiUrl { get; set; }

        /// <summary>
        /// Email liên hệ của đơn vị vận chuyển. Định dạng email hợp lệ, tối đa 255 ký tự.
        /// </summary>
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
        [MaxLength(255, ErrorMessage = "Email liên hệ không quá 255 ký tự.")]
        public string? ContactEmail { get; set; }

        /// <summary>
        /// Số điện thoại liên hệ của đơn vị vận chuyển. Tối đa 50 ký tự.
        /// </summary>
        [MaxLength(50, ErrorMessage = "Số điện thoại liên hệ không quá 50 ký tự.")]
        public string? ContactPhone { get; set; }

        /// <summary>
        /// Các phương thức vận chuyển hỗ trợ. Tối đa 500 ký tự.
        /// </summary>
        [MaxLength(500, ErrorMessage = "Phương thức vận chuyển hỗ trợ không quá 500 ký tự.")]
        public string? SupportedShippingMethods { get; set; }

        /// <summary>
        /// Các khu vực/Quốc gia hỗ trợ. Tối đa 500 ký tự.
        /// </summary>
        [MaxLength(500, ErrorMessage = "Quốc gia/khu vực hỗ trợ không quá 500 ký tự.")]
        public string? SupportedRegions { get; set; }

        /// <summary>
        /// Trạng thái hoạt động (mặc định là true).
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Ghi chú nội bộ dành cho hệ thống. Tối đa 1000 ký tự.
        /// </summary>
        [MaxLength(1000, ErrorMessage = "Ghi chú nội bộ không quá 1000 ký tự.")]
        public string? InternalNotes { get; set; }
    }

    /// <summary>
    /// DTO yêu cầu cập nhật thông tin đơn vị vận chuyển.
    /// </summary>
    public class UpdateCarrierRequestDto
    {
        /// <summary>
        /// Tên đơn vị vận chuyển. Bắt buộc, tối đa 255 ký tự.
        /// </summary>
        [Required(ErrorMessage = "Tên đơn vị vận chuyển không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên đơn vị vận chuyển không quá 255 ký tự.")]
        public string CarrierName { get; set; } = null!;

        /// <summary>
        /// Mã định danh duy nhất của đơn vị vận chuyển. Bắt buộc, tối đa 100 ký tự. Chỉ nhận chữ, số, gạch dưới (_) và gạch ngang (-).
        /// </summary>
        [Required(ErrorMessage = "Mã đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Mã đơn vị vận chuyển không quá 100 ký tự.")]
        [RegularExpression(@"^[a-zA-Z0-9_\-]+$", ErrorMessage = "Mã đơn vị vận chuyển chỉ được chứa ký tự chữ, số, gạch dưới và gạch ngang.")]
        public string CarrierCode { get; set; } = null!;

        /// <summary>
        /// Phân loại đơn vị vận chuyển. Bắt buộc, tối đa 100 ký tự.
        /// </summary>
        [Required(ErrorMessage = "Loại đơn vị vận chuyển không được để trống.")]
        [MaxLength(100, ErrorMessage = "Loại đơn vị vận chuyển không quá 100 ký tự.")]
        public string CarrierType { get; set; } = null!;

        /// <summary>
        /// Đường dẫn API tích hợp tra cứu vận đơn nếu có. Định dạng URL hợp lệ.
        /// </summary>
        [Url(ErrorMessage = "Đường dẫn API không hợp lệ.")]
        public string? ApiUrl { get; set; }

        /// <summary>
        /// Email liên hệ của đơn vị vận chuyển. Định dạng email hợp lệ, tối đa 255 ký tự.
        /// </summary>
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
        [MaxLength(255, ErrorMessage = "Email liên hệ không quá 255 ký tự.")]
        public string? ContactEmail { get; set; }

        /// <summary>
        /// Số điện thoại liên hệ của đơn vị vận chuyển. Tối đa 50 ký tự.
        /// </summary>
        [MaxLength(50, ErrorMessage = "Số điện thoại liên hệ không quá 50 ký tự.")]
        public string? ContactPhone { get; set; }

        /// <summary>
        /// Các phương thức vận chuyển hỗ trợ. Tối đa 500 ký tự.
        /// </summary>
        [MaxLength(500, ErrorMessage = "Phương thức vận chuyển hỗ trợ không quá 500 ký tự.")]
        public string? SupportedShippingMethods { get; set; }

        /// <summary>
        /// Các khu vực/Quốc gia hỗ trợ. Tối đa 500 ký tự.
        /// </summary>
        [MaxLength(500, ErrorMessage = "Quốc gia/khu vực hỗ trợ không quá 500 ký tự.")]
        public string? SupportedRegions { get; set; }

        /// <summary>
        /// Trạng thái hoạt động.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Ghi chú nội bộ dành cho hệ thống. Tối đa 1000 ký tự.
        /// </summary>
        [MaxLength(1000, ErrorMessage = "Ghi chú nội bộ không quá 1000 ký tự.")]
        public string? InternalNotes { get; set; }
    }
}
