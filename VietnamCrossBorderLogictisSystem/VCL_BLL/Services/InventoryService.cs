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

            // 3. Không cho tạo trùng tồn kho cho cùng một Parcel
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var exists = allInventories.Any(i => i.ParcelId == parcelId);
            if (exists)
            {
                throw new ArgumentException("Kiện hàng đã được ghi nhận tồn kho trước đó.");
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

            // 7. Trả kết quả
            return new InventoryResponseDto
            {
                InventoryId = inventory.Id,
                CustomerId = inventory.CustomerId,
                OrderId = inventory.OrderId,
                OrderItemId = inventory.OrderItemId,
                ParcelId = inventory.ParcelId,
                BinId = inventory.BinId,
                Quantity = inventory.Quantity,
                Status = inventory.Status,
                CreatedAt = inventory.CreatedAt
            };
        }
    }
}
