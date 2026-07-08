using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CreateInternationalShipmentDto
    {
        [Required]
        [MinLength(1, ErrorMessage = "Phải chọn ít nhất một yêu cầu xuất kho.")]
        public List<Guid> WroRequestIds { get; set; } = new();

        [Required(ErrorMessage = "Kho đi không được để trống.")]
        public Guid OriginWarehouseId { get; set; }

        [Required(ErrorMessage = "Kho đến không được để trống.")]
        public Guid DestinationWarehouseId { get; set; }

        /// <summary>Đơn vị vận chuyển (nếu có).</summary>
        public Guid? CarrierId { get; set; }

        /// <summary>Phương thức vận chuyển (nếu có), VD: Sea, Air.</summary>
        public string? ShippingMethod { get; set; }
    }

    public class InternationalShipmentWroDto
    {
        public Guid WroRequestId { get; set; }
        public string WroCode { get; set; } = string.Empty;
    }

    public class InternationalShipmentParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public decimal? Weight { get; set; }
    }

    public class UpdateInternationalShipmentStatusDto
    {
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        /// <summary>Ghi chú vận chuyển (nếu có).</summary>
        public string? Note { get; set; }
    }

    public class UpdateInternationalShipmentStatusResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string UpdatedByName { get; set; } = string.Empty;
    }

    public class ConfirmShipmentArrivalDto
    {
        /// <summary>Ghi chú khi xác nhận đến kho đích (nếu có).</summary>
        public string? Note { get; set; }
    }

    public class ConfirmShipmentArrivalResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public Guid? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ArrivedAt { get; set; }
        public string ConfirmedByName { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int TotalPackages { get; set; }
        public List<InternationalShipmentParcelDto> Parcels { get; set; } = new();
    }

    public class DestinationReceivingDto
    {
        /// <summary>Danh sách Parcel cần xác nhận nhận (bỏ trống = nhận tất cả kiện trong lô chưa nhận).</summary>
        public List<Guid>? ParcelIds { get; set; }

        public string? Note { get; set; }
    }

    public class DestinationReceivingParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
    }

    public class DestinationReceivingResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public Guid? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
        public string ReceivedByName { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int ReceivedCount { get; set; }
        public List<DestinationReceivingParcelDto> Parcels { get; set; } = new();
    }

    public class InternationalShipmentResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public Guid? OriginWarehouseId { get; set; }
        public string OriginWarehouseName { get; set; } = string.Empty;
        public Guid? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public Guid? CarrierId { get; set; }
        public string? CarrierName { get; set; }
        public string? ShippingMethod { get; set; }
        public int TotalPackages { get; set; }
        public decimal TotalWeight { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<InternationalShipmentWroDto> WroRequests { get; set; } = new();
        public List<InternationalShipmentParcelDto> Parcels { get; set; } = new();
    }
}
