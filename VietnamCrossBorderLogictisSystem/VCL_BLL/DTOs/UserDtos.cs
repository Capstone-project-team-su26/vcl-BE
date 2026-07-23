using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public record RegisterRequest(string FullName, string Email, string Password, string Phone, string Role, string? Region = null);

    public class UserResponse
    {
        public Guid Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Role { get; set; }
        public string UserType { get; set; }
        public string Status { get; set; }
        public string? Region { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class UpdateUserRoleRequest
    {
        [Required]
        public string Role { get; set; }
        public string? Region { get; set; }
    }

    public class UserProfileResponse
    {
        public Guid Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Role { get; set; }
        public string UserType { get; set; }
        public string Status { get; set; }
        public string? Region { get; set; }
        public DateTime? CreatedAt { get; set; }
        
        // Customer-specific fields (null for employees)
        public string CustomerCode { get; set; }
        public string CustomerType { get; set; }
        public string Country { get; set; }
        public string Address { get; set; }
    }

    public class UpdateProfileRequest
    {
        [Required(ErrorMessage = "Họ tên không được để trống.")]
        public string FullName { get; set; }

        [Required(ErrorMessage = "Số điện thoại không được để trống.")]
        public string Phone { get; set; }

        // Optional, only used for Customers
        public string Country { get; set; }
        public string Address { get; set; }
    }
}
