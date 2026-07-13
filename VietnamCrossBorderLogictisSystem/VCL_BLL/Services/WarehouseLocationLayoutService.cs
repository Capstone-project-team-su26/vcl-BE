using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services;

public class WarehouseLocationLayoutService : IWarehouseLocationLayoutService
{
    private readonly IUnitOfWork _unitOfWork;

    public WarehouseLocationLayoutService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private void EnsureReadAccess(string userRole)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(userRole, "Operations", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(userRole, "Warehouse Staff", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(userRole, "Staff", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền truy cập sơ đồ kho.");
        }
    }

    private void EnsureWriteAccess(string userRole)
    {
        if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(userRole, "Operations", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Chỉ Operations hoặc Admin mới được phép thao tác cấu hình sơ đồ kho.");
        }
    }

    public async Task<List<WarehouseLocationLayoutDto>> GetLayoutByWarehouseIdAsync(Guid warehouseId, string userRole)
    {
        EnsureReadAccess(userRole);

        var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId);
        if (warehouse == null)
        {
            throw new NotFoundException($"Không tìm thấy kho hàng với ID: {warehouseId}");
        }

        var layoutRepo = _unitOfWork.Repository<WarehouseLocationLayout>();
        var allLayouts = await layoutRepo.GetAllAsync();
        var warehouseLayouts = allLayouts
            .Where(l => l.WarehouseId == warehouseId)
            .OrderBy(l => l.RowIndex)
            .ThenBy(l => l.ColumnIndex)
            .ToList();

        var zones = await _unitOfWork.Repository<WarehouseZone>().GetAllAsync();
        var shelves = await _unitOfWork.Repository<Shelf>().GetAllAsync();
        var bins = await _unitOfWork.Repository<Bin>().GetAllAsync();

        var zoneMap = zones.ToDictionary(z => z.Id, z => z.ZoneName);
        var shelfMap = shelves.ToDictionary(s => s.Id, s => s.ShelfCode);
        var binMap = bins.ToDictionary(b => b.Id, b => b.BinCode);

        return warehouseLayouts.Select(l => new WarehouseLocationLayoutDto
        {
            Id = l.Id,
            WarehouseId = l.WarehouseId,
            ZoneId = l.ZoneId,
            ShelfId = l.ShelfId,
            BinId = l.BinId,
            RowIndex = l.RowIndex,
            ColumnIndex = l.ColumnIndex,
            DisplayLabel = l.DisplayLabel,
            LayoutType = l.LayoutType,
            Status = l.Status,
            Width = l.Width,
            Height = l.Height,
            ColorCode = l.ColorCode,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt,
            ZoneName = l.ZoneId.HasValue && zoneMap.ContainsKey(l.ZoneId.Value) ? zoneMap[l.ZoneId.Value] : null,
            ShelfCode = l.ShelfId.HasValue && shelfMap.ContainsKey(l.ShelfId.Value) ? shelfMap[l.ShelfId.Value] : null,
            BinCode = l.BinId.HasValue && binMap.ContainsKey(l.BinId.Value) ? binMap[l.BinId.Value] : null
        }).ToList();
    }

    public async Task<WarehouseLocationLayoutDto> CreateLayoutAsync(Guid warehouseId, string userRole, CreateWarehouseLocationLayoutDto dto)
    {
        EnsureWriteAccess(userRole);

        var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId);
        if (warehouse == null)
        {
            throw new NotFoundException($"Không tìm thấy kho hàng với ID: {warehouseId}");
        }

        // Validate Coordinates
        if (dto.RowIndex < 0 || dto.ColumnIndex < 0)
        {
            throw new ArgumentException("Tọa độ hàng và cột không được nhỏ hơn 0.");
        }

        // Validate Uniqueness
        var layoutRepo = _unitOfWork.Repository<WarehouseLocationLayout>();
        var allLayouts = await layoutRepo.GetAllAsync();
        var exists = allLayouts.Any(l => l.WarehouseId == warehouseId && l.RowIndex == dto.RowIndex && l.ColumnIndex == dto.ColumnIndex);
        if (exists)
        {
            throw new ArgumentException($"Tọa độ ({dto.RowIndex}, {dto.ColumnIndex}) đã tồn tại một vị trí trong kho này.");
        }

        // Validate Links
        if (dto.ZoneId.HasValue)
        {
            var zone = await _unitOfWork.Repository<WarehouseZone>().GetByIdAsync(dto.ZoneId.Value);
            if (zone == null)
            {
                throw new NotFoundException($"Không tìm thấy khu vực (Zone) với ID: {dto.ZoneId.Value}");
            }
            if (zone.WarehouseId != warehouseId)
            {
                throw new ArgumentException("Khu vực (Zone) không thuộc kho hàng này.");
            }
        }

