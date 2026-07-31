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

                if (!string.Equals(inventory.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase) || inventory.Quantity <= 0)
                {
                    throw new ArgumentException($"Hàng tồn kho với ID {item.InventoryId} không khả dụng để xuất (Trạng thái hiện tại: {inventory.Status}, Số lượng: {inventory.Quantity}).");
                }

                if (inventory.Quantity < item.Quantity)
                {
                    throw new ArgumentException($"Số lượng xuất ({item.Quantity}) vượt quá số lượng tồn kho khả dụng ({inventory.Quantity}) của mặt hàng.");
                }
            }

            // 3. Khởi tạo WRO Request
            Guid? targetCustomerId = customer?.Id;
            if (!targetCustomerId.HasValue && request.Items.Any())
            {
                var firstInv = await inventoryRepo.GetByIdAsync(request.Items.First().InventoryId);
                if (firstInv != null)
                {
                    targetCustomerId = firstInv.CustomerId;
                }
            }

            var wroCode = $"WRO-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";
            var wroRequest = new WroRequest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CustomerId = targetCustomerId,
                WroCode = wroCode,
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                DeliveryAddress = request.DeliveryAddress,
                ShelfCode = request.ShelfCode,
                Status = "RELEASE_PENDING",
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
                Guid targetInventoryId;

                if (inventory!.Quantity == item.Quantity)
                {
                    // Chuyển trực tiếp bản ghi hiện tại sang RESERVED (tránh tạo dòng AVAILABLE quantity = 0 zombie)
                    inventory.Status = "RESERVED";
                    inventoryRepo.Update(inventory);
                    targetInventoryId = inventory.Id;
                }
                else
                {
                    // Trừ số lượng khỏi bản ghi AVAILABLE hiện tại (vẫn còn quantity > 0)
                    inventory.Quantity -= item.Quantity;
                    inventoryRepo.Update(inventory);

                    // Tạo bản ghi RESERVED riêng cho phần xuất
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
                    targetInventoryId = reservedInventory.Id;
                }

                // C. Tạo WroItem liên kết
                var wroItem = new WroItem
                {
                    Id = Guid.NewGuid(),
                    WroRequestId = wroRequest.Id,
                    InventoryId = targetInventoryId,
                    Quantity = item.Quantity
                };
                await wroItemRepo.AddAsync(wroItem);

                // Lấy thông tin mã kiện hàng để trả về response
                var parcel = await parcelRepo.GetByIdAsync(inventory.ParcelId);
                var packageCode = parcel?.PackageCode ?? string.Empty;

                resultItems.Add(new WroItemResponseDto
                {
                    ItemId = wroItem.Id,
                    InventoryId = targetInventoryId,
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
                bool isOwner = (customer != null && wroRequest.CustomerId == customer.Id) || (wroRequest.UserId == userId);
                if (!isOwner)
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
            var validStatuses = new[] { "RELEASE_APPROVED", "RELEASE_REJECTED" };
            if (!validStatuses.Contains(targetStatus))
            {
                throw new ArgumentException("Trạng thái mới không hợp lệ. Các trạng thái hợp lệ: RELEASE_APPROVED, RELEASE_REJECTED.");
            }

            // 3. Kiểm tra trạng thái hiện tại (Không cho cập nhật nếu không ở trạng thái RELEASE_PENDING)
            if (!string.Equals(wroRequest.Status, "RELEASE_PENDING", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Không thể cập nhật trạng thái yêu cầu xuất kho do yêu cầu đã ở trạng thái {wroRequest.Status}");
            }

            // 4. Xử lý lưu lý do từ chối nếu RELEASE_REJECTED
            if (targetStatus == "RELEASE_REJECTED")
            {
                if (string.IsNullOrWhiteSpace(request.RejectionReason))
                {
                    throw new ArgumentException("Vui lòng cung cấp lý do từ chối (RejectionReason).");
                }
                wroRequest.RejectionReason = request.RejectionReason;
            }

            wroRequest.Status = targetStatus;
            wroRequestRepo.Update(wroRequest);

            // 5. Nếu RELEASE_REJECTED -> Hoàn lại số lượng tồn kho khả dụng từ RESERVED về AVAILABLE
            if (targetStatus == "RELEASE_REJECTED")
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
                            inv.Id != reservedInventory.Id &&
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
                            // Nếu không tìm thấy record AVAILABLE khác (do trước đó chuyển toàn bộ record sang RESERVED), chuyển lại trạng thái thành AVAILABLE
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

            // 2. Kiểm tra trạng thái yêu cầu xuất kho (Phải là RELEASE_APPROVED)
            if (!string.Equals(wroRequest.Status, "RELEASE_APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Chỉ có thể tạo phiếu picking cho yêu cầu xuất kho đã được duyệt (RELEASE_APPROVED).");
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

        public async Task<CompleteWroResponseDto> CompleteWroRequestAsync(
            Guid requestId,
            Guid userId,
            string role,
            CompleteWroRequestDto request)
        {
            if (request == null)
            {
                throw new ArgumentException("Yêu cầu hoàn tất xuất kho không được để trống.");
            }

            // 1. Phân quyền: Chỉ Warehouse Staff, Staff hoặc Operations được thao tác
            var allowedRoles = new[] { "WarehouseStaff", "Staff", "OperationsManager", "Operations", "Admin" };
            if (!allowedRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Bạn không có quyền hoàn tất xuất kho.");
            }

            // 2. Tìm yêu cầu xuất kho
            var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRequestRepo.GetByIdAsync(requestId);
            if (wroRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {requestId}");
            }

            // 3. Chỉ hoàn tất xuất kho với yêu cầu đã PICKED hoặc PACKING
            if (string.Equals(wroRequest.Status, "RELEASED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Yêu cầu xuất kho này đã hoàn tất xuất kho trước đó.");
            }

            var validStatuses = new[] { "PICKED", "PACKING" };
            if (!validStatuses.Any(s => string.Equals(s, wroRequest.Status, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Yêu cầu xuất kho chưa đủ điều kiện xuất kho. Trạng thái hiện tại: {wroRequest.Status}");
            }

            // 4. Lấy phiếu picking để xác nhận có phiếu và liên kết
            var pickingListRepo = _unitOfWork.Repository<PickingList>();
            var allPickingLists = await pickingListRepo.GetAllAsync();
            var pickingList = allPickingLists.FirstOrDefault(pl => pl.WroRequestId == requestId);
            if (pickingList == null)
            {
                throw new NotFoundException($"Không tìm thấy phiếu picking liên kết với yêu cầu xuất kho có ID {requestId}.");
            }

            if (!string.Equals(pickingList.Status, "PICKED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Yêu cầu xuất kho chưa được xác nhận picking (Phiếu picking chưa ở trạng thái PICKED). Trạng thái hiện tại: {pickingList.Status}");
            }

            // 5. Lấy danh sách hàng đã picking (RESERVED hoặc PICKED)
            var pickingItemRepo = _unitOfWork.Repository<PickingItem>();
            var allPickingItems = await pickingItemRepo.GetAllAsync();
            var relatedPickingItems = allPickingItems.Where(pi => pi.PickingListId == pickingList.Id).ToList();

            var wroItemRepo = _unitOfWork.Repository<WroItem>();
            var allWroItems = await wroItemRepo.GetAllAsync();
            var relatedWroItems = allWroItems.Where(wi => wi.WroRequestId == requestId).ToList();

            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();

            var binRepo = _unitOfWork.Repository<Bin>();
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var allBins = await binRepo.GetAllAsync();
            var allWarehouses = await warehouseRepo.GetAllAsync();

            // 6. Kiểm tra các InventoryId gửi lên có hợp lệ không
            var pickedInventoryIds = relatedPickingItems.Select(pi => pi.InventoryId).ToHashSet();
            foreach (var reqItem in request.Items)
            {
                if (!pickedInventoryIds.Contains(reqItem.InventoryId))
                {
                    throw new ArgumentException($"Tồn kho ID {reqItem.InventoryId} không nằm trong danh sách đã picking của yêu cầu xuất kho này.");
                }
            }

            // 7. Kiểm tra số lượng đóng gói không vượt quá số lượng đã picking (RESERVED/PICKED)
            foreach (var item in relatedPickingItems)
            {
                var reqItem = request.Items.FirstOrDefault(ri => ri.InventoryId == item.InventoryId);
                var qShipped = reqItem?.Quantity ?? 0;

                if (qShipped < 0)
                {
                    throw new ArgumentException("Số lượng xuất kho không được nhỏ hơn 0.");
                }

                if (qShipped > item.Quantity)
                {
                    throw new ArgumentException($"Số lượng xuất ({qShipped}) vượt quá số lượng đã picking ({item.Quantity}) cho tồn kho ID {item.InventoryId}.");
                }
            }

            // 8. Cập nhật Inventory và các liên kết
            var confirmedItemsResponse = new List<CompleteWroItemResponseDto>();
            Guid? warehouseId = null;
            string warehouseName = string.Empty;

            foreach (var item in relatedPickingItems)
            {
                var reqItem = request.Items.FirstOrDefault(ri => ri.InventoryId == item.InventoryId);
                var qShipped = reqItem?.Quantity ?? 0;
                var qPicked = item.Quantity;

                var inventoryRecord = await inventoryRepo.GetByIdAsync(item.InventoryId);
                var wroItem = relatedWroItems.FirstOrDefault(wi => wi.InventoryId == item.InventoryId);

                // Xác định thông tin Kho xuất hàng từ Bin của inventory đầu tiên có hợp lệ
                if (inventoryRecord != null && !warehouseId.HasValue)
                {
                    var bin = allBins.FirstOrDefault(b => b.Id == inventoryRecord.BinId);
                    if (bin != null && bin.WarehouseId.HasValue)
                    {
                        warehouseId = bin.WarehouseId.Value;
                        var wh = allWarehouses.FirstOrDefault(w => w.Id == warehouseId.Value);
                        warehouseName = wh?.WarehouseName ?? string.Empty;
                    }
                }

                if (qShipped == 0)
                {
                    // Trả lại toàn bộ lượng picked về AVAILABLE
                    await ReleaseInventoryToAvailableAsync(inventoryRecord, qPicked, inventoryRepo, allInventories);

                    // Xóa liên kết
                    pickingItemRepo.Delete(item);
                    if (wroItem != null)
                    {
                        wroItemRepo.Delete(wroItem);
                    }
                    if (inventoryRecord != null)
                    {
                        inventoryRepo.Delete(inventoryRecord);
                    }
                }
                else if (qShipped < qPicked)
                {
                    // Trả phần dư về AVAILABLE
                    var qRemain = qPicked - qShipped;
                    await ReleaseInventoryToAvailableAsync(inventoryRecord, qRemain, inventoryRepo, allInventories);

                    // Cập nhật số lượng và trạng thái phần xuất
                    if (inventoryRecord != null)
                    {
                        inventoryRecord.Quantity = qShipped;
                        inventoryRecord.Status = "RELEASED";
                        inventoryRepo.Update(inventoryRecord);
                    }

                    item.Quantity = qShipped;
                    pickingItemRepo.Update(item);

                    if (wroItem != null)
                    {
                        wroItem.Quantity = qShipped;
                        wroItemRepo.Update(wroItem);
                    }

                    confirmedItemsResponse.Add(new CompleteWroItemResponseDto
                    {
                        InventoryId = item.InventoryId,
                        Quantity = qShipped,
                        Status = "RELEASED"
                    });
                }
                else // qShipped == qPicked
                {
                    if (inventoryRecord != null)
                    {
                        inventoryRecord.Status = "RELEASED";
                        inventoryRepo.Update(inventoryRecord);
                    }

                    confirmedItemsResponse.Add(new CompleteWroItemResponseDto
                    {
                        InventoryId = item.InventoryId,
                        Quantity = qShipped,
                        Status = "RELEASED"
                    });
                }
            }

            // 9. Cập nhật yêu cầu xuất kho thành RELEASED
            wroRequest.Status = "RELEASED";
            wroRequest.ReleasedBy = userId;
            wroRequest.ReleasedAt = DateTime.UtcNow;
            wroRequestRepo.Update(wroRequest);

            // 10. Lưu các thay đổi vào DB
            await _unitOfWork.SaveChangesAsync();

            // Lấy thông tin người xác nhận
            var userRepo = _unitOfWork.Repository<User>();
            var user = await userRepo.GetByIdAsync(userId);
            var releasedByName = user?.FullName ?? string.Empty;

            return new CompleteWroResponseDto
            {
                WroRequestId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                Status = wroRequest.Status,
                ReleasedBy = wroRequest.ReleasedBy,
                ReleasedByName = releasedByName,
                ReleasedAt = wroRequest.ReleasedAt,
                WarehouseId = warehouseId,
                WarehouseName = warehouseName,
                Items = confirmedItemsResponse
            };
        }

        public async Task<PagedWroListResponseDto> GetWroRequestsAsync(Guid userId, string role, GetWroListRequestDto request)
        {
            var wroRepo = _unitOfWork.Repository<WroRequest>();
            var allWros = await wroRepo.GetAllAsync();
            var query = allWros.AsQueryable();

            bool isCustomer = string.Equals(role, "Customer", StringComparison.OrdinalIgnoreCase);
            if (isCustomer)
            {
                var customerRepo = _unitOfWork.Repository<Customer>();
                var allCustomers = await customerRepo.GetAllAsync();
                var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer != null)
                {
                    query = query.Where(w => w.CustomerId == customer.Id || w.UserId == userId);
                }
                else
                {
                    query = query.Where(w => w.UserId == userId);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                query = query.Where(w => string.Equals(w.Status, request.Status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                var term = request.SearchTerm.Trim();
                query = query.Where(w => (w.WroCode != null && w.WroCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                                         (w.ReceiverName != null && w.ReceiverName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                                         (w.ReceiverPhone != null && w.ReceiverPhone.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            int totalCount = query.Count();
            int pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
            int pageSize = request.PageSize < 1 ? 10 : request.PageSize;

            var pagedItems = query.OrderByDescending(w => w.CreatedAt)
                                  .Skip((pageIndex - 1) * pageSize)
                                  .Take(pageSize)
                                  .ToList();

            var itemsDto = new List<WroDetailResponseDto>();
            foreach (var item in pagedItems)
            {
                try
                {
                    var detail = await GetWroRequestDetailAsync(item.Id, userId, role);
                    itemsDto.Add(detail);
                }
                catch
                {
                    // Nếu gặp lỗi khi lấy chi tiết của 1 phần tử lẻ, bỏ qua phần tử đó để không làm hỏng cả danh sách
                }
            }

            return new PagedWroListResponseDto
            {
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
                Items = itemsDto
            };
        }

        public async Task<AssignShippingRouteResponseDto> AssignShippingRouteAsync(Guid requestId, Guid userId, string role, AssignShippingRouteRequestDto request)
        {
            var allowedRoles = new[] { "OperationsManager", "Operations", "Admin" };
            if (!allowedRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Chỉ Operations Manager hoặc Admin mới có quyền gán tuyến vận chuyển cho WRO.");
            }

            var wroRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRepo.GetByIdAsync(requestId);
            if (wroRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {requestId}");
            }

            string carrierName = string.Empty;
            if (request.CarrierId.HasValue && request.CarrierId.Value != Guid.Empty)
            {
                var carrierRepo = _unitOfWork.Repository<Carrier>();
                var carrier = await carrierRepo.GetByIdAsync(request.CarrierId.Value);
                if (carrier != null)
                {
                    carrierName = carrier.CarrierName ?? string.Empty;
                }
            }

            wroRepo.Update(wroRequest);
            await _unitOfWork.SaveChangesAsync();

            return new AssignShippingRouteResponseDto
            {
                WroId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                Status = wroRequest.Status,
                CarrierId = request.CarrierId,
                CarrierName = carrierName,
                ShippingRoute = request.ShippingRoute ?? string.Empty,
                Note = request.Note ?? string.Empty
            };
        }

        public async Task<UpdatePackingDetailsResponseDto> UpdatePackingDetailsAsync(Guid requestId, Guid userId, string role, UpdatePackingDetailsRequestDto request)
        {
            var allowedRoles = new[] { "WarehouseStaff", "Staff", "OperationsManager", "Operations", "Admin" };
            if (!allowedRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Bạn không có quyền cập nhật đóng gói kiện xuất kho.");
            }

            var wroRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRepo.GetByIdAsync(requestId);
            if (wroRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {requestId}");
            }

            wroRequest.Status = "PACKED";
            wroRepo.Update(wroRequest);
            await _unitOfWork.SaveChangesAsync();

            decimal volumeWeight = (request.Length * request.Width * request.Height) / 5000m;

            return new UpdatePackingDetailsResponseDto
            {
                WroId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                Status = wroRequest.Status,
                Weight = request.Weight,
                VolumeWeight = volumeWeight,
                PhotosCount = request.PackagingPhotos?.Count ?? 0,
                PackedAt = DateTime.UtcNow
            };
        }

        public async Task<ReleaseHandoverResponseDto> HandoverToCarrierAsync(Guid requestId, Guid userId, string role, ReleaseHandoverRequestDto request)
        {
            var allowedRoles = new[] { "WarehouseStaff", "Staff", "OperationsManager", "Operations", "Admin" };
            if (!allowedRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Bạn không có quyền thực hiện bàn giao kiện hàng cho đơn vị vận chuyển.");
            }

            var wroRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRepo.GetByIdAsync(requestId);
            if (wroRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {requestId}");
            }

            var carrierRepo = _unitOfWork.Repository<Carrier>();
            var carrier = await carrierRepo.GetByIdAsync(request.CarrierId);
            string carrierName = carrier?.CarrierName ?? "Đơn vị vận chuyển";

            wroRequest.Status = "HANDED_OVER";
            wroRepo.Update(wroRequest);
            await _unitOfWork.SaveChangesAsync();

            return new ReleaseHandoverResponseDto
            {
                WroId = wroRequest.Id,
                WroCode = wroRequest.WroCode,
                Status = wroRequest.Status,
                CarrierName = carrierName,
                TrackingNumber = request.TrackingNumber,
                HandoverAt = DateTime.UtcNow
            };
        }

        private async Task ReleaseInventoryToAvailableAsync(
            Inventory? reservedInventory,
            int quantityToRelease,
            IGenericRepository<Inventory> inventoryRepo,
            IEnumerable<Inventory> allInventories)
        {
            if (reservedInventory == null || quantityToRelease <= 0) return;

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
                originalInventory.Quantity += quantityToRelease;
                inventoryRepo.Update(originalInventory);
            }
            else
            {
                var newAvailable = new Inventory
                {
                    Id = Guid.NewGuid(),
                    CustomerId = reservedInventory.CustomerId,
                    OrderId = reservedInventory.OrderId,
                    OrderItemId = reservedInventory.OrderItemId,
                    ParcelId = reservedInventory.ParcelId,
                    BinId = reservedInventory.BinId,
                    Quantity = quantityToRelease,
                    Status = "AVAILABLE",
                    CreatedAt = DateTime.UtcNow
                };
                await inventoryRepo.AddAsync(newAvailable);
            }
        }
    }
}

