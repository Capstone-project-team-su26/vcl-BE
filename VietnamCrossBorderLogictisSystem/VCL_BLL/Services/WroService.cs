using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class WroService : IWroService
    {
        private readonly IUnitOfWork _unitOfWork;

        public WroService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<WroResponseDto> CreateWroRequestAsync(Guid userId, string userRole, CreateWroRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu xuất kho không được để trống.");

            // 1. Phân quyền và tìm Customer ID nếu là Customer
            Customer? customer = null;
            bool isCustomer = string.Equals(userRole, "Customer", StringComparison.OrdinalIgnoreCase);
            if (isCustomer)
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var allCustomers = await customerRepo.GetAllAsync();
                customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null)
                {
                    throw new UnauthorizedAccessException("Không tìm thấy thông tin khách hàng tương ứng với tài khoản.");
                }
            }

            // 2. Kiểm tra tính hợp lệ của từng Inventory item (Validate phase)
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var binRepo = _unitOfWork.Repository<Bin>();
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();

            foreach (var item in request.Items)
            {
                var inventory = await inventoryRepo.GetByIdAsync(item.InventoryId);
                if (inventory == null)
                {
                    throw new NotFoundException($"Không tìm thấy tồn kho với ID: {item.InventoryId}");
                }

                // A. Kiểm tra quyền sở hữu đối với Customer
                if (isCustomer && inventory.CustomerId != customer!.Id)
                {
                    throw new UnauthorizedAccessException($"Bạn không có quyền tạo yêu cầu xuất kho cho tồn kho {item.InventoryId} do không thuộc sở hữu của bạn.");
                }

                // B. Kiểm tra vị trí kho (Customer chỉ được xuất ở kho đích)
                var bin = await binRepo.GetByIdAsync(inventory.BinId);
                if (bin == null)
                {
                    throw new NotFoundException($"Không tìm thấy vị trí lưu trữ liên kết với tồn kho {item.InventoryId}.");
                }

                var warehouse = await warehouseRepo.GetByIdAsync(bin.WarehouseId ?? Guid.Empty);
                if (warehouse == null)
                {
                    throw new NotFoundException($"Không tìm thấy kho hàng lưu trữ liên kết với tồn kho {item.InventoryId}.");
                }

                if (isCustomer)
                {
                    bool isDestination = string.Equals(warehouse.WarehouseType, "Destination", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(warehouse.Country, "Vietnam", StringComparison.OrdinalIgnoreCase);
                    if (!isDestination)
                    {
                        throw new UnauthorizedAccessException($"Khách hàng chỉ được phép tạo yêu cầu xuất kho tại kho đích (Vietnam). Kho hiện tại: {warehouse.WarehouseName}.");
                    }
                }

                // C. Kiểm tra số lượng và trạng thái khả dụng
                if (item.Quantity <= 0)
                {
                    throw new ArgumentException("Số lượng xuất kho phải lớn hơn 0.");
                }

                if (!string.Equals(inventory.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Hàng tồn kho với ID {item.InventoryId} không khả dụng để xuất (Trạng thái hiện tại: {inventory.Status}).");
                }

                if (inventory.Quantity < item.Quantity)
                {
                    throw new ArgumentException($"Số lượng xuất ({item.Quantity}) vượt quá số lượng tồn kho khả dụng ({inventory.Quantity}) của mặt hàng.");
                }
            }

            // 3. Khởi tạo WRO Request
            var wroCode = $"WRO-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";
            var wroRequest = new WroRequest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CustomerId = customer?.Id,
                WroCode = wroCode,
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                DeliveryAddress = request.DeliveryAddress,
                ShelfCode = request.ShelfCode,
                Status = "PENDING_REVIEW",
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Repository<WroRequest>().AddAsync(wroRequest);

            // 4. Giữ số lượng tồn ở trạng thái RESERVED và tạo WroItem liên kết
            var wroItemRepo = _unitOfWork.Repository<WroItem>();
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var resultItems = new List<WroItemResponseDto>();

            foreach (var item in request.Items)
            {
                var inventory = await inventoryRepo.GetByIdAsync(item.InventoryId);

                // A. Giảm số lượng tồn AVAILABLE
                inventory.Quantity -= item.Quantity;
                inventoryRepo.Update(inventory);

                // B. Tạo bản ghi RESERVED mới
                var reservedInventory = new Inventory
                {
                    Id = Guid.NewGuid(),
                    CustomerId = inventory.CustomerId,
                    OrderId = inventory.OrderId,
                    OrderItemId = inventory.OrderItemId,
                    ParcelId = inventory.ParcelId,
                    BinId = inventory.BinId,
                    Quantity = item.Quantity,
                    Status = "RESERVED",
                    CreatedAt = DateTime.UtcNow
                };
                await inventoryRepo.AddAsync(reservedInventory);

                // C. Tạo WroItem liên kết
                var wroItem = new WroItem
                {
                    Id = Guid.NewGuid(),
                    WroRequestId = wroRequest.Id,
                    InventoryId = reservedInventory.Id,
                    Quantity = item.Quantity
                };
                await wroItemRepo.AddAsync(wroItem);

                // Lấy thông tin mã kiện hàng để trả về response
                var parcel = await parcelRepo.GetByIdAsync(inventory.ParcelId);
                var packageCode = parcel?.PackageCode ?? string.Empty;

                resultItems.Add(new WroItemResponseDto
                {
                    ItemId = wroItem.Id,
                    InventoryId = reservedInventory.Id,
                    Quantity = wroItem.Quantity,
                    PackageCode = packageCode
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 5. Trả về thông tin WRO kết quả
            return new WroResponseDto
            {
                WroId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                CustomerId = wroRequest.CustomerId,
                ReceiverName = wroRequest.ReceiverName,
                ReceiverPhone = wroRequest.ReceiverPhone,
                DeliveryAddress = wroRequest.DeliveryAddress,
                Status = wroRequest.Status,
                ShelfCode = wroRequest.ShelfCode,
                CreatedAt = wroRequest.CreatedAt ?? DateTime.UtcNow,
                Items = resultItems
            };
        }

        public async Task<WroDetailResponseDto> GetWroRequestDetailAsync(Guid requestId, Guid userId, string role)
        {
            var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRequestRepo.GetByIdAsync(requestId);
            if (wroRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {requestId}");
            }

            // 1. Phân quyền: Customer chỉ được xem yêu cầu xuất kho của chính mình
            bool isCustomer = string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase);
            if (isCustomer)
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var allCustomers = await customerRepo.GetAllAsync();
                var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null || wroRequest.CustomerId != customer.Id)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền xem yêu cầu xuất kho này.");
                }
            }

            // 2. Lấy thông tin người tạo (User)
            var userRepo = _unitOfWork.Repository<User>();
            var creator = wroRequest.UserId.HasValue ? await userRepo.GetByIdAsync(wroRequest.UserId.Value) : null;
            string createdByName = creator?.FullName ?? "Hệ thống";
            string createdByUserRole = creator?.Role ?? "System";

            // 3. Lấy thông tin Customer sở hữu
            string customerName = "";
            if (wroRequest.CustomerId.HasValue)
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var wroOwner = await customerRepo.GetByIdAsync(wroRequest.CustomerId.Value);
                customerName = wroOwner?.FullName ?? string.Empty;
            }

            // 4. Lấy chi tiết các mặt hàng cần xuất và thông tin kho hàng liên quan
            var wroItemRepo = _unitOfWork.Repository<WroItem>();
            var allWroItems = await wroItemRepo.GetAllAsync();
            var relatedWroItems = allWroItems.Where(i => i.WroRequestId == requestId).ToList();

            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();

            var binRepo = _unitOfWork.Repository<Bin>();
            var allBins = await binRepo.GetAllAsync();

            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var allWarehouses = await warehouseRepo.GetAllAsync();

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allOrderItems = await orderItemRepo.GetAllAsync();

            var itemsDetail = new List<WroDetailItemDto>();
            string warehouseName = "";
            int totalQuantity = 0;

            foreach (var item in relatedWroItems)
            {
                var inventory = allInventories.FirstOrDefault(inv => inv.Id == item.InventoryId);
                string productName = "";
                string packageCode = "";
                string binCode = "";

                if (inventory != null)
                {
                    // Lấy tên mặt hàng từ OrderItem tương ứng của Inventory
                    if (inventory.OrderItemId.HasValue)
                    {
                        var orderItem = allOrderItems.FirstOrDefault(oi => oi.Id == inventory.OrderItemId.Value);
                        productName = orderItem?.ProductName ?? string.Empty;
                    }

                    // Lấy mã kiện hàng Parcel tương ứng
                    var parcel = allParcels.FirstOrDefault(p => p.Id == inventory.ParcelId);
                    packageCode = parcel?.PackageCode ?? string.Empty;

                    // Lấy mã vị trí Bin và tên kho hàng Warehouse
                    var bin = allBins.FirstOrDefault(b => b.Id == inventory.BinId);
                    if (bin != null)
                    {
                        binCode = bin.BinCode ?? string.Empty;

                        if (string.IsNullOrEmpty(warehouseName))
                        {
                            var whId = bin.WarehouseId;
                            if (whId.HasValue)
                            {
                                var warehouse = allWarehouses.FirstOrDefault(w => w.Id == whId.Value);
                                warehouseName = warehouse?.WarehouseName ?? string.Empty;
                            }
                        }
                    }
                }

                itemsDetail.Add(new WroDetailItemDto
                {
                    ItemId = item.Id,
                    InventoryId = item.InventoryId,
                    Quantity = item.Quantity,
                    PackageCode = packageCode,
                    ProductName = productName,
                    BinCode = binCode
                });

                totalQuantity += item.Quantity;
            }

            return new WroDetailResponseDto
            {
                WroId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                CreatedByName = createdByName,
                CreatedByUserRole = createdByUserRole,
                WarehouseName = warehouseName,
                CustomerId = wroRequest.CustomerId,
                CustomerName = customerName,
                ReceiverName = wroRequest.ReceiverName ?? string.Empty,
                ReceiverPhone = wroRequest.ReceiverPhone ?? string.Empty,
                DeliveryAddress = wroRequest.DeliveryAddress ?? string.Empty,
                Status = wroRequest.Status ?? string.Empty,
                ShelfCode = wroRequest.ShelfCode,
                Note = null, // Cột ghi chú xử lý chưa có trong DB, trả về null theo spec
                CreatedAt = wroRequest.CreatedAt ?? DateTime.UtcNow,
                TotalQuantity = totalQuantity,
                Items = itemsDetail
            };
        }
    }
}
