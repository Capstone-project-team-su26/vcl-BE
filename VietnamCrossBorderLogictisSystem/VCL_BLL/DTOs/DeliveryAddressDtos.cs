using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class DeliveryAddressResponse
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string Address { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class CreateDeliveryAddressRequest
    {
        [Required(ErrorMessage = "Địa chỉ không được để trống.")]
        public string Address { get; set; } = string.Empty;
    }

    public class UpdateDeliveryAddressRequest
    {
        [Required(ErrorMessage = "Địa chỉ không được để trống.")]
        public string Address { get; set; } = string.Empty;
    }
}
