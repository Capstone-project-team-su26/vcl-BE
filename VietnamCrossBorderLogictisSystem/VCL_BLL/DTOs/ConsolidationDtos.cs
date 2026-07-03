using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class CreateConsolidationRequest
    {
        public List<Guid> OrderIds { get; set; } = new List<Guid>();
        public string? CustomMasterCode { get; set; }
        public string? Status { get; set; }
    }

    public class UpdateConsolidationRequest
    {
        public string? MasterCode { get; set; }
        public string? Status { get; set; }
        public Guid? ShipmentId { get; set; }
        public List<Guid>? OrderIds { get; set; }
    }

    public class ConsolidationResponseDto
    {
        public Guid Id { get; set; }
        public string MasterCode { get; set; } = string.Empty;
        public decimal? TotalWeight { get; set; }
        public decimal? TotalVolume { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? ShipmentId { get; set; }
        public List<ConsolidatedOrderDto> Orders { get; set; } = new List<ConsolidatedOrderDto>();
    }

    public class ConsolidatedOrderDto
    {
        public Guid Id { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public List<ConsolidatedParcelDto> Parcels { get; set; } = new List<ConsolidatedParcelDto>();
    }

    public class ConsolidatedParcelDto
    {
        public Guid Id { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string PackageStatus { get; set; } = string.Empty;
    }
}
