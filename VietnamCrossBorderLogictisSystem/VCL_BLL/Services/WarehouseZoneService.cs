using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class WarehouseZoneDto
    {
        public Guid ZoneId { get; set; }
        public Guid WarehouseId { get; set; }
        public string ZoneCode { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;

        /// <summary>RECEIVING · QUARANTINE · STORAGE · OUTBOUND; trống là khu chưa phân loại (không xếp lưu kho được).</summary>
        public string ZoneType { get; set; } = string.Empty;
        public string ZoneTypeText { get; set; } = string.Empty;

        /// <summary>ACTIVE · INACTIVE.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Khu nhận hàng lưu kho được: loại STORAGE và đang dùng.</summary>
        public bool AcceptsStorage { get; set; }
        public int ShelfCount { get; set; }
        public int BinCount { get; set; }
        public int ActiveBinCount { get; set; }
        public int ParcelCount { get; set; }
    }

    public class UpdateWarehouseZoneRequest
    {
        public string? ZoneCode { get; set; }
        public string? ZoneName { get; set; }

        /// <summary>RECEIVING · QUARANTINE · STORAGE · OUTBOUND.</summary>
        public string ZoneType { get; set; } = string.Empty;

        /// <summary>ACTIVE · INACTIVE. Bỏ trống giữ nguyên.</summary>
        public string? Status { get; set; }
    }

    public class MisplacedParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
        public string ConsignmentCode { get; set; } = string.Empty;
        public Guid BinId { get; set; }
        public string BinCode { get; set; } = string.Empty;
        public Guid? ZoneId { get; set; }
        public string ZoneName { get; set; } = string.Empty;
        public string ZoneType { get; set; } = string.Empty;

        /// <summary>Vì sao kiện đang nằm sai chỗ, để kho biết cần chuyển về khu lưu kho.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Loại khu và trạng thái khu của kho. Hàng lưu kho chỉ nằm ở khu STORAGE đang dùng
    /// (xem <see cref="BinPlacementPolicy"/>).
    /// </summary>
    public class WarehouseZoneService
    {
        private readonly IUnitOfWork _unitOfWork;

        public WarehouseZoneService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<WarehouseZoneDto>> GetZonesAsync(Guid warehouseId)
        {
            _ = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId)
                ?? throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");

            var zones = (await _unitOfWork.Repository<WarehouseZone>().GetAllAsync())
                .Where(z => z.WarehouseId == warehouseId)
                .ToList();
            var shelves = (await _unitOfWork.Repository<Shelf>().GetAllAsync()).ToList();
            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).Where(b => b.WarehouseId == warehouseId).ToList();
            var occupancy = await BinPlacementPolicy.OccupancyAsync(_unitOfWork);

            return zones
                .Select(zone =>
                {
                    var shelfIds = shelves.Where(s => s.ZoneId == zone.Id).Select(s => s.Id).ToHashSet();
                    var zoneBins = bins.Where(b => b.ShelfId.HasValue && shelfIds.Contains(b.ShelfId.Value)).ToList();
                    return new WarehouseZoneDto
                    {
                        ZoneId = zone.Id,
                        WarehouseId = warehouseId,
                        ZoneCode = zone.ZoneCode ?? string.Empty,
                        ZoneName = zone.ZoneName ?? string.Empty,
                        ZoneType = ZoneTypes.Normalize(zone.ZoneType) ?? string.Empty,
                        ZoneTypeText = ZoneTypes.Describe(zone.ZoneType),
                        Status = BinPlacementPolicy.IsZoneActive(zone) ? "ACTIVE" : "INACTIVE",
                        AcceptsStorage = BinPlacementPolicy.IsStorageZone(zone),
                        ShelfCount = shelfIds.Count,
                        BinCount = zoneBins.Count,
                        ActiveBinCount = zoneBins.Count(b => b.IsActive),
                        ParcelCount = zoneBins.Sum(b => occupancy.GetValueOrDefault(b.Id).Count)
                    };
                })
                .OrderBy(z => Array.IndexOf(ZoneTypes.All, z.ZoneType) is var i && i < 0 ? int.MaxValue : i)
                .ThenBy(z => z.ZoneCode)
                .ThenBy(z => z.ZoneName)
                .ToList();
        }

        public async Task<WarehouseZoneDto> UpdateZoneAsync(Guid zoneId, UpdateWarehouseZoneRequest request)
        {
            var zoneRepo = _unitOfWork.Repository<WarehouseZone>();
            var zone = await zoneRepo.GetByIdAsync(zoneId)
                ?? throw new NotFoundException($"Không tìm thấy khu kho với ID: {zoneId}");
            var warehouseId = zone.WarehouseId
                ?? throw new ArgumentException("Khu kho chưa gắn kho.");

            var zoneType = ZoneTypes.Normalize(request?.ZoneType)
                ?? throw new ArgumentException($"Loại khu phải là một trong: {string.Join(", ", ZoneTypes.All)}.");

            var status = request!.Status?.Trim().ToUpperInvariant();
            if (status != null && status != "ACTIVE" && status != "INACTIVE")
                throw new ArgumentException("Trạng thái khu phải là ACTIVE hoặc INACTIVE.");

            var siblings = (await zoneRepo.GetAllAsync()).Where(z => z.WarehouseId == warehouseId && z.Id != zone.Id).ToList();

            var name = string.IsNullOrWhiteSpace(request.ZoneName) ? zone.ZoneName : request.ZoneName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tên khu là bắt buộc.");
            if (siblings.Any(z => string.Equals(z.ZoneName?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Kho đã có khu tên \"{name}\".");

            var code = request.ZoneCode == null ? zone.ZoneCode : request.ZoneCode.Trim();
            if (!string.IsNullOrWhiteSpace(code)
                && siblings.Any(z => string.Equals(z.ZoneCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Kho đã có khu mã \"{code}\".");

            // Khu đang chứa hàng lưu kho thì không đổi sang loại khác hay ngừng dùng — hàng trên kệ
            // sẽ nằm sai khu ngay lập tức. Chuyển hết hàng sang khu lưu kho khác trước.
            var parcelCount = await CountParcelsInZoneAsync(zone.Id);
            var willStopStorage = zoneType != ZoneTypes.Storage || status == "INACTIVE";
            if (parcelCount > 0 && BinPlacementPolicy.IsStorageZone(zone) && willStopStorage)
            {
                throw new ArgumentException(
                    $"Khu {zone.ZoneName} đang chứa {parcelCount} kiện nên không đổi loại hoặc ngừng dùng được. Chuyển hàng sang ô khu lưu kho khác trước.");
            }

            zone.ZoneName = name;
            zone.ZoneCode = string.IsNullOrWhiteSpace(code) ? null : code;
            zone.ZoneType = zoneType;
            zone.Status = status == null ? (BinPlacementPolicy.IsZoneActive(zone) ? "ACTIVE" : "INACTIVE") : status;
            zoneRepo.Update(zone);
            await _unitOfWork.SaveChangesAsync();

            return (await GetZonesAsync(warehouseId)).First(z => z.ZoneId == zone.Id);
        }

        /// <summary>
        /// Kiện đang chiếm ô kệ không hợp lệ cho hàng lưu kho: khu chưa phân loại, khu không phải
        /// STORAGE, khu hoặc ô đã ngừng dùng. Chỉ liệt kê — kho tự chuyển ô qua API điều chuyển.
        /// </summary>
        public async Task<List<MisplacedParcelDto>> GetMisplacedParcelsAsync(Guid warehouseId)
        {
            _ = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId)
                ?? throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");

            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).Where(b => b.WarehouseId == warehouseId).ToDictionary(b => b.Id);
            var shelves = (await _unitOfWork.Repository<Shelf>().GetAllAsync()).ToDictionary(s => s.Id);
            var zones = (await _unitOfWork.Repository<WarehouseZone>().GetAllAsync()).ToDictionary(z => z.Id);
            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);

            // Kiện đã bốc sang khu xuất theo một phiếu xuất kho là nằm ĐÚNG chỗ của nó, dù khu đó
            // không phải khu lưu kho. Không loại ra thì mỗi lần kho soạn hàng, báo cáo này lại
            // báo động giả đúng những kiện đang chờ lên xe.
            var stagedParcelIds = (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
                .Where(r => string.Equals(r.Status?.Trim(), WroParcelStatuses.Picked, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ParcelId)
                .ToHashSet();

            var result = new List<MisplacedParcelDto>();
            foreach (var parcel in (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                         .Where(p => p.BinId.HasValue && bins.ContainsKey(p.BinId.Value)
                                     && !string.Equals(p.PackageStatus, "SPLIT", StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(p.PackageStatus, "MERGED", StringComparison.OrdinalIgnoreCase)))
            {
                var bin = bins[parcel.BinId!.Value];
                WarehouseZone? zone = null;
                if (bin.ShelfId.HasValue && shelves.TryGetValue(bin.ShelfId.Value, out var shelf) && shelf.ZoneId.HasValue)
                    zones.TryGetValue(shelf.ZoneId.Value, out zone);

                string? reason = zone == null ? "Ô kệ chưa gắn khu"
                    : !BinPlacementPolicy.IsZoneActive(zone) ? "Khu đã ngừng dùng"
                    : ZoneTypes.Normalize(zone.ZoneType) == null ? "Khu chưa phân loại"
                    : ZoneTypes.Normalize(zone.ZoneType) != ZoneTypes.Storage ? $"Hàng đang nằm ở {ZoneTypes.Describe(zone.ZoneType)}"
                    : !bin.IsActive ? "Ô kệ đã ngừng dùng"
                    : null;
                if (reason == null) continue;
                if (stagedParcelIds.Contains(parcel.Id)
                    && zone != null && ZoneTypes.Normalize(zone.ZoneType) == ZoneTypes.Outbound) continue;

                result.Add(new MisplacedParcelDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode ?? string.Empty,
                    PackageStatus = parcel.PackageStatus ?? string.Empty,
                    ConsignmentCode = parcel.ConsignmentId.HasValue && orders.TryGetValue(parcel.ConsignmentId.Value, out var order)
                        ? order.ConsignmentCode ?? string.Empty
                        : string.Empty,
                    BinId = bin.Id,
                    BinCode = bin.BinCode ?? string.Empty,
                    ZoneId = zone?.Id,
                    ZoneName = zone?.ZoneName ?? string.Empty,
                    ZoneType = ZoneTypes.Normalize(zone?.ZoneType) ?? string.Empty,
                    Reason = reason
                });
            }

            return result.OrderBy(r => r.ZoneName).ThenBy(r => r.BinCode).ToList();
        }

        private async Task<int> CountParcelsInZoneAsync(Guid zoneId)
        {
            var shelfIds = (await _unitOfWork.Repository<Shelf>().GetAllAsync()).Where(s => s.ZoneId == zoneId).Select(s => s.Id).ToHashSet();
            var binIds = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.ShelfId.HasValue && shelfIds.Contains(b.ShelfId.Value)).Select(b => b.Id).ToHashSet();
            var occupancy = await BinPlacementPolicy.OccupancyAsync(_unitOfWork);
            return binIds.Sum(id => occupancy.GetValueOrDefault(id).Count);
        }
    }
}
