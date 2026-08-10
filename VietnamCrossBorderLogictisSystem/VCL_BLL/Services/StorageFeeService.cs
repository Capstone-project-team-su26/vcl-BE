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

            // Ô kệ nào thuộc kho đích — chỉ kho đích mới tính phí lưu, kho nguồn thì không.
            var destinationBinIds = await GetDestinationBinIdsAsync();

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

                var (storedAt, releasedAt) = ResolveStoragePeriod(parcel, movements, destinationBinIds);
                line.StoredAt = storedAt;
                line.ReleasedAt = releasedAt;

                if (storedAt.HasValue)
                {
                    var until = releasedAt ?? now;
                    var elapsed = until - storedAt.Value;

                    // Làm tròn lên theo ngày: nằm sang ngày thứ tám là tính một ngày phí.
                    line.StoredDays = elapsed <= TimeSpan.Zero
                        ? 0
                        : (int)Math.Ceiling(elapsed.TotalDays);

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

        /// <summary>
        /// Khoảng thời gian kiện nằm ở kho đích: từ lần cất vào ô kệ kho đích gần nhất
        /// đến lần lấy ra kế tiếp. Chưa lấy ra thì trả null để bên gọi tính tới hiện tại.
        /// </summary>
        private static (DateTime? StoredAt, DateTime? ReleasedAt) ResolveStoragePeriod(
            Parcel parcel, List<InventoryMovement> movements, HashSet<Guid> destinationBinIds)
        {
            var forParcel = movements.Where(m => m.ParcelId == parcel.Id).ToList();

            var inbound = forParcel
                .Where(m => destinationBinIds.Contains(m.ToBinId)
                            && !string.Equals(m.MovementType, "OUTBOUND", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.MovedAt)
                .FirstOrDefault();

            if (inbound == null)
            {
                // Chưa có nhật ký nhập kho đích. Kiện đang nằm trong ô kệ kho đích thì vẫn tính
                // được bằng PutAwayTime — trường hợp dữ liệu cũ không có movement.
                if (parcel.BinId.HasValue && destinationBinIds.Contains(parcel.BinId.Value) && parcel.PutAwayTime.HasValue)
                {
                    return (parcel.PutAwayTime, null);
                }
                return (null, null);
            }

            var outbound = forParcel
                .Where(m => string.Equals(m.MovementType, "OUTBOUND", StringComparison.OrdinalIgnoreCase)
                            && m.MovedAt > inbound.MovedAt)
                .OrderBy(m => m.MovedAt)
                .FirstOrDefault();

            return (inbound.MovedAt, outbound?.MovedAt);
        }

        private async Task<HashSet<Guid>> GetDestinationBinIdsAsync()
        {
            var destinationWarehouseIds = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .Where(w => string.Equals(w.WarehouseType?.Trim(), "DESTINATION", StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Id)
                .ToHashSet();

            return (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.WarehouseId.HasValue && destinationWarehouseIds.Contains(b.WarehouseId.Value))
                .Select(b => b.Id)
                .ToHashSet();
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
                return "Chưa có kiện nào được xếp vào ô kệ tại kho đích nên chưa tính phí lưu kho.";
            }

            var longest = result.Parcels.Max(p => p.StoredDays);
            return $"Kiện lưu lâu nhất mới {longest} ngày, còn trong {freeDays} ngày miễn phí.";
        }
    }
}
