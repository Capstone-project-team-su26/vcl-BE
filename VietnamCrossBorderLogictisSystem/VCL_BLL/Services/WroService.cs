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
    }
}
