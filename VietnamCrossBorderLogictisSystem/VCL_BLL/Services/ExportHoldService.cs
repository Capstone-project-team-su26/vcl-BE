using System;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Giữ hàng tại kho nguồn: hàng ký gửi mặc định là đi theo chuyến gần nhất, khách nào muốn
    /// giữ lại thì bật cờ này thay vì phải xác nhận từng lần xuất.
    /// </summary>
    public class ExportHoldService : IExportHoldService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;

        public ExportHoldService(IUnitOfWork unitOfWork, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
        }

        public async Task<object> SetHoldAsync(Guid userId, string role, Guid orderId, bool hold, string reason)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn {orderId}.");

            if (!StaffRoleHelper.IsInternalStaff(role))
            {
                var customer = order.CustomerId.HasValue
                    ? await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value)
                    : null;
                if (customer?.UserId != userId)
                    throw new UnauthorizedAccessException("Bạn chỉ bật/tắt giữ hàng cho đơn của mình.");
            }

            if (hold && string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Bật giữ hàng phải ghi lý do để kho biết vì sao hàng không lên chuyến.");

            // Kiện đã nằm trong phiếu đã duyệt thì vẫn đi: hàng có thể đang ở khu xuất chờ xe.
            // Cờ này chỉ chặn các phiếu lập sau, nên báo rõ để khách không hiểu nhầm là đã dừng.
            var lockedParcelCodes = hold ? await LockedParcelCodesAsync(orderId) : Array.Empty<string>();

            order.ExportHold = hold;
            order.ExportHoldReason = hold ? reason.Trim() : null;
            order.ExportHoldById = hold ? userId : null;
            order.ExportHoldAt = hold ? DateTime.UtcNow : null;
            _unitOfWork.Repository<Order>().Update(order);
            await _unitOfWork.SaveChangesAsync();

            if (hold)
            {
                await _notificationService.NotifyRolesAsync(
                    new[] { "Sale", "Sales", "WarehouseStaff", "OperationsManager" },
                    "Khách yêu cầu giữ hàng",
                    $"Đơn {order.ConsignmentCode} bật giữ hàng tại kho nguồn: {reason.Trim()}",
                    userId);
            }

            return new
            {
                orderId = order.Id,
                consignmentCode = order.ConsignmentCode,
                exportHold = order.ExportHold,
                exportHoldReason = order.ExportHoldReason,
                exportHoldAt = order.ExportHoldAt,
                parcelsAlreadyInApprovedRelease = lockedParcelCodes
            };
        }

        /// <summary>Kiện của đơn đang nằm trong phiếu xuất đã duyệt — giữ hàng không rút lại được.</summary>
        private async Task<string[]> LockedParcelCodesAsync(Guid orderId)
        {
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .ToDictionary(p => p.Id, p => p.PackageCode);
            if (parcels.Count == 0) return Array.Empty<string>();

            var approvedRequestIds = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                .Where(w => w.Status != null && new[]
                {
                    WroStatuses.Approved, WroStatuses.Picking, WroStatuses.Ready, WroStatuses.InShipment
                }.Contains(w.Status.Trim(), StringComparer.OrdinalIgnoreCase))
                .Select(w => w.Id)
                .ToHashSet();
            if (approvedRequestIds.Count == 0) return Array.Empty<string>();

            return (await _unitOfWork.Repository<WroParcel>().GetAllAsync())
                .Where(r => approvedRequestIds.Contains(r.WroRequestId)
                            && parcels.ContainsKey(r.ParcelId)
                            && !string.Equals(r.Status?.Trim(), WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase))
                .Select(r => parcels[r.ParcelId])
                .ToArray();
        }
    }
}
