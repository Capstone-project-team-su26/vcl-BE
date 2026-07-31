using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class ShippingRouteDto
    {
        public Guid Id { get; set; }
        public string RouteCode { get; set; } = string.Empty;
        public string RouteName { get; set; } = string.Empty;
        public string OriginCountry { get; set; } = string.Empty;
        public Guid? OriginWarehouseId { get; set; }
        public string OriginWarehouseName { get; set; } = string.Empty;
        public string DestinationCountry { get; set; } = string.Empty;
        public Guid? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public string TransportMode { get; set; } = string.Empty;
        public Guid? CarrierId { get; set; }
        public string CarrierName { get; set; } = string.Empty;
        public Guid? SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int? EstimatedTransitDays { get; set; }
        public string Note { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class CreateShippingRouteRequest
    {
        [Required(ErrorMessage = "Mã tuyến không được để trống.")]
        [MaxLength(50, ErrorMessage = "Mã tuyến không quá 50 ký tự.")]
        public string RouteCode { get; set; } = null!;

        [Required(ErrorMessage = "Tên tuyến không được để trống.")]
        [MaxLength(255, ErrorMessage = "Tên tuyến không quá 255 ký tự.")]
        public string RouteName { get; set; } = null!;

        [Required(ErrorMessage = "Quốc gia đi không được để trống.")]
        [MaxLength(100, ErrorMessage = "Quốc gia đi không quá 100 ký tự.")]
        public string OriginCountry { get; set; } = null!;

        public Guid? OriginWarehouseId { get; set; }

        [Required(ErrorMessage = "Quốc gia đến không được để trống.")]
        [MaxLength(100, ErrorMessage = "Quốc gia đến không quá 100 ký tự.")]
        public string DestinationCountry { get; set; } = null!;

        public Guid? DestinationWarehouseId { get; set; }

        [Required(ErrorMessage = "Hình thức vận chuyển không được để trống.")]
        [RegularExpression("^(AIR|SEA|ROAD|RAIL)$", ErrorMessage = "Hình thức vận chuyển chỉ nhận: AIR, SEA, ROAD, RAIL.")]
        public string TransportMode { get; set; } = null!;

        public Guid? CarrierId { get; set; }

        public Guid? SupplierId { get; set; }

        [Range(0, 365, ErrorMessage = "Thời gian vận chuyển dự kiến phải nằm trong khoảng 0 đến 365 ngày.")]
        public int? EstimatedTransitDays { get; set; }

        [MaxLength(500, ErrorMessage = "Ghi chú không quá 500 ký tự.")]
        public string Note { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }

    public class UpdateShippingRouteRequest : CreateShippingRouteRequest
    {
    }
}
