using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class DeliveryAddressResponse
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateDeliveryAddressRequest
    {
        [Required(ErrorMessage = "Tên người nhận không được để trống.")]
        public string ReceiverName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số điện thoại không được để trống.")]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Địa chỉ không được để trống.")]
        public string Address { get; set; } = string.Empty;

        /// <summary>Đặt làm địa chỉ mặc định.</summary>
        public bool IsDefault { get; set; }
    }

    public class UpdateDeliveryAddressRequest
    {
        [Required(ErrorMessage = "Tên người nhận không được để trống.")]
        public string ReceiverName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số điện thoại không được để trống.")]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Địa chỉ không được để trống.")]
        public string Address { get; set; } = string.Empty;

        public bool IsDefault { get; set; }
    }
}
