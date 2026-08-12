using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <inheritdoc cref="IStorageFeeService"/>
    public class StorageFeeService : IStorageFeeService
    {
        private const int DefaultFreeDays = 7;
        private const string StorageServiceType = ServicePricingHelper.StorageServiceType;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;

        public StorageFeeService(IUnitOfWork unitOfWork, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
        }

        public async Task<StorageFeeDto> CalculateForOrderAsync(Guid orderId)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var freeDays = ReadFreeDays();
            var (unitPrice, currency) = await ResolveUnitPriceAsync();

            var result = new StorageFeeDto
            {
                OrderId = order.Id,
                OrderCode = order.ConsignmentCode ?? string.Empty,
                FreeDays = freeDays,
                UnitPrice = unitPrice,
                Currency = currency
            };

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == order.Id)
                .ToList();

            if (parcels.Count == 0)
            {
                result.Note = "Đơn chưa có kiện hàng nào nên chưa phát sinh phí lưu kho.";
                return result;
            }

            // Phí lưu kho tính ở CẢ HAI KHO (nguồn quốc tế + đích VN) nên không lọc theo kho
            // nữa; chỉ cần biết mỗi ô kệ thuộc kho nào để hiện chi tiết từng chặng cho khách.
            var binWarehouses = await GetBinWarehouseMapAsync();

            var parcelIds = parcels.Select(p => p.Id).ToHashSet();
            var movements = (await _unitOfWork.Repository<InventoryMovement>().GetAllAsync())
                .Where(m => parcelIds.Contains(m.ParcelId))
                .OrderBy(m => m.MovedAt)
                .ToList();

            var now = DateTime.UtcNow;

            foreach (var parcel in parcels)
            {
                var line = new StorageFeeParcelDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode ?? string.Empty
                };

                foreach (var stay in ResolveStorageStays(parcel, movements))
                {
                    var until = stay.ReleasedAt ?? now;
                    var elapsed = until - stay.StoredAt;

                    // Làm tròn lên theo ngày, TỪNG CHẶNG một. Cộng thời lượng thô rồi mới làm
                    // tròn thì tổng lệch với các con số hiện trên màn hình, khách nhìn thấy
                    // "10 ngày + 2 ngày = 13 ngày" là mất tin ngay.
                    var days = elapsed <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(elapsed.TotalDays);

                    var knownWarehouse = binWarehouses.TryGetValue(stay.BinId, out var warehouse);

                    line.Legs.Add(new StorageFeeLegDto
                    {
                        WarehouseId = knownWarehouse ? warehouse.Id : null,
                        WarehouseName = knownWarehouse ? warehouse.Name : string.Empty,
                        WarehouseType = knownWarehouse ? warehouse.Type : string.Empty,
                        StoredAt = stay.StoredAt,
                        ReleasedAt = stay.ReleasedAt,
                        Days = days
                    });
                }

                if (line.Legs.Count > 0)
                {
                    line.StoredAt = line.Legs.Min(l => l.StoredAt);

                    // Còn chặng nào chưa đóng thì kiện vẫn đang nằm kho, đồng hồ chưa dừng.
                    line.ReleasedAt = line.Legs.Any(l => l.ReleasedAt == null)
                        ? null
                        : line.Legs.Max(l => l.ReleasedAt);

                    line.StoredDays = line.Legs.Sum(l => l.Days);

                    // Miễn phí trừ MỘT LẦN trên tổng cả hành trình, không phải mỗi kho một lần.
                    line.ChargeableDays = Math.Max(0, line.StoredDays - freeDays);
                    line.Amount = Math.Round(line.ChargeableDays * unitPrice, 0);
                }

                result.Parcels.Add(line);
            }

            result.TotalAmount = result.Parcels.Sum(p => p.Amount);

            if (result.TotalAmount == 0m)
            {
                result.Note = BuildZeroNote(result, unitPrice, freeDays);
            }

            return result;
        }

        /// <summary>Một lượt kiện nằm trong kho, dùng nội bộ khi dựng các chặng.</summary>
        private sealed class StorageStay
        {
            public Guid BinId { get; set; }
            public DateTime StoredAt { get; set; }
            public DateTime? ReleasedAt { get; set; }
        }

        /// <summary>
        /// Mọi lượt kiện nằm kho trên toàn hành trình, theo thứ tự thời gian. Kiện đi trọn
        /// đường có hai lượt: nằm kho nguồn chờ gom lô, rồi nằm kho đích chờ giao (nếu có nhập
        /// kho — luồng giao thẳng thì không có lượt thứ hai).
        ///
        /// Điều chuyển nội bộ (RELOCATION) chỉ đổi ô kệ chứ kiện vẫn nằm trong kho, nên KHÔNG
        /// mở lượt mới — mở mới ở đây là tính phí chồng lên chính khoảng thời gian đó.
        /// </summary>
        private static List<StorageStay> ResolveStorageStays(Parcel parcel, List<InventoryMovement> movements)
        {
            var stays = new List<StorageStay>();

            StorageStay? open = null;
            foreach (var movement in movements.Where(m => m.ParcelId == parcel.Id).OrderBy(m => m.MovedAt))
            {
                if (string.Equals(movement.MovementType, "OUTBOUND", StringComparison.OrdinalIgnoreCase))
                {
                    if (open != null)
                    {
                        open.ReleasedAt = movement.MovedAt;
                        stays.Add(open);
                        open = null;
                    }
                    continue;
                }

                open ??= new StorageStay { BinId = movement.ToBinId, StoredAt = movement.MovedAt };
            }

            // Lượt chưa đóng: kiện còn nằm trong kho, bên gọi tính tới hiện tại.
            if (open != null)
            {
                stays.Add(open);
            }

            // Dữ liệu cũ không có nhật ký nhưng kiện đang nằm trong ô kệ thì vẫn tính được.
            if (stays.Count == 0 && parcel.BinId.HasValue && parcel.PutAwayTime.HasValue)
            {
                stays.Add(new StorageStay { BinId = parcel.BinId.Value, StoredAt = parcel.PutAwayTime.Value });
            }

            return stays;
        }

        /// <summary>Ô kệ thuộc kho nào — để hiện tên kho trên từng chặng lưu kho.</summary>
        private async Task<Dictionary<Guid, (Guid Id, string Name, string Type)>> GetBinWarehouseMapAsync()
        {
            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .ToDictionary(w => w.Id);

            var map = new Dictionary<Guid, (Guid, string, string)>();
            foreach (var bin in await _unitOfWork.Repository<Bin>().GetAllAsync())
            {
                if (bin.WarehouseId.HasValue && warehouses.TryGetValue(bin.WarehouseId.Value, out var warehouse))
                {
                    map[bin.Id] = (warehouse.Id, warehouse.WarehouseName ?? string.Empty, warehouse.WarehouseType?.Trim() ?? string.Empty);
                }
            }

            return map;
        }

        /// <summary>
        /// Đơn giá lưu kho do Admin cấu hình trong SERVICE_PRICINGS. Chưa cấu hình thì trả 0
        /// để hệ thống không tự bịa ra tiền của khách.
        /// </summary>
        private async Task<(decimal UnitPrice, string Currency)> ResolveUnitPriceAsync()
        {
            var now = DateTime.UtcNow;
            var pricing = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                .Where(p => !p.IsDeleted
                            && string.Equals(p.ServiceType?.Trim(), StorageServiceType, StringComparison.OrdinalIgnoreCase)
                            && (!p.EffectiveDate.HasValue || p.EffectiveDate.Value <= now))
                .OrderByDescending(p => p.EffectiveDate ?? DateTime.MinValue)
                .FirstOrDefault();

            return (pricing?.Price ?? 0m, string.IsNullOrWhiteSpace(pricing?.Currency) ? "VND" : pricing.Currency);
        }

        private int ReadFreeDays()
        {
            var configured = _configuration["PricingSettings:StorageFreeDays"];
            return int.TryParse(configured, out var parsed) && parsed >= 0 ? parsed : DefaultFreeDays;
        }

        private static string BuildZeroNote(StorageFeeDto result, decimal unitPrice, int freeDays)
        {
            if (unitPrice <= 0m)
            {
                return $"Chưa cấu hình đơn giá lưu kho (SERVICE_PRICINGS, ServiceType = {StorageServiceType}) nên tạm tính 0đ.";
            }

            if (result.Parcels.All(p => p.StoredAt == null))
            {
                return "Chưa có kiện nào được xếp vào ô kệ nên chưa tính phí lưu kho.";
            }

            var longest = result.Parcels.Max(p => p.StoredDays);
            return $"Kiện lưu lâu nhất mới {longest} ngày (cộng cả hành trình), còn trong {freeDays} ngày miễn phí.";
        }
    }
}
