using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class ParcelCheckInRequest
    {
        public string ShippingCode { get; set; } = string.Empty;
        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
    }

    public class ParcelCheckInResponse
    {
        public string ShippingCode { get; set; } = string.Empty;
        public Guid ConsignmentId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public List<DeclaredItemDto> DeclaredItems { get; set; } = new List<DeclaredItemDto>();
        public string PackageCode { get; set; } = string.Empty;
        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string PackageStatus { get; set; } = string.Empty;
    }

    public class DeclaredItemDto
    {
        public string ProductName { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? DeclaredValue { get; set; }
    }

    public class PendingPutawayParcelDto
    {
        public Guid Id { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string PackageStatus { get; set; } = string.Empty;
        public DateTime? CheckInTime { get; set; }
    }

    public class ParcelPutAwayRequest
    {
        public Guid WarehouseId { get; set; }
        public Guid ZoneId { get; set; }
        public Guid ShelfId { get; set; }
        public Guid BinId { get; set; }
    }

    public class ParcelPutAwayResponse
    {
        public string PackageCode { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public string ShelfCode { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
        public DateTime PutAwayTime { get; set; }
    }
}
