using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Helpers;
using VCL_DLL.Enums;
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
            var allUsers = await userRepo.GetAllAsync();

            // 1. Seed Warehouse Staff (General / JP)
            var staffEmail = "staff@employee.com";
            var existingStaff = allUsers.FirstOrDefault(u => u.Email == staffEmail);
            if (existingStaff != null)
            {
                existingStaff.Password = PasswordHelper.HashPassword("Employee123");
                existingStaff.Region = RegionCode.JP;
                userRepo.Update(existingStaff);
            }
            else
            {
                var newStaff = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Test Warehouse Staff (JP)",
                    Email = staffEmail,
                    Phone = "0123456789",
                    Password = PasswordHelper.HashPassword("Employee123"),
                    Role = "WarehouseStaff",
                    UserType = "Employee",
                    Status = "Active",
                    Region = RegionCode.JP,
                    CreatedAt = DateTime.UtcNow,
                    IsEmailVerified = true
                };
                await userRepo.AddAsync(newStaff);
            }

            // 2. Seed Staff JP (Japan)
            var staffJpEmail = "staff_jp@employee.com";
            var existingJp = allUsers.FirstOrDefault(u => u.Email.Equals(staffJpEmail, StringComparison.OrdinalIgnoreCase));
            if (existingJp != null)
            {
                existingJp.Password = PasswordHelper.HashPassword("Employee123");
                existingJp.Region = RegionCode.JP;
                existingJp.Role = "WarehouseStaff";
                userRepo.Update(existingJp);
            }
            else
            {
                var newJp = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Nhân viên Kho Nhật Bản",
                    Email = staffJpEmail,
                    Phone = "0811111111",
                    Password = PasswordHelper.HashPassword("Employee123"),
                    Role = "WarehouseStaff",
                    UserType = "Employee",
                    Status = "Active",
                    Region = RegionCode.JP,
                    CreatedAt = DateTime.UtcNow,
                    IsEmailVerified = true
                };
                await userRepo.AddAsync(newJp);
            }

            // 3. Seed Staff CN (China)
            var staffCnEmail = "staff_cn@employee.com";
            var existingCn = allUsers.FirstOrDefault(u => u.Email.Equals(staffCnEmail, StringComparison.OrdinalIgnoreCase));
            if (existingCn != null)
            {
                existingCn.Password = PasswordHelper.HashPassword("Employee123");
                existingCn.Region = RegionCode.CN;
                existingCn.Role = "WarehouseStaff";
                userRepo.Update(existingCn);
            }
            else
            {
                var newCn = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Nhân viên Kho Trung Quốc",
                    Email = staffCnEmail,
                    Phone = "0822222222",
                    Password = PasswordHelper.HashPassword("Employee123"),
                    Role = "WarehouseStaff",
                    UserType = "Employee",
                    Status = "Active",
                    Region = RegionCode.CN,
                    CreatedAt = DateTime.UtcNow,
                    IsEmailVerified = true
                };
                await userRepo.AddAsync(newCn);
            }

            // 4. Seed Staff KR (South Korea)
            var staffKrEmail = "staff_kr@employee.com";
            var existingKr = allUsers.FirstOrDefault(u => u.Email.Equals(staffKrEmail, StringComparison.OrdinalIgnoreCase));
            if (existingKr != null)
            {
                existingKr.Password = PasswordHelper.HashPassword("Employee123");
                existingKr.Region = RegionCode.KR;
                existingKr.Role = "WarehouseStaff";
                userRepo.Update(existingKr);
            }
            else
            {
                var newKr = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Nhân viên Kho Hàn Quốc",
                    Email = staffKrEmail,
                    Phone = "0833333333",
                    Password = PasswordHelper.HashPassword("Employee123"),
                    Role = "WarehouseStaff",
                    UserType = "Employee",
                    Status = "Active",
                    Region = RegionCode.KR,
                    CreatedAt = DateTime.UtcNow,
                    IsEmailVerified = true
                };
                await userRepo.AddAsync(newKr);
            }

            // Seed Sales Staff
            var salesEmail = "sales@test.com";
            var existingSales = allUsers.FirstOrDefault(u => u.Email == salesEmail);
            if (existingSales != null)
            {
                existingSales.Password = PasswordHelper.HashPassword("Sales123");
                userRepo.Update(existingSales);
            }
            else
            {
                var newSales = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Test Sales Staff",
                    Email = salesEmail,
                    Phone = "0988888888",
                    Password = PasswordHelper.HashPassword("Sales123"),
                    Role = "Sale",
                    UserType = "Employee",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    IsEmailVerified = true
                };
                await userRepo.AddAsync(newSales);
            }

            await _unitOfWork.SaveChangesAsync();

            return Ok(new 
            { 
                message = "Created JP, CN, KR WarehouseStaff and Sales staff successfully.", 
                staffJpEmail = staffJpEmail,
                staffCnEmail = staffCnEmail,
                staffKrEmail = staffKrEmail,
                staffEmail = staffEmail, 
                staffPassword = "Employee123",
                salesEmail = salesEmail,
                salesPassword = "Sales123"
            });
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

        [HttpGet("seed-orders")]
        public async Task<IActionResult> SeedOrders()
        {
            var userRepo = _unitOfWork.Repository<User>();
            var customerRepo = _unitOfWork.Repository<Customer>();
            var orderRepo = _unitOfWork.Repository<Order>();
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var purchaseReqRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseReqItemRepo = _unitOfWork.Repository<PurchaseRequestItem>();

            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault();
            if (customer == null)
            {
                await SeedCustomer();
                allCustomers = await customerRepo.GetAllAsync();
                customer = allCustomers.FirstOrDefault();
            }

            if (customer == null)
            {
                return BadRequest("Không thể khởi tạo khách hàng mẫu.");
            }

            var now = DateTime.UtcNow;
            var createdOrders = new System.Collections.Generic.List<object>();

            // 1. Tạo Đơn Ký Gửi 1 (CONSIGNMENT) - Trạng thái: DÃ DUYỆT (APPROVED)
            var kgOrder1Id = Guid.NewGuid();
            var kgOrder1Code = $"KG-{now:yyyyMMdd}-1001";
            var kgOrder1 = new Order
            {
                Id = kgOrder1Id,
                CustomerId = customer.Id,
                ConsignmentCode = kgOrder1Code,
                ConsignmentType = "KY_GUI",
                OrderType = "CONSIGNMENT",
                Status = "APPROVED",
                Route = "CN-VN",
                ReceiverName = customer.FullName ?? "Nguyễn Văn A",
                ReceiverPhone = customer.Phone ?? "0987654321",
                ReceiverAddress = customer.Address ?? "123 Nguyễn Huệ, Q.1, TP.HCM",
                TotalWeight = 5.5m,
                TotalVolume = 0.02m,
                CreatedAt = now.AddHours(-5)
            };
            await orderRepo.AddAsync(kgOrder1);

            var kgItem1 = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = kgOrder1Id,
                ProductName = "Quần áo thời trang mẫu A",
                Sku = "CLOTH-001",
                Quantity = 10,
                Weight = 2.5m,
                DeclaredValue = 500000m,
                DomesticTrackingCode = "TRACK-CN-8801",
                CreatedAt = now.AddHours(-5)
            };
            var kgItem2 = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = kgOrder1Id,
                ProductName = "Giày thể thao Nam",
                Sku = "SHOES-002",
                Quantity = 5,
                Weight = 3.0m,
                DeclaredValue = 1200000m,
                DomesticTrackingCode = "TRACK-CN-8802",
                CreatedAt = now.AddHours(-5)
            };
            await orderItemRepo.AddAsync(kgItem1);
            await orderItemRepo.AddAsync(kgItem2);

            var kgParcel1 = new Parcel
            {
                Id = Guid.NewGuid(),
                ConsignmentId = kgOrder1Id,
                OrderItemId = kgItem1.Id,
                PackageCode = $"PKG-{now:yyyyMMdd}-001",
                ActualWeight = 2.5m,
                VolumetricWeight = 2.0m,
                ChargeableWeight = 2.5m,
                PackageStatus = "PENDING_CHECKIN"
            };
            var kgParcel2 = new Parcel
            {
                Id = Guid.NewGuid(),
                ConsignmentId = kgOrder1Id,
                OrderItemId = kgItem2.Id,
                PackageCode = $"PKG-{now:yyyyMMdd}-002",
                ActualWeight = 3.0m,
                VolumetricWeight = 2.8m,
                ChargeableWeight = 3.0m,
                PackageStatus = "PENDING_CHECKIN"
            };
            await parcelRepo.AddAsync(kgParcel1);
            await parcelRepo.AddAsync(kgParcel2);
            createdOrders.Add(new { code = kgOrder1Code, type = "CONSIGNMENT", status = "APPROVED", items = 2 });

            // 2. Tạo Đơn Ký Gửi 2 (CONSIGNMENT) - Trạng thái: ĐÃ VÀO KHO (IN_WAREHOUSE)
            var kgOrder2Id = Guid.NewGuid();
            var kgOrder2Code = $"KG-{now:yyyyMMdd}-1002";
            var kgOrder2 = new Order
            {
                Id = kgOrder2Id,
                CustomerId = customer.Id,
                ConsignmentCode = kgOrder2Code,
                ConsignmentType = "KY_GUI",
                OrderType = "CONSIGNMENT",
                Status = "IN_WAREHOUSE",
                Route = "JP-VN",
                ReceiverName = customer.FullName ?? "Nguyễn Văn A",
                ReceiverPhone = customer.Phone ?? "0987654321",
                ReceiverAddress = customer.Address ?? "123 Nguyễn Huệ, Q.1, TP.HCM",
                TotalWeight = 1.2m,
                TotalVolume = 0.008m,
                CreatedAt = now.AddDays(-1)
            };
            await orderRepo.AddAsync(kgOrder2);

            var kgItem3 = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = kgOrder2Id,
                ProductName = "Mỹ phẩm Nhật Bản SK-II",
                Sku = "COSM-JP-001",
                Quantity = 3,
                Weight = 1.2m,
                DeclaredValue = 3500000m,
                DomesticTrackingCode = "TRACK-JP-9901",
                CreatedAt = now.AddDays(-1)
            };
            await orderItemRepo.AddAsync(kgItem3);

            var kgParcel3 = new Parcel
            {
                Id = Guid.NewGuid(),
                ConsignmentId = kgOrder2Id,
                OrderItemId = kgItem3.Id,
                PackageCode = $"PKG-{now:yyyyMMdd}-003",
                ActualWeight = 1.2m,
                VolumetricWeight = 1.0m,
                ChargeableWeight = 1.2m,
                PackageStatus = "IN_WAREHOUSE",
                CheckInTime = now.AddHours(-10)
            };
            await parcelRepo.AddAsync(kgParcel3);
            createdOrders.Add(new { code = kgOrder2Code, type = "CONSIGNMENT", status = "IN_WAREHOUSE", items = 1 });

            // 3. Tạo Yêu Cầu Mua Hộ + Đơn Mua Hộ (PURCHASE)
            var prId = Guid.NewGuid();
            var prCode = $"PR-{now:yyyyMMdd}-2001";
            var purchaseReq = new PurchaseRequest
            {
                Id = prId,
                CustomerId = customer.Id,
                UserId = customer.UserId,
                PurchaseCode = prCode,
                Route = "CN-VN",
                ShippingOption = "STANDARD",
                ReceiverName = customer.FullName ?? "Nguyễn Văn A",
                ReceiverPhone = customer.Phone ?? "0987654321",
                ReceiverAddress = customer.Address ?? "123 Nguyễn Huệ, Q.1, TP.HCM",
                Status = "APPROVED",
                CreatedAt = now.AddDays(-2)
            };
            await purchaseReqRepo.AddAsync(purchaseReq);

            var prItem1 = new PurchaseRequestItem
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = prId,
                ProductName = "Tai nghe Bluetooth Wireless Pro",
                ProductLink = "https://item.taobao.com/item.htm?id=61002345",
                SourceWebsite = "Taobao",
                Quantity = 2
            };
            await purchaseReqItemRepo.AddAsync(prItem1);

            var mhOrderId = Guid.NewGuid();
            var mhOrderCode = $"MH-{now:yyyyMMdd}-2001";
            var mhOrder = new Order
            {
                Id = mhOrderId,
                CustomerId = customer.Id,
                PurchaseRequestId = prId,
                ConsignmentCode = mhOrderCode,
                ConsignmentType = "MUA_HO",
                OrderType = "PURCHASE",
                Status = "APPROVED",
                Route = "CN-VN",
                ReceiverName = customer.FullName ?? "Nguyễn Văn A",
                ReceiverPhone = customer.Phone ?? "0987654321",
                ReceiverAddress = customer.Address ?? "123 Nguyễn Huệ, Q.1, TP.HCM",
                TotalWeight = 0.8m,
                CreatedAt = now.AddDays(-2)
            };
            await orderRepo.AddAsync(mhOrder);

            var mhItem1 = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = mhOrderId,
                ProductName = "Tai nghe Bluetooth Wireless Pro",
                Sku = "EAR-001",
                Quantity = 2,
                Weight = 0.8m,
                DeclaredValue = 1050000m,
                CreatedAt = now.AddDays(-2)
            };
            await orderItemRepo.AddAsync(mhItem1);

            var mhParcel = new Parcel
            {
                Id = Guid.NewGuid(),
                ConsignmentId = mhOrderId,
                OrderItemId = mhItem1.Id,
                PackageCode = $"PKG-{now:yyyyMMdd}-004",
                ActualWeight = 0.8m,
                VolumetricWeight = 0.6m,
                ChargeableWeight = 0.8m,
                PackageStatus = "PENDING_CHECKIN"
            };
            await parcelRepo.AddAsync(mhParcel);
            createdOrders.Add(new { code = mhOrderCode, type = "PURCHASE", status = "APPROVED", items = 1 });

            await _unitOfWork.SaveChangesAsync();

            return Ok(new
            {
                message = "Tạo các đơn ký gửi và mua hộ mẫu thành công!",
                customer = customer.FullName,
                email = customer.Email,
                orders = createdOrders
            });
        }
    }
}
