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
            
            // Seed Customer A
            var emailA = "customer@test.com";
            var allUsers = await userRepo.GetAllAsync();
            var existingCustomerA = allUsers.FirstOrDefault(u => u.Email == emailA);
            if (existingCustomerA == null)
            {
                var userId = Guid.NewGuid();
                var customerId = Guid.NewGuid();
                var now = DateTime.UtcNow;

                var newUser = new User
                {
                    Id = userId,
                    FullName = "Test Customer A",
                    Email = emailA,
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
                    FullName = "Test Customer A",
                    Email = emailA,
                    Phone = "0987654321",
                    Country = "Vietnam",
                    Address = "Ho Chi Minh City"
                };

                await userRepo.AddAsync(newUser);
                await customerRepo.AddAsync(newCustomer);
            }
            else
            {
                existingCustomerA.Password = PasswordHelper.HashPassword("Customer123");
                userRepo.Update(existingCustomerA);
            }

            // Seed Customer B
            var emailB = "customer_b@test.com";
            var existingCustomerB = allUsers.FirstOrDefault(u => u.Email == emailB);
            if (existingCustomerB == null)
            {
                var userId = Guid.NewGuid();
                var customerId = Guid.NewGuid();
                var now = DateTime.UtcNow;

                var newUser = new User
                {
                    Id = userId,
                    FullName = "Test Customer B",
                    Email = emailB,
                    Phone = "0966666666",
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
                    FullName = "Test Customer B",
                    Email = emailB,
                    Phone = "0966666666",
                    Country = "Vietnam",
                    Address = "Ho Chi Minh City"
                };

                await userRepo.AddAsync(newUser);
                await customerRepo.AddAsync(newCustomer);
            }
            else
            {
                existingCustomerB.Password = PasswordHelper.HashPassword("Customer123");
                userRepo.Update(existingCustomerB);
            }

            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "Created Customers A & B successfully.", emailA = emailA, emailB = emailB, password = "Customer123" });
        }

        [HttpGet("seed-admin")]
        public async Task<IActionResult> SeedAdmin()
        {
            var userRepo = _unitOfWork.Repository<User>();
            var email = "admin@test.com";
            
            var allUsers = await userRepo.GetAllAsync();
            var existingAdmin = allUsers.FirstOrDefault(u => u.Email == email);
            if (existingAdmin != null)
            {
                existingAdmin.Password = PasswordHelper.HashPassword("Admin123");
                userRepo.Update(existingAdmin);
                await _unitOfWork.SaveChangesAsync();
                return Ok(new { message = "Admin already exists, password reset.", email = email, password = "Admin123" });
            }

            var newAdmin = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Test Administrator",
                Email = email,
                Phone = "0111222333",
                Password = PasswordHelper.HashPassword("Admin123"),
                Role = "Admin",
                UserType = "Employee",
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                IsEmailVerified = true
            };

            await userRepo.AddAsync(newAdmin);
            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "Created Admin successfully.", email = email, password = "Admin123" });
        }

        [HttpGet("reset-wro-test")]
        public async Task<IActionResult> ResetWroTest()
        {
            var parcelId = Guid.Parse("3f00f601-be63-4a50-9524-88355ce84280");
            
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
            var wroItemRepo = _unitOfWork.Repository<WroItem>();

            // Get inventories for this parcel
            var allInventories = await inventoryRepo.GetAllAsync();
            var targetInventories = allInventories.Where(i => i.ParcelId == parcelId).ToList();
            var targetInventoryIds = targetInventories.Select(i => i.Id).ToList();

            // Delete WRO items associated with these inventories
            var allWroItems = await wroItemRepo.GetAllAsync();
            var wroItemsToDelete = allWroItems.Where(wi => targetInventoryIds.Contains(wi.InventoryId)).ToList();
            foreach (var item in wroItemsToDelete)
            {
                wroItemRepo.Delete(item);
            }

            // Get release requests to delete
            var wroRequestIdsToDelete = wroItemsToDelete.Select(wi => wi.WroRequestId).Distinct().ToList();

            // Explicitly delete picking items and lists first to avoid FK constraint issues
            var pickingListRepo = _unitOfWork.Repository<PickingList>();
            var pickingItemRepo = _unitOfWork.Repository<PickingItem>();
            var allPickingLists = await pickingListRepo.GetAllAsync();
            var pickingListsToDelete = allPickingLists.Where(p => wroRequestIdsToDelete.Contains(p.WroRequestId)).ToList();
            var pickingListIdsToDelete = pickingListsToDelete.Select(p => p.Id).ToList();

            var allPickingItems = await pickingItemRepo.GetAllAsync();
            var pickingItemsToDelete = allPickingItems.Where(pi => pickingListIdsToDelete.Contains(pi.PickingListId) || targetInventoryIds.Contains(pi.InventoryId)).ToList();

            foreach (var pi in pickingItemsToDelete)
            {
                pickingItemRepo.Delete(pi);
            }
            foreach (var pl in pickingListsToDelete)
            {
                pickingListRepo.Delete(pl);
            }

            foreach (var reqId in wroRequestIdsToDelete)
            {
                var req = await wroRequestRepo.GetByIdAsync(reqId);
                if (req != null)
                {
                    wroRequestRepo.Delete(req);
                }
            }

            // Delete inventories
            foreach (var inv in targetInventories)
            {
                inventoryRepo.Delete(inv);
            }

            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "Reset WRO test data successfully." });
        }
    }
}
