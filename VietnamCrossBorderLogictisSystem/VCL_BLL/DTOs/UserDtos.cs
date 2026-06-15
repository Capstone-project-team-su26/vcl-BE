using System;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public record RegisterRequest(string FullName, string Email, string Password, string Phone, string Role);

    public class UserResponse
    {
        public Guid Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Role { get; set; }
        public string UserType { get; set; }
        public string Status { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class UpdateUserRoleRequest
    {
        [Required]
        public string Role { get; set; }
    }
}
