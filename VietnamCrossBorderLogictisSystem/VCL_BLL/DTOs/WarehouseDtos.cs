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
}
