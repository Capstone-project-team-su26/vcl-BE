using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Helpers;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;

        public TestController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        [HttpGet("seed-staff")]
        public async Task<IActionResult> SeedStaff()
        {
            var userRepo = _unitOfWork.Repository<User>();
            var email = "staff@employee.com";
            
            var allUsers = await userRepo.GetAllAsync();
            var existingStaff = allUsers.FirstOrDefault(u => u.Email == email);
            if (existingStaff != null)
            {
                existingStaff.Password = PasswordHelper.HashPassword("Employee123");
                userRepo.Update(existingStaff);
                await _unitOfWork.SaveChangesAsync();
                return Ok(new { message = "WarehouseStaff already exists, password reset.", email = email, password = "Employee123" });
            }

            var newStaff = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Test Warehouse Staff",
                Email = email,
                Phone = "0123456789",
                Password = PasswordHelper.HashPassword("Employee123"),
                Role = "WarehouseStaff",
                UserType = "Employee",
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                IsEmailVerified = true
            };

            await userRepo.AddAsync(newStaff);
            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "Created WarehouseStaff successfully.", email = email, password = "Employee123" });
        }

        [HttpGet("seed-customer")]
        public async Task<IActionResult> SeedCustomer()
        {
            var userRepo = _unitOfWork.Repository<User>();
            var customerRepo = _unitOfWork.Repository<Customer>();
            var email = "customer@test.com";
            
            var allUsers = await userRepo.GetAllAsync();
            var existingCustomer = allUsers.FirstOrDefault(u => u.Email == email);
            if (existingCustomer != null)
            {
                existingCustomer.Password = PasswordHelper.HashPassword("Customer123");
                userRepo.Update(existingCustomer);
                await _unitOfWork.SaveChangesAsync();
                return Ok(new { message = "Customer already exists, password reset.", email = email, password = "Customer123" });
            }

            var userId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var newUser = new User
            {
                Id = userId,
                FullName = "Test Customer",
                Email = email,
                Phone = "0987654321",
                Password = PasswordHelper.HashPassword("Customer123"),
                Role = "Customer",
                UserType = "Customer",
                Status = "Active",
                CreatedAt = now,
                IsEmailVerified = true
            };

            var newCustomer = new Customer
            {
                Id = customerId,
                UserId = userId,
                CustomerCode = $"CUS-{now:yyyyMMddHHmmss}-{customerId.ToString("N")[..6].ToUpperInvariant()}",
                CustomerType = "Individual",
                Status = "Active",
                CreatedAt = now,
                FullName = "Test Customer",
                Email = email,
                Phone = "0987654321",
                Country = "Vietnam",
                Address = "Ho Chi Minh City"
            };

            await userRepo.AddAsync(newUser);
            await customerRepo.AddAsync(newCustomer);
            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "Created Customer successfully.", email = email, password = "Customer123" });
        }
    }
}
