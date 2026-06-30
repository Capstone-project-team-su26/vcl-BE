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
                Note = wroRequest.RejectionReason,
                CreatedAt = wroRequest.CreatedAt ?? DateTime.UtcNow,
                TotalQuantity = totalQuantity,
                Items = itemsDetail
            };
        }

        public async Task<UpdateWroStatusResponseDto> UpdateWroStatusAsync(Guid requestId, Guid userId, string role, UpdateWroStatusRequestDto request)
        {
            // 1. Phân quyền: Chỉ Staff hoặc Operations được phép duyệt/từ chối
            bool isCustomer = string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase);
            if (isCustomer)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền cập nhật trạng thái yêu cầu xuất kho.");
            }

            var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRequestRepo.GetByIdAsync(requestId);
            if (wroRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {requestId}");
            }

            // 2. Validate trạng thái mới
            var targetStatus = request.Status.Trim().ToUpperInvariant();
            var validStatuses = new[] { "APPROVED", "REJECTED" };
            if (!validStatuses.Contains(targetStatus))
            {
                throw new ArgumentException("Trạng thái mới không hợp lệ. Các trạng thái hợp lệ: APPROVED, REJECTED.");
            }

            // 3. Kiểm tra trạng thái hiện tại (Không cho cập nhật nếu không ở trạng thái PENDING_REVIEW)
            if (!string.Equals(wroRequest.Status, "PENDING_REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Không thể cập nhật trạng thái yêu cầu xuất kho do yêu cầu đã ở trạng thái {wroRequest.Status}");
            }

            // 4. Xử lý lưu lý do từ chối nếu REJECTED
            if (targetStatus == "REJECTED")
            {
                if (string.IsNullOrWhiteSpace(request.RejectionReason))
                {
                    throw new ArgumentException("Vui lòng cung cấp lý do từ chối (RejectionReason).");
                }
                wroRequest.RejectionReason = request.RejectionReason;
            }

            wroRequest.Status = targetStatus;
            wroRequestRepo.Update(wroRequest);

            // 5. Nếu REJECTED -> Hoàn lại số lượng tồn kho khả dụng từ RESERVED về AVAILABLE
            if (targetStatus == "REJECTED")
            {
                var wroItemRepo = _unitOfWork.Repository<WroItem>();
                var allWroItems = await wroItemRepo.GetAllAsync();
                var relatedWroItems = allWroItems.Where(i => i.WroRequestId == requestId).ToList();

                var inventoryRepo = _unitOfWork.Repository<Inventory>();
                var allInventories = await inventoryRepo.GetAllAsync();

                foreach (var item in relatedWroItems)
                {
                    var reservedInventory = await inventoryRepo.GetByIdAsync(item.InventoryId);
                    if (reservedInventory != null)
                    {
                        // Tìm tồn kho gốc dạng AVAILABLE để cộng gộp lại số lượng
                        var originalInventory = allInventories.FirstOrDefault(inv =>
                            inv.CustomerId == reservedInventory.CustomerId &&
                            inv.OrderId == reservedInventory.OrderId &&
                            inv.OrderItemId == reservedInventory.OrderItemId &&
                            inv.ParcelId == reservedInventory.ParcelId &&
                            inv.BinId == reservedInventory.BinId &&
                            string.Equals(inv.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase)
                        );

                        if (originalInventory != null)
                        {
                            originalInventory.Quantity += item.Quantity;
                            inventoryRepo.Update(originalInventory);

                            // Xóa WroItem liên kết trước để tránh vi phạm FK, sau đó xóa reserved inventory
                            wroItemRepo.Delete(item);
                            inventoryRepo.Delete(reservedInventory);
                        }
                        else
                        {
                            // Nếu không tìm thấy record AVAILABLE cũ (có thể đã bị xóa khi qty về 0), đổi trạng thái reserved thành AVAILABLE
                            reservedInventory.Status = "AVAILABLE";
                            inventoryRepo.Update(reservedInventory);
                        }
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return new UpdateWroStatusResponseDto
            {
                WroId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                Status = wroRequest.Status
            };
        }

        public async Task<PickingListResponseDto> CreatePickingListAsync(Guid requestId, Guid userId, string role)
        {
            // 1. Phân quyền: Chỉ Staff hoặc Operations được phép tạo phiếu picking
            bool isCustomer = string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase);
            if (isCustomer)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền tạo phiếu picking.");
            }

            var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRequestRepo.GetByIdAsync(requestId);
            if (wroRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {requestId}");
            }

            // 2. Kiểm tra trạng thái yêu cầu xuất kho (Phải là APPROVED)
            if (!string.Equals(wroRequest.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Chỉ có thể tạo phiếu picking cho yêu cầu xuất kho đã được duyệt (APPROVED).");
            }

            // 3. Kiểm tra xem phiếu picking cho yêu cầu này đã tồn tại chưa
            var pickingListRepo = _unitOfWork.Repository<PickingList>();
            var allPickingLists = await pickingListRepo.GetAllAsync();
            var existingPickingList = allPickingLists.FirstOrDefault(p => p.WroRequestId == requestId);
            if (existingPickingList != null)
            {
                throw new ArgumentException("Phiếu picking cho yêu cầu xuất kho này đã tồn tại.");
            }

            // 4. Tạo phiếu PickingList mới
            var pickingList = new PickingList
            {
                Id = Guid.NewGuid(),
                WroRequestId = requestId,
                Status = "PENDING",
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            await pickingListRepo.AddAsync(pickingList);

            // 5. Lấy danh sách WroItems của yêu cầu xuất kho
            var wroItemRepo = _unitOfWork.Repository<WroItem>();
            var allWroItems = await wroItemRepo.GetAllAsync();
            var relatedWroItems = allWroItems.Where(i => i.WroRequestId == requestId).ToList();

            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var pickingItemRepo = _unitOfWork.Repository<PickingItem>();

            foreach (var item in relatedWroItems)
            {
                var inventory = await inventoryRepo.GetByIdAsync(item.InventoryId);
                if (inventory == null || !string.Equals(inventory.Status, "RESERVED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Tồn kho liên kết {item.InventoryId} không ở trạng thái RESERVED.");
                }

                // Tạo từng PickingItem liên kết
                var pickingItem = new PickingItem
                {
                    Id = Guid.NewGuid(),
                    PickingListId = pickingList.Id,
                    InventoryId = item.InventoryId,
                    Quantity = item.Quantity
                };
                await pickingItemRepo.AddAsync(pickingItem);
            }

            // 6. Cập nhật trạng thái yêu cầu xuất kho sang PICKING
            wroRequest.Status = "PICKING";
            wroRequestRepo.Update(wroRequest);

            await _unitOfWork.SaveChangesAsync();

            // 7. Truy vấn tên người tạo và khách hàng sở hữu hàng
            var userRepo = _unitOfWork.Repository<User>();
            var user = await userRepo.GetByIdAsync(userId);
            var createdByName = user?.FullName ?? string.Empty;

            var customerRepo = _unitOfWork.Repository<Customer>();
            string customerName = string.Empty;
            if (wroRequest.CustomerId.HasValue)
            {
                var customer = await customerRepo.GetByIdAsync(wroRequest.CustomerId.Value);
                customerName = customer?.FullName ?? string.Empty;
            }

            // 8. Lấy chi tiết vị trí kho, mã kiện hàng, tên sản phẩm để trả về DTO
            var itemsResponse = new List<PickingItemResponseDto>();
            var binRepo = _unitOfWork.Repository<Bin>();
            var shelfRepo = _unitOfWork.Repository<Shelf>();
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();

            var allBins = await binRepo.GetAllAsync();
            var allShelves = await shelfRepo.GetAllAsync();
            var allParcels = await parcelRepo.GetAllAsync();
            var allOrderItems = await orderItemRepo.GetAllAsync();

            foreach (var item in relatedWroItems)
            {
                var inventory = await inventoryRepo.GetByIdAsync(item.InventoryId);
                string binCode = string.Empty;
                string shelfCode = string.Empty;
                string packageCode = string.Empty;
                string productName = string.Empty;

                if (inventory != null)
                {
                    if (inventory.BinId != Guid.Empty)
                    {
                        var bin = allBins.FirstOrDefault(b => b.Id == inventory.BinId);
                        if (bin != null)
                        {
                            binCode = bin.BinCode ?? string.Empty;
                            if (bin.ShelfId.HasValue)
                            {
                                var shelf = allShelves.FirstOrDefault(s => s.Id == bin.ShelfId.Value);
                                shelfCode = shelf?.ShelfCode ?? string.Empty;
                            }
                        }
                    }

                    if (inventory.ParcelId != Guid.Empty)
                    {
                        var parcel = allParcels.FirstOrDefault(p => p.Id == inventory.ParcelId);
                        if (parcel != null)
                        {
                            packageCode = parcel.PackageCode ?? string.Empty;
                        }
                    }

                    if (inventory.OrderItemId.HasValue)
                    {
                        var orderItem = allOrderItems.FirstOrDefault(oi => oi.Id == inventory.OrderItemId.Value);
                        if (orderItem != null)
                        {
                            productName = orderItem.ProductName ?? string.Empty;
                        }
                    }
                }

                itemsResponse.Add(new PickingItemResponseDto
                {
                    ItemId = item.Id,
                    InventoryId = item.InventoryId,
                    PackageCode = packageCode,
                    ProductName = productName,
                    BinCode = binCode,
                    ShelfCode = shelfCode,
                    Quantity = item.Quantity
                });
            }

            return new PickingListResponseDto
            {
                PickingListId = pickingList.Id,
                WroRequestId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                CustomerId = wroRequest.CustomerId,
                CustomerName = customerName,
                Status = pickingList.Status,
                CreatedBy = pickingList.CreatedBy,
                CreatedByName = createdByName,
                CreatedAt = pickingList.CreatedAt,
                Items = itemsResponse
            };
        }
    }
}
