using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly IUnitOfWork _unitOfWork;

        public InventoryService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<InventoryResponseDto> RecordInventoryFromParcelAsync(Guid parcelId)
        {
            // 1. Tìm Parcel theo parcelId
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(parcelId);
            if (parcel == null)
            {
                throw new NotFoundException($"Không tìm thấy kiện hàng với ID: {parcelId}");
            }

            // 2. Không cho tạo tồn kho nếu Parcel chưa được put-away
            // Put-away thành công khi PackageStatus == "STORED" và có BinId
            if (!string.Equals(parcel.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase) || !parcel.BinId.HasValue)
            {
                throw new ArgumentException("Kiện hàng chưa được nhập vị trí kho (chưa put-away).");
            }

            // 3. Nếu kiện hàng đã được ghi nhận tồn kho trước đó -> trả về bản ghi hiện có (idempotent)
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var existingInventory = allInventories.FirstOrDefault(i => i.ParcelId == parcelId);
            if (existingInventory != null)
            {
                var existingDtos = await BuildInventoryDtosAsync(new List<Inventory> { existingInventory });
                return existingDtos.First();
            }

            // 4. Kiểm tra yêu cầu ký gửi liên kết
            if (!parcel.ConsignmentId.HasValue)
            {
                throw new ArgumentException("Kiện hàng không liên kết với yêu cầu ký gửi nào.");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(parcel.ConsignmentId.Value);
            if (order == null)
            {
                throw new NotFoundException("Không tìm thấy thông tin yêu cầu ký gửi liên kết với kiện hàng.");
            }

            if (!order.CustomerId.HasValue)
            {
                throw new ArgumentException("Yêu cầu ký gửi không liên kết với khách hàng nào.");
            }

            // 5. Tìm OrderItem và tổng số lượng hàng trong Parcel
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var consignmentItems = allItems.Where(i => i.OrderId == order.Id).ToList();

            Guid? orderItemId = consignmentItems.FirstOrDefault()?.Id;
            int quantity = consignmentItems.Sum(item => item.Quantity ?? 1);
            if (quantity <= 0)
            {
                quantity = 1;
            }

            // 6. Ghi nhận bản ghi tồn kho mới
            var inventory = new Inventory
            {
                Id = Guid.NewGuid(),
                CustomerId = order.CustomerId.Value,
                OrderId = order.Id,
                OrderItemId = orderItemId,
                ParcelId = parcel.Id,
                BinId = parcel.BinId.Value,
                Quantity = quantity,
                Status = "AVAILABLE",
                CreatedAt = DateTime.UtcNow
            };

            await inventoryRepo.AddAsync(inventory);
            await _unitOfWork.SaveChangesAsync();

            // 7. Trả kết quả kèm thông tin khách hàng, kiện hàng và vị trí lưu kho
            var createdDtos = await BuildInventoryDtosAsync(new List<Inventory> { inventory });
            return createdDtos.First();
        }

        public async Task<List<InventoryResponseDto>> GetInventoriesAsync(string? status, Guid? customerId, Guid? warehouseId)
        {
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();

            var query = allInventories.AsQueryable();

            // Loại bỏ các bản ghi zombie AVAILABLE có Quantity <= 0
            query = query.Where(i => !(string.Equals(i.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase) && i.Quantity <= 0));

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(i => string.Equals(i.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            if (customerId.HasValue)
            {
                query = query.Where(i => i.CustomerId == customerId.Value);
            }

            var dtos = await BuildInventoryDtosAsync(query.ToList());

            // Lọc theo kho sau khi đã xác định được kho từ ô kệ của từng bản ghi tồn kho
            if (warehouseId.HasValue)
            {
                dtos = dtos.Where(d => d.WarehouseId == warehouseId.Value).ToList();
            }

            return dtos;
        }

        /// <summary>
        /// Bổ sung thông tin khách hàng, yêu cầu ký gửi, kiện hàng và vị trí lưu kho cho danh sách tồn kho.
        /// Các bảng liên quan được nạp một lần rồi tra cứu theo Id để tránh truy vấn lặp trên từng dòng.
        /// </summary>
        private async Task<List<InventoryResponseDto>> BuildInventoryDtosAsync(List<Inventory> inventories)
        {
            if (inventories == null || inventories.Count == 0)
            {
                return new List<InventoryResponseDto>();
            }

            var customerIds = inventories.Select(i => i.CustomerId).ToHashSet();
            var orderIds = inventories.Select(i => i.OrderId).ToHashSet();
            var parcelIds = inventories.Select(i => i.ParcelId).ToHashSet();
            var binIds = inventories.Select(i => i.BinId).ToHashSet();

            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .Where(c => customerIds.Contains(c.Id))
                .ToDictionary(c => c.Id);

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id))
                .ToDictionary(o => o.Id);

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToDictionary(p => p.Id);

            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => binIds.Contains(b.Id))
                .ToDictionary(b => b.Id);

            var warehouseIds = bins.Values
                .Where(b => b.WarehouseId.HasValue)
                .Select(b => b.WarehouseId.Value)
                .ToHashSet();

            var warehouses = warehouseIds.Count == 0
                ? new Dictionary<Guid, Warehouse>()
                : (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                    .Where(w => warehouseIds.Contains(w.Id))
                    .ToDictionary(w => w.Id);

            var result = new List<InventoryResponseDto>();

            foreach (var inventory in inventories)
            {
                customers.TryGetValue(inventory.CustomerId, out var customer);
                orders.TryGetValue(inventory.OrderId, out var order);
                parcels.TryGetValue(inventory.ParcelId, out var parcel);
                bins.TryGetValue(inventory.BinId, out var bin);

                Warehouse warehouse = null;
                if (bin?.WarehouseId != null)
                {
                    warehouses.TryGetValue(bin.WarehouseId.Value, out warehouse);
                }

                // Mốc bắt đầu lưu kho ưu tiên thời điểm put-away của kiện hàng
                var storedAt = parcel?.PutAwayTime ?? inventory.CreatedAt;

                // Thể tích thực tế tính theo cùng công thức với kiểm tra sức chứa ô kệ
                var actualVolume = (parcel?.Length ?? 0) * (parcel?.Width ?? 0) * (parcel?.Height ?? 0);

                result.Add(new InventoryResponseDto
                {
                    InventoryId = inventory.Id,
                    CustomerId = inventory.CustomerId,
                    OrderId = inventory.OrderId,
                    OrderItemId = inventory.OrderItemId,
                    ParcelId = inventory.ParcelId,
                    BinId = inventory.BinId,
                    Quantity = inventory.Quantity,
                    Status = inventory.Status ?? string.Empty,
                    CreatedAt = inventory.CreatedAt,

                    CustomerCode = customer?.CustomerCode ?? string.Empty,
                    CustomerName = customer?.FullName ?? string.Empty,
                    CustomerPhone = customer?.Phone ?? string.Empty,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    PackageCode = parcel?.PackageCode ?? string.Empty,
                    BinCode = bin?.BinCode ?? string.Empty,
                    WarehouseId = bin?.WarehouseId,
                    WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                    StoredAt = storedAt,
                    ActualWeight = parcel?.ActualWeight ?? 0,
                    ActualVolume = actualVolume
                });
            }

            return result;
        }
    }
}
