using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Helpers
{
    /// <summary>Loại khu trong kho. Chỉ khu <see cref="Storage"/> được xếp hàng lưu kho.</summary>
    public static class ZoneTypes
    {
        /// <summary>Khu nhận: hàng vừa check-in, chờ xếp kệ.</summary>
        public const string Receiving = "RECEIVING";

        /// <summary>Khu cách ly: hàng lệch/hỏng chờ quản lý kho quyết.</summary>
        public const string Quarantine = "QUARANTINE";

        /// <summary>Khu lưu kho: hàng đã nhập kho chính thức.</summary>
        public const string Storage = "STORAGE";

        /// <summary>Khu xuất: hàng đã soạn, chờ bàn giao.</summary>
        public const string Outbound = "OUTBOUND";

        public static readonly string[] All = { Receiving, Quarantine, Storage, Outbound };

        /// <summary>Chuẩn hoá loại khu; giá trị lạ hoặc trống trả null (khu chưa phân loại).</summary>
        public static string? Normalize(string? value)
        {
            var upper = value?.Trim().ToUpperInvariant();
            return All.Contains(upper) ? upper : null;
        }

        public static string Describe(string? value) => Normalize(value) switch
        {
            Receiving => "khu nhận",
            Quarantine => "khu cách ly",
            Storage => "khu lưu kho",
            Outbound => "khu xuất",
            _ => "khu chưa phân loại"
        };
    }

    /// <summary>Ô kệ kèm kệ, khu và kho chứa nó.</summary>
    public sealed record BinPlacement(Bin Bin, Shelf Shelf, WarehouseZone Zone, Warehouse Warehouse, decimal OccupiedVolumeAfter);

    /// <summary>
    /// Một chỗ duy nhất quyết định kiện được nằm ở ô kệ nào: xếp kệ, chuyển ô, tách/gộp kiện,
    /// nhận hàng hoàn và tự xếp khi duyệt phiếu nhập kho đều đi qua đây.
    /// </summary>
    /// <remarks>
    /// Trước đây mỗi luồng tự kiểm một kiểu: không luồng nào xét loại khu (hàng lưu kho nằm được
    /// ở khu nhận/khu xuất), chuyển ô và tách kiện không kiểm tải trọng giống xếp kệ, và sức chứa
    /// đọc từ cột CURRENT_VOLUME vốn lệch dần sau các lần xuất. Sức chứa giờ tính lại từ kiện
    /// đang thực sự nằm trong ô.
    /// </remarks>
    public static class BinPlacementPolicy
    {
        // Kiện gốc đã tách/gộp vẫn giữ BinId cũ nhưng không còn chiếm chỗ.
        private static readonly string[] RetiredStatuses = { "SPLIT", "MERGED" };

        public static bool IsZoneActive(WarehouseZone zone) =>
            !string.Equals(zone.Status?.Trim(), "INACTIVE", StringComparison.OrdinalIgnoreCase);

        public static bool IsStorageZone(WarehouseZone zone) =>
            ZoneTypes.Normalize(zone.ZoneType) == ZoneTypes.Storage && IsZoneActive(zone);

        public static decimal Volume(Parcel parcel) =>
            (parcel.Length ?? 0m) * (parcel.Width ?? 0m) * (parcel.Height ?? 0m);

        private static bool Occupies(Parcel parcel, Guid binId) =>
            parcel.BinId == binId && !RetiredStatuses.Contains(parcel.PackageStatus?.Trim().ToUpperInvariant());

        /// <summary>
        /// Tìm ô kệ theo mã quét được hoặc theo ID. Gửi cả hai thì phải trỏ cùng một ô.
        /// </summary>
        public static async Task<Bin> ResolveBinAsync(IUnitOfWork unitOfWork, Guid warehouseId, Guid? binId, string? binCode)
        {
            if (!string.IsNullOrWhiteSpace(binCode))
            {
                var code = binCode.Trim();
                var bin = (await unitOfWork.Repository<Bin>().GetAllAsync())
                    .FirstOrDefault(b => b.WarehouseId == warehouseId
                                         && string.Equals(b.BinCode?.Trim(), code, StringComparison.OrdinalIgnoreCase))
                    ?? throw new NotFoundException($"Không tìm thấy ô kệ mã {code} trong kho đã chọn.");

                if (binId.HasValue && binId.Value != Guid.Empty && binId.Value != bin.Id)
                    throw new ArgumentException($"Mã ô kệ {code} không khớp với ô kệ đã chọn.");

                return bin;
            }

            if (!binId.HasValue || binId.Value == Guid.Empty)
                throw new ArgumentException("Vui lòng quét mã ô kệ (binCode) hoặc chọn ô kệ (binId).");

            return await unitOfWork.Repository<Bin>().GetByIdAsync(binId.Value)
                ?? throw new NotFoundException("Không tìm thấy ô kệ (Bin).");
        }

        /// <summary>
        /// Kiểm ô kệ nhận được các kiện <paramref name="incoming"/>: đúng kho, ô và khu đang dùng,
        /// khu là khu lưu kho, còn đủ thể tích và tải trọng.
        /// </summary>
        /// <param name="leaving">Kiện đang nằm trong ô nhưng sẽ rời đi trong cùng thao tác (vd. kiện gốc khi tách/gộp).</param>
        /// <param name="requiredZoneType">Loại khu bắt buộc của ô. Mặc định khu lưu kho; bước bốc hàng của luồng xuất truyền khu xuất.</param>
        public static async Task<BinPlacement> EnsureCanStoreAsync(
            IUnitOfWork unitOfWork, Bin bin, Guid warehouseId,
            IReadOnlyCollection<Parcel> incoming, IReadOnlyCollection<Guid>? leaving = null,
            string? requiredZoneType = null)
        {
            var wantedZoneType = ZoneTypes.Normalize(requiredZoneType) ?? ZoneTypes.Storage;
            var code = bin.BinCode ?? bin.Id.ToString();

            var warehouse = await unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId)
                ?? throw new NotFoundException("Không tìm thấy kho hàng.");

            if (bin.WarehouseId != warehouseId)
                throw new ArgumentException($"Ô kệ {code} không thuộc kho {warehouse.WarehouseName}.");

            if (!bin.IsActive)
                throw new ArgumentException($"Ô kệ {code} đang ngừng sử dụng.");

            var shelf = bin.ShelfId.HasValue ? await unitOfWork.Repository<Shelf>().GetByIdAsync(bin.ShelfId.Value) : null;
            var zone = shelf?.ZoneId != null ? await unitOfWork.Repository<WarehouseZone>().GetByIdAsync(shelf.ZoneId.Value) : null;
            if (shelf == null || zone == null)
                throw new ArgumentException($"Ô kệ {code} chưa gắn vào kệ/khu nào nên không xếp hàng được.");

            if (zone.WarehouseId != warehouseId)
                throw new ArgumentException($"Khu {zone.ZoneName} của ô kệ {code} không thuộc kho {warehouse.WarehouseName}.");

            if (!IsZoneActive(zone))
                throw new ArgumentException($"Khu {zone.ZoneName} đang ngừng sử dụng.");

            if (ZoneTypes.Normalize(zone.ZoneType) != wantedZoneType)
            {
                var purpose = wantedZoneType == ZoneTypes.Outbound ? "Kiện đã bốc" : "Hàng lưu kho";
                throw new ArgumentException(
                    $"Ô kệ {code} thuộc {ZoneTypes.Describe(zone.ZoneType)} \"{zone.ZoneName}\". "
                    + $"{purpose} chỉ được xếp vào {ZoneTypes.Describe(wantedZoneType)}"
                    + (ZoneTypes.Normalize(zone.ZoneType) == null ? " — nhờ quản lý kho khai loại cho khu này." : "."));
            }

            var incomingIds = incoming.Select(p => p.Id).ToHashSet();
            var leavingIds = leaving?.ToHashSet() ?? new HashSet<Guid>();
            var staying = (await unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => Occupies(p, bin.Id) && !incomingIds.Contains(p.Id) && !leavingIds.Contains(p.Id))
                .ToList();

            var usedVolume = staying.Sum(Volume);
            var usedWeight = staying.Sum(p => p.ActualWeight ?? 0m);
            var addVolume = incoming.Sum(Volume);
            var addWeight = incoming.Sum(p => p.ActualWeight ?? 0m);

            if (bin.MaxVolume.HasValue && usedVolume + addVolume > bin.MaxVolume.Value)
            {
                throw new ArgumentException(
                    $"Ô kệ {code} không đủ sức chứa. Cần {addVolume:N0} cm³, ô còn {Math.Max(0m, bin.MaxVolume.Value - usedVolume):N0} cm³ "
                    + $"(sức chứa khai báo {bin.MaxVolume.Value:N0} cm³). Nếu ô thực tế lớn hơn, kiểm tra lại đơn vị maxVolume — trường này tính bằng cm³.");
            }

            if (bin.MaxWeight.HasValue && usedWeight + addWeight > bin.MaxWeight.Value)
            {
                throw new ArgumentException(
                    $"Ô kệ {code} không đủ tải trọng. Cần {addWeight:N2} kg, ô còn chịu được {Math.Max(0m, bin.MaxWeight.Value - usedWeight):N2} kg "
                    + $"(tải trọng khai báo {bin.MaxWeight.Value:N2} kg).");
            }

            return new BinPlacement(bin, shelf, zone, warehouse, usedVolume + addVolume);
        }

        /// <summary>Thể tích còn lại trong ô sau khi các kiện <paramref name="leaving"/> rời đi.</summary>
        public static async Task<decimal> VolumeAfterLeavingAsync(IUnitOfWork unitOfWork, Guid binId, IReadOnlyCollection<Guid> leaving)
        {
            var leavingIds = leaving.ToHashSet();
            return (await unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => Occupies(p, binId) && !leavingIds.Contains(p.Id))
                .Sum(Volume);
        }

        /// <summary>
        /// Thể tích và tải trọng đang chiếm của từng ô, tính từ kiện đang thực sự nằm trong ô.
        /// </summary>
        public static async Task<Dictionary<Guid, (decimal Volume, decimal Weight, int Count)>> OccupancyAsync(IUnitOfWork unitOfWork)
        {
            return (await unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.BinId.HasValue && Occupies(p, p.BinId.Value))
                .GroupBy(p => p.BinId!.Value)
                .ToDictionary(g => g.Key, g => (g.Sum(Volume), g.Sum(p => p.ActualWeight ?? 0m), g.Count()));
        }

        /// <summary>Ô còn chứa được kiện theo thể tích và tải trọng khai báo.</summary>
        public static bool Fits(Bin bin, (decimal Volume, decimal Weight, int Count) used, Parcel parcel) =>
            (!bin.MaxVolume.HasValue || used.Volume + Volume(parcel) <= bin.MaxVolume.Value)
            && (!bin.MaxWeight.HasValue || used.Weight + (parcel.ActualWeight ?? 0m) <= bin.MaxWeight.Value);

        /// <summary>Các ô kệ khu lưu kho đang dùng của một kho, kèm khu, để gợi ý vị trí.</summary>
        public static async Task<List<(Bin Bin, Shelf Shelf, WarehouseZone Zone)>> StorageBinsAsync(IUnitOfWork unitOfWork, Guid warehouseId)
        {
            var zones = (await unitOfWork.Repository<WarehouseZone>().GetAllAsync())
                .Where(z => z.WarehouseId == warehouseId && IsStorageZone(z))
                .ToDictionary(z => z.Id);
            var shelves = (await unitOfWork.Repository<Shelf>().GetAllAsync())
                .Where(s => s.ZoneId.HasValue && zones.ContainsKey(s.ZoneId.Value))
                .ToDictionary(s => s.Id);

            return (await unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.WarehouseId == warehouseId && b.IsActive && b.ShelfId.HasValue && shelves.ContainsKey(b.ShelfId.Value))
                .Select(b => (b, shelves[b.ShelfId!.Value], zones[shelves[b.ShelfId!.Value].ZoneId!.Value]))
                .OrderBy(x => x.Item3.ZoneCode ?? x.Item3.ZoneName)
                .ThenBy(x => x.b.BinCode)
                .ToList();
        }
    }
}
