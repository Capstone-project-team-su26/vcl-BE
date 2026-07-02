using System;

namespace VCL_BLL.DTOs
{
    public class WarehouseResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string WarehouseType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class CreateWarehouseRequestDto
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string WarehouseType { get; set; } = "DOMESTIC";
        public bool IsActive { get; set; } = true;
    }

    public class WarehouseLocationResponseDto
    {
        public Guid LocationId { get; set; }
        public string BinCode { get; set; } = string.Empty;
        public Guid ShelfId { get; set; }
        public string ShelfCode { get; set; } = string.Empty;
        public Guid ZoneId { get; set; }
        public string ZoneName { get; set; } = string.Empty;
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public decimal? MaxVolume { get; set; }
        public decimal? MaxWeight { get; set; }
        public bool IsActive { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public class CreateLocationRequestDto
    {
        public string ZoneName { get; set; } = string.Empty;
        public string ShelfCode { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
        public decimal? MaxVolume { get; set; }
        public decimal? MaxWeight { get; set; }
        public bool IsActive { get; set; } = true;
        public string Note { get; set; } = string.Empty;
    }
}