        if (dto.ShelfId.HasValue)
        {
            var shelf = await _unitOfWork.Repository<Shelf>().GetByIdAsync(dto.ShelfId.Value);
            if (shelf == null)
            {
                throw new NotFoundException($"Không tìm thấy kệ (Shelf) với ID: {dto.ShelfId.Value}");
            }
            if (dto.ZoneId.HasValue && shelf.ZoneId != dto.ZoneId.Value)
            {
                throw new ArgumentException("Kệ hàng (Shelf) không thuộc khu vực (Zone) đã chọn.");
            }
        }

        if (dto.BinId.HasValue)
        {
            var bin = await _unitOfWork.Repository<Bin>().GetByIdAsync(dto.BinId.Value);
            if (bin == null)
            {
                throw new NotFoundException($"Không tìm thấy ô chứa (Bin) với ID: {dto.BinId.Value}");
            }
            if (bin.WarehouseId != warehouseId)
            {
                throw new ArgumentException("Ô chứa (Bin) không thuộc kho hàng này.");
            }
            if (dto.ShelfId.HasValue && bin.ShelfId != dto.ShelfId.Value)
            {
                throw new ArgumentException("Ô chứa (Bin) không thuộc kệ hàng (Shelf) đã chọn.");
            }
        }

        var newLayout = new WarehouseLocationLayout
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            ZoneId = dto.ZoneId,
            ShelfId = dto.ShelfId,
            BinId = dto.BinId,
            RowIndex = dto.RowIndex,
            ColumnIndex = dto.ColumnIndex,
            DisplayLabel = dto.DisplayLabel,
            LayoutType = dto.LayoutType,
            Status = dto.Status,
            Width = dto.Width,
            Height = dto.Height,
            ColorCode = dto.ColorCode,
            CreatedAt = DateTime.UtcNow
        };

        await layoutRepo.AddAsync(newLayout);
        await _unitOfWork.SaveChangesAsync();

        return new WarehouseLocationLayoutDto
        {
            Id = newLayout.Id,
            WarehouseId = newLayout.WarehouseId,
            ZoneId = newLayout.ZoneId,
            ShelfId = newLayout.ShelfId,
            BinId = newLayout.BinId,
            RowIndex = newLayout.RowIndex,
            ColumnIndex = newLayout.ColumnIndex,
            DisplayLabel = newLayout.DisplayLabel,
            LayoutType = newLayout.LayoutType,
            Status = newLayout.Status,
            Width = newLayout.Width,
            Height = newLayout.Height,
            ColorCode = newLayout.ColorCode,
            CreatedAt = newLayout.CreatedAt
        };
    }

    public async Task<WarehouseLocationLayoutDto> UpdateLayoutAsync(Guid warehouseId, Guid layoutId, string userRole, UpdateWarehouseLocationLayoutDto dto)
    {
        EnsureWriteAccess(userRole);

        var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId);
        if (warehouse == null)
        {
            throw new NotFoundException($"Không tìm thấy kho hàng với ID: {warehouseId}");
        }

        var layoutRepo = _unitOfWork.Repository<WarehouseLocationLayout>();
        var layout = await layoutRepo.GetByIdAsync(layoutId);
        if (layout == null)
        {
            throw new NotFoundException($"Không tìm thấy vị trí sơ đồ kho với ID: {layoutId}");
        }
        if (layout.WarehouseId != warehouseId)
        {
            throw new ArgumentException("Vị trí sơ đồ này không thuộc kho hàng đã chọn.");
        }

        // Validate Coordinates
        if (dto.RowIndex < 0 || dto.ColumnIndex < 0)
        {
            throw new ArgumentException("Tọa độ hàng và cột không được nhỏ hơn 0.");
        }

        // Validate Uniqueness if coords changed
        if (layout.RowIndex != dto.RowIndex || layout.ColumnIndex != dto.ColumnIndex)
        {
            var allLayouts = await layoutRepo.GetAllAsync();
            var exists = allLayouts.Any(l => l.WarehouseId == warehouseId && l.RowIndex == dto.RowIndex && l.ColumnIndex == dto.ColumnIndex && l.Id != layoutId);
            if (exists)
            {
                throw new ArgumentException($"Tọa độ ({dto.RowIndex}, {dto.ColumnIndex}) đã tồn tại một vị trí trong kho này.");
            }
        }

        // Validate Links
        if (dto.ZoneId.HasValue)
        {
            var zone = await _unitOfWork.Repository<WarehouseZone>().GetByIdAsync(dto.ZoneId.Value);
            if (zone == null)
            {
                throw new NotFoundException($"Không tìm thấy khu vực (Zone) với ID: {dto.ZoneId.Value}");
            }
            if (zone.WarehouseId != warehouseId)
            {
                throw new ArgumentException("Khu vực (Zone) không thuộc kho hàng này.");
            }
        }

        if (dto.ShelfId.HasValue)
        {
            var shelf = await _unitOfWork.Repository<Shelf>().GetByIdAsync(dto.ShelfId.Value);
            if (shelf == null)
            {
                throw new NotFoundException($"Không tìm thấy kệ (Shelf) với ID: {dto.ShelfId.Value}");
            }
            if (dto.ZoneId.HasValue && shelf.ZoneId != dto.ZoneId.Value)
            {
                throw new ArgumentException("Kệ hàng (Shelf) không thuộc khu vực (Zone) đã chọn.");
            }
        }

        if (dto.BinId.HasValue)
        {
            var bin = await _unitOfWork.Repository<Bin>().GetByIdAsync(dto.BinId.Value);
            if (bin == null)
            {
                throw new NotFoundException($"Không tìm thấy ô chứa (Bin) với ID: {dto.BinId.Value}");
            }
            if (bin.WarehouseId != warehouseId)
            {
                throw new ArgumentException("Ô chứa (Bin) không thuộc kho hàng này.");
            }
            if (dto.ShelfId.HasValue && bin.ShelfId != dto.ShelfId.Value)
            {
                throw new ArgumentException("Ô chứa (Bin) không thuộc kệ hàng (Shelf) đã chọn.");
            }
        }

        layout.ZoneId = dto.ZoneId;
        layout.ShelfId = dto.ShelfId;
        layout.BinId = dto.BinId;
        layout.RowIndex = dto.RowIndex;
        layout.ColumnIndex = dto.ColumnIndex;
        layout.DisplayLabel = dto.DisplayLabel;
        layout.LayoutType = dto.LayoutType;
        layout.Status = dto.Status;
        layout.Width = dto.Width;
        layout.Height = dto.Height;
        layout.ColorCode = dto.ColorCode;
        layout.UpdatedAt = DateTime.UtcNow;

        layoutRepo.Update(layout);
        await _unitOfWork.SaveChangesAsync();

        return new WarehouseLocationLayoutDto
        {
            Id = layout.Id,
            WarehouseId = layout.WarehouseId,
            ZoneId = layout.ZoneId,
            ShelfId = layout.ShelfId,
            BinId = layout.BinId,
            RowIndex = layout.RowIndex,
            ColumnIndex = layout.ColumnIndex,
            DisplayLabel = layout.DisplayLabel,
            LayoutType = layout.LayoutType,
            Status = layout.Status,
            Width = layout.Width,
            Height = layout.Height,
            ColorCode = layout.ColorCode,
            CreatedAt = layout.CreatedAt,
            UpdatedAt = layout.UpdatedAt
        };
    }

    public async Task<bool> DeleteLayoutAsync(Guid warehouseId, Guid layoutId, string userRole)
    {
        EnsureWriteAccess(userRole);

        var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId);
        if (warehouse == null)
        {
            throw new NotFoundException($"Không tìm thấy kho hàng với ID: {warehouseId}");
        }

        var layoutRepo = _unitOfWork.Repository<WarehouseLocationLayout>();
        var layout = await layoutRepo.GetByIdAsync(layoutId);
        if (layout == null)
        {
            throw new NotFoundException($"Không tìm thấy vị trí sơ đồ kho với ID: {layoutId}");
        }
        if (layout.WarehouseId != warehouseId)
        {
            throw new ArgumentException("Vị trí sơ đồ này không thuộc kho hàng đã chọn.");
        }

        // Check Inventory in linked Bin before deleting
        if (layout.BinId.HasValue)
        {
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventory = await inventoryRepo.GetAllAsync();
            var hasActiveStock = allInventory.Any(i => i.BinId == layout.BinId.Value && i.Quantity > 0);
            if (hasActiveStock)
            {
                throw new ArgumentException("Không thể xóa vị trí sơ đồ kho vì ô chứa (Bin) tương ứng hiện vẫn đang có hàng tồn kho.");
            }
        }

        layoutRepo.Delete(layout);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }
}
