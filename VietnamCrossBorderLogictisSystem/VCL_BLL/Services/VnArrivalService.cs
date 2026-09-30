using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Việc của khách khi hàng về Việt Nam. Giao ngay hay gửi lại kho là quyết định của chủ hàng,
    /// không phải của kho — trước đây kho chọn lúc mở lô và ghi đè cả ý khách.
    /// </summary>
    public class VnArrivalService : IVnArrivalService
    {
        private static readonly HashSet<string> RetiredParcelStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "SPLIT", "MERGED" };

        private readonly IUnitOfWork _unitOfWork;
        private readonly IVnSettlementService _settlementService;

        public VnArrivalService(IUnitOfWork unitOfWork, IVnSettlementService settlementService)
        {
            _unitOfWork = unitOfWork;
            _settlementService = settlementService;
        }

        public async Task<List<ParcelHandlingDto>> GetHandlingAsync(Guid userId, string role, Guid orderId)
        {
            var order = await GetOrderAsync(orderId);
            await EnsureOwnerOrStaffAsync(order, userId, role);
            return await BuildHandlingAsync(order);
        }

        public async Task<List<ParcelHandlingDto>> SetHandlingAsync(
            Guid userId, string role, Guid orderId, SetDestinationHandlingRequest request)
        {
            var order = await GetOrderAsync(orderId);
            await EnsureOwnerOrStaffAsync(order, userId, role);

            if (request?.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Chọn ít nhất một kiện.");

            var current = (await BuildHandlingAsync(order)).ToDictionary(h => h.ParcelId);
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == order.Id)
                .ToDictionary(p => p.Id);

            foreach (var item in request.Items)
            {
                if (!current.TryGetValue(item.ParcelId, out var row) || !parcels.TryGetValue(item.ParcelId, out var parcel))
                    throw new ArgumentException($"Kiện {item.ParcelId} không thuộc đơn này.");

                var handling = DestinationHandlings.Normalize(item.Handling)
                    ?? throw new ArgumentException($"Cách xử lý kiện chỉ nhận {StatusNames.QuoteAny("DIRECT_DELIVERY", "STORE_AT_VN")}.");

                if (!row.CanChange && !string.Equals(row.Handling, handling, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Kiện {row.PackageCode}: {row.LockedReason}");

                parcel.DestinationHandling = handling;
                _unitOfWork.Repository<Parcel>().Update(parcel);
            }

            await _unitOfWork.SaveChangesAsync();
            return await BuildHandlingAsync(order);
        }

        public async Task<SettlementPreviewDto> GetSettlementPreviewAsync(Guid userId, string role, Guid orderId)
        {
            var order = await GetOrderAsync(orderId);
            await EnsureOwnerOrStaffAsync(order, userId, role);
            return await _settlementService.PreviewAsync(orderId);
        }

        /// <summary>
        /// Hướng xử lý từng kiện kèm cờ còn đổi được không. Kiện đã nằm trong phiếu nhập kho hoặc
        /// yêu cầu giao còn hiệu lực thì khoá — đổi lúc đó là kho đang làm một việc, sổ ghi việc khác.
        /// </summary>
        private async Task<List<ParcelHandlingDto>> BuildHandlingAsync(Order order)
        {
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == order.Id && !RetiredParcelStatuses.Contains(p.PackageStatus?.Trim() ?? string.Empty))
                .OrderBy(p => p.PackageCode)
                .ToList();
            var ids = parcels.Select(p => p.Id).ToHashSet();

            var liveInbound = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync())
                .Where(r => !string.Equals(r.Status, WarehouseInboundService.StatusRejected, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Id).ToHashSet();
            var inInbound = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Where(i => ids.Contains(i.ParcelId) && liveInbound.Contains(i.InboundRequestId))
                .Select(i => i.ParcelId).ToHashSet();

            var liveDelivery = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync())
                .Where(r => !DeliveryRequestService.IsClosedWithoutDelivery(r.Status))
                .Select(r => r.Id).ToHashSet();
            var inDelivery = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                .Where(i => ids.Contains(i.ParcelId) && liveDelivery.Contains(i.DeliveryRequestId))
                .Select(i => i.ParcelId).ToHashSet();

            var fallback = DestinationHandlings.Normalize(order.DefaultDestinationHandling) ?? DestinationHandlings.DirectDelivery;

            return parcels.Select(p =>
            {
                string? locked = null;
                var status = p.PackageStatus?.Trim().ToUpperInvariant();
                if (status is VnParcelStatuses.Delivered or VnParcelStatuses.Disposed)
                    locked = "Kiện đã giao hoặc đã huỷ.";
                else if (inDelivery.Contains(p.Id))
                    locked = "Kiện đã nằm trong yêu cầu giao hàng.";
                else if (inInbound.Contains(p.Id))
                    locked = "Kiện đã vào phiếu nhập kho Việt Nam.";

                return new ParcelHandlingDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode,
                    PackageStatus = p.PackageStatus,
                    Handling = DestinationHandlings.Normalize(p.DestinationHandling) ?? fallback,
                    CanChange = locked == null,
                    LockedReason = locked
                };
            }).ToList();
        }

        private async Task<Order> GetOrderAsync(Guid orderId) =>
            await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        private async Task EnsureOwnerOrStaffAsync(Order order, Guid userId, string role)
        {
            if (StaffRoleHelper.IsInternalStaff(role)) return;
            var customer = order.CustomerId.HasValue ? await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value) : null;
            if (customer?.UserId != userId)
                throw new UnauthorizedAccessException("Bạn chỉ thao tác được trên đơn của mình.");
        }
    }
}
