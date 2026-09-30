using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class RegisterRequest
    {
        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required, Phone]
        public string Phone { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;

        public string? Region { get; set; }
    }

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

        /// <summary>Kho Admin đã gán cho tài khoản (rỗng = chưa gán, giới hạn theo vùng).</summary>
        public List<AssignedWarehouseBriefDto> AssignedWarehouses { get; set; } = new();
    }

    /// <summary>Kho được gán, dạng rút gọn cho danh sách người dùng.</summary>
    public class AssignedWarehouseBriefDto
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseCode { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string? Region { get; set; }
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

        // Optional
        public string? Country { get; set; }
        public string? Address { get; set; }
        public string? Region { get; set; }
    }
}
