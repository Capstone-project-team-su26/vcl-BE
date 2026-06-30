using System;

namespace VCL_BLL.DTOs
{
    public class CustomerResponseDto
    {
        public Guid Id { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
        public string? TaxId { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class CreateCustomerRequestDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? CompanyName { get; set; }
        public string? TaxId { get; set; }
        public string Status { get; set; } = "ACTIVE";
    }

    public class UpdateCustomerRequestDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? CompanyName { get; set; }
        public string? TaxId { get; set; }
        public string Status { get; set; } = "ACTIVE";
    }
}
