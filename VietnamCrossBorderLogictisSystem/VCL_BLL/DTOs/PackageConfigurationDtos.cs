using System;

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
        public decimal PackageFee { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
