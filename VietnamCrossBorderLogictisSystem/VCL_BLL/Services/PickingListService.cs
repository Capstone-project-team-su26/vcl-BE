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
    public class PickingListService : IPickingListService
    {
        private readonly IUnitOfWork _unitOfWork;

        public PickingListService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<ConfirmPickingResponseDto> ConfirmPickingListAsync(
            Guid pickingListId,
            Guid userId,
            string role,
            ConfirmPickingRequestDto request)
        {
            if (request == null)
            {
                throw new ArgumentException("Yêu cầu xác nhận picking không được để trống.");
            }

            // 1. Phân quyền: Chỉ Warehouse Staff, Staff hoặc Operations được thao tác
            var allowedRoles = new[] { "WarehouseStaff", "Staff", "OperationsManager", "Operations", "Admin" };
            if (!allowedRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận phiếu picking.");
            }

            // 2. Tìm phiếu picking
            var pickingListRepo = _unitOfWork.Repository<PickingList>();
            var pickingList = await pickingListRepo.GetByIdAsync(pickingListId);
            if (pickingList == null)
            {
                throw new NotFoundException($"Không tìm thấy phiếu picking với ID {pickingListId}");
            }

            // 3. Chỉ xác nhận được phiếu picking ở trạng thái PENDING
            if (!string.Equals(pickingList.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Chỉ có thể xác nhận phiếu picking ở trạng thái PENDING. Trạng thái hiện tại: {pickingList.Status}");
            }

            // 4. Lấy danh sách PickingItems liên kết
            var pickingItemRepo = _unitOfWork.Repository<PickingItem>();
            var allPickingItems = await pickingItemRepo.GetAllAsync();
            var relatedPickingItems = allPickingItems.Where(pi => pi.PickingListId == pickingListId).ToList();

            // 5. Kiểm tra danh sách Inventory IDs trong request có hợp lệ không
            var pickingInventoryIds = relatedPickingItems.Select(pi => pi.InventoryId).ToHashSet();
            foreach (var reqItem in request.Items)
            {
                if (!pickingInventoryIds.Contains(reqItem.InventoryId))
                {
                    throw new ArgumentException($"Tồn kho ID {reqItem.InventoryId} không thuộc danh sách hàng cần picking của phiếu này.");
                }
            }

            // 6. Kiểm tra số lượng picking không vượt quá số lượng đã RESERVED
            foreach (var item in relatedPickingItems)
            {
                var reqItem = request.Items.FirstOrDefault(ri => ri.InventoryId == item.InventoryId);
                var qPick = reqItem?.Quantity ?? 0;

                if (qPick < 0)
                {
                    throw new ArgumentException("Số lượng picking không hợp lệ (không được âm).");
                }

                if (qPick > item.Quantity)
                {
                    throw new ArgumentException($"Số lượng picking ({qPick}) vượt quá số lượng đã RESERVED ({item.Quantity}) cho tồn kho ID {item.InventoryId}.");
                }
            }

            // 7. Cập nhật trạng thái Inventory và các bảng liên quan
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();

            var wroItemRepo = _unitOfWork.Repository<WroItem>();
            var allWroItems = await wroItemRepo.GetAllAsync();
            var relatedWroItems = allWroItems.Where(wi => wi.WroRequestId == pickingList.WroRequestId).ToList();

            var binRepo = _unitOfWork.Repository<Bin>();
            var shelfRepo = _unitOfWork.Repository<Shelf>();
            var allBins = await binRepo.GetAllAsync();
            var allShelves = await shelfRepo.GetAllAsync();

            var confirmedItemsResponse = new List<ConfirmPickingItemResponseDto>();

            foreach (var item in relatedPickingItems)
            {
                var reqItem = request.Items.FirstOrDefault(ri => ri.InventoryId == item.InventoryId);
                var qPick = reqItem?.Quantity ?? 0;
                var qReserved = item.Quantity;

                var reservedInventory = await inventoryRepo.GetByIdAsync(item.InventoryId);
                var wroItem = relatedWroItems.FirstOrDefault(wi => wi.InventoryId == item.InventoryId);

                if (qPick == 0)
                {
                    // Trả lại toàn bộ lượng reserved về AVAILABLE
                    await ReleaseInventoryToAvailableAsync(reservedInventory, qReserved, inventoryRepo, allInventories);

                    // Xóa liên kết picking item, wro item và reserved inventory
                    pickingItemRepo.Delete(item);
                    if (wroItem != null)
                    {
                        wroItemRepo.Delete(wroItem);
                    }
                    if (reservedInventory != null)
                    {
                        inventoryRepo.Delete(reservedInventory);
                    }
                }
                else if (qPick < qReserved)
                {
                    // Lấy bớt một phần, trả phần dư về AVAILABLE
                    var qRemain = qReserved - qPick;
                    await ReleaseInventoryToAvailableAsync(reservedInventory, qRemain, inventoryRepo, allInventories);

                    // Cập nhật số lượng và trạng thái phần được lấy
                    if (reservedInventory != null)
                    {
                        reservedInventory.Quantity = qPick;
                        reservedInventory.Status = "PICKED";
                        inventoryRepo.Update(reservedInventory);
                    }

                    item.Quantity = qPick;
                    pickingItemRepo.Update(item);

                    if (wroItem != null)
                    {
                        wroItem.Quantity = qPick;
                        wroItemRepo.Update(wroItem);
                    }

                    // Thêm vào danh sách response
                    if (reservedInventory != null)
                    {
                        var binDetails = GetBinAndShelfDetails(reservedInventory.BinId, allBins, allShelves);
                        confirmedItemsResponse.Add(new ConfirmPickingItemResponseDto
                        {
                            InventoryId = reservedInventory.Id,
                            Quantity = qPick,
                            BinId = reservedInventory.BinId,
                            BinCode = binDetails.BinCode,
                            ShelfCode = binDetails.ShelfCode
                        });
                    }
                }
                else // qPick == qReserved
                {
                    // Cập nhật trạng thái thành PICKED
                    if (reservedInventory != null)
                    {
                        reservedInventory.Status = "PICKED";
                        inventoryRepo.Update(reservedInventory);
                    }

                    // Thêm vào danh sách response
                    if (reservedInventory != null)
                    {
                        var binDetails = GetBinAndShelfDetails(reservedInventory.BinId, allBins, allShelves);
                        confirmedItemsResponse.Add(new ConfirmPickingItemResponseDto
                        {
                            InventoryId = reservedInventory.Id,
                            Quantity = qPick,
                            BinId = reservedInventory.BinId,
                            BinCode = binDetails.BinCode,
                            ShelfCode = binDetails.ShelfCode
                        });
                    }
                }
            }

            // 8. Cập nhật phiếu picking
            pickingList.Status = "PICKED";
            pickingList.ConfirmedBy = userId;
            pickingList.ConfirmedAt = DateTime.UtcNow;
            pickingListRepo.Update(pickingList);

            // 9. Cập nhật trạng thái yêu cầu xuất kho sang PACKING
            var wroRequestRepo = _unitOfWork.Repository<WroRequest>();
            var wroRequest = await wroRequestRepo.GetByIdAsync(pickingList.WroRequestId);
            if (wroRequest != null)
            {
                wroRequest.Status = "PACKING";
                wroRequestRepo.Update(wroRequest);
            }

            // 10. Lưu các thay đổi
            await _unitOfWork.SaveChangesAsync();

            // Lấy thông tin người xác nhận để trả về
            var userRepo = _unitOfWork.Repository<User>();
            var user = await userRepo.GetByIdAsync(userId);
            var confirmedByName = user?.FullName ?? string.Empty;

            return new ConfirmPickingResponseDto
            {
                PickingListId = pickingList.Id,
                WroRequestId = pickingList.WroRequestId,
                Status = pickingList.Status,
                ConfirmedBy = pickingList.ConfirmedBy,
                ConfirmedByName = confirmedByName,
                ConfirmedAt = pickingList.ConfirmedAt,
                Items = confirmedItemsResponse
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
                inv.Id != reservedInventory.Id &&
                string.Equals(inv.Status, "AVAILABLE", StringComparison.OrdinalIgnoreCase)
            );

            if (originalInventory != null)
            {
                originalInventory.Quantity += quantityToRelease;
                inventoryRepo.Update(originalInventory);
            }
            else
            {
                if (quantityToRelease == reservedInventory.Quantity)
                {
                    reservedInventory.Status = "AVAILABLE";
                    inventoryRepo.Update(reservedInventory);
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

        private (string BinCode, string ShelfCode) GetBinAndShelfDetails(
            Guid binId,
            IEnumerable<Bin> allBins,
            IEnumerable<Shelf> allShelves)
        {
            string binCode = string.Empty;
            string shelfCode = string.Empty;

            if (binId != Guid.Empty)
            {
                var bin = allBins.FirstOrDefault(b => b.Id == binId);
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

            return (binCode, shelfCode);
        }
    }
}
