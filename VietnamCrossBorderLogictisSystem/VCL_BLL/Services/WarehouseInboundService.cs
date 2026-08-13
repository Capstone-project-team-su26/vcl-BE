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
    /// <inheritdoc cref="IWarehouseInboundService"/>
    public class WarehouseInboundService : IWarehouseInboundService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Lập phiếu: kho VN là người trực tiếp thấy hàng, OM lập thay cũng được.
        private static readonly string[] CreateRoles =
            { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Duyệt phiếu: chỉ OM, đúng như anh Hoàng chốt.
        private static readonly string[] ApproveRoles =
            { "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        public const string StatusPending = "INBOUND_PENDING";
        public const string StatusApproved = "INBOUND_APPROVED";
        public const string StatusRejected = "INBOUND_REJECTED";

        private const string StoreAtVn = "STORE_AT_VN";
        private const string ReceivedAtDestination = "RECEIVED_AT_DESTINATION";

        public WarehouseInboundService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<WarehouseInboundRequestDto> CreateAsync(
            Guid userId, string userRole, CreateWarehouseInboundRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu phiếu nhập kho không được để trống.");

            EnsureRole(userRole, CreateRoles, "Chỉ nhân viên kho hoặc Operations được lập phiếu nhập kho.");

            var shipment = await _unitOfWork.Repository<Shipment>().GetByIdAsync(request.ShipmentId)
                ?? throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {request.ShipmentId}");

            // Kiện của lô, kèm những kiện đã nằm trong phiếu nhập kho khác để khỏi lập trùng.
            var shipmentParcelIds = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .Where(sp => sp.ShipmentId == shipment.Id)
                .Select(sp => sp.ParcelId)
                .ToHashSet();

            var allParcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => shipmentParcelIds.Contains(p.Id))
                .ToList();

            var alreadyInRequest = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Select(i => i.ParcelId)
                .ToHashSet();

            List<Parcel> targets;
            if (request.ParcelIds != null && request.ParcelIds.Count > 0)
            {
                targets = new List<Parcel>();
                foreach (var pid in request.ParcelIds.Distinct())
                {
                    var parcel = allParcels.FirstOrDefault(p => p.Id == pid)
                        ?? throw new ArgumentException($"Kiện {pid} không thuộc lô vận chuyển này.");

                    EnsureParcelEligible(parcel, alreadyInRequest);
                    targets.Add(parcel);
                }
            }
            else
            {
                // Tự gom mọi kiện khách xin gửi lại mà chưa nằm trong phiếu nào.
                targets = allParcels
                    .Where(p => IsStoreAtVn(p) && IsReceived(p) && !alreadyInRequest.Contains(p.Id))
                    .ToList();

                if (targets.Count == 0)
                {
                    throw new ArgumentException(
                        "Lô này không còn kiện nào cần nhập kho. Kiện phải được kho xác nhận nhận " +
                        "và phân loại là khách gửi lại kho VN (STORE_AT_VN) thì mới lập phiếu được.");
                }
            }

            var warehouseId = request.WarehouseId ?? shipment.DestinationWarehouseId;
            if (warehouseId == null || warehouseId == Guid.Empty)
            {
                throw new ArgumentException("Không xác định được kho VN nhận hàng. Truyền warehouseId hoặc gán kho đích cho lô.");
            }

            var now = DateTime.UtcNow;
            var inbound = new WarehouseInboundRequest
            {
                Id = Guid.NewGuid(),
                InboundCode = $"WIN-{now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                ShipmentId = shipment.Id,
                WarehouseId = warehouseId,
                Status = StatusPending,
                Note = request.Note,
                CreatedBy = userId,
                CreatedAt = now
            };
            await _unitOfWork.Repository<WarehouseInboundRequest>().AddAsync(inbound);

            var itemRepo = _unitOfWork.Repository<WarehouseInboundItem>();
            foreach (var parcel in targets)
            {
                await itemRepo.AddAsync(new WarehouseInboundItem
                {
                    Id = Guid.NewGuid(),
                    InboundRequestId = inbound.Id,
                    ParcelId = parcel.Id
                });
            }

            await _unitOfWork.SaveChangesAsync();
            return await BuildDtoAsync(inbound);
        }

        public async Task<WarehouseInboundRequestDto> UpdateStatusAsync(
            Guid inboundRequestId, Guid userId, string userRole, UpdateWarehouseInboundStatusDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            EnsureRole(userRole, ApproveRoles, "Chỉ Operations Manager được duyệt phiếu nhập kho.");

            var repo = _unitOfWork.Repository<WarehouseInboundRequest>();
            var inbound = await repo.GetByIdAsync(inboundRequestId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu nhập kho với ID {inboundRequestId}");

            var target = request.Status?.Trim().ToUpperInvariant();
            if (target != StatusApproved && target != StatusRejected)
            {
                throw new ArgumentException($"Trạng thái không hợp lệ. Chỉ nhận {StatusApproved} hoặc {StatusRejected}.");
            }

            var current = inbound.Status?.Trim().ToUpperInvariant();
            if (current != StatusPending)
            {
                throw new ArgumentException($"Phiếu đã được xử lý trước đó (hiện tại: {inbound.Status}), không duyệt lại được.");
            }

            // Từ chối mà không nói lý do thì kho không biết phải sửa gì.
            if (target == StatusRejected && string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                throw new ArgumentException("Từ chối phiếu nhập kho thì bắt buộc ghi lý do.");
            }

            inbound.Status = target;
            inbound.ApprovedBy = userId;
            inbound.ApprovedAt = DateTime.UtcNow;
            inbound.RejectionReason = target == StatusRejected ? request.RejectionReason!.Trim() : null;
            repo.Update(inbound);

            await _unitOfWork.SaveChangesAsync();
            return await BuildDtoAsync(inbound);
        }

        public async Task<WarehouseInboundRequestDto> GetByIdAsync(Guid inboundRequestId)
        {
            var inbound = await _unitOfWork.Repository<WarehouseInboundRequest>().GetByIdAsync(inboundRequestId)
                ?? throw new NotFoundException($"Không tìm thấy phiếu nhập kho với ID {inboundRequestId}");

            return await BuildDtoAsync(inbound);
        }

        public async Task<List<WarehouseInboundRequestDto>> GetListAsync(string? status, Guid? shipmentId)
        {
            var all = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync()).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var s = status.Trim();
                all = all.Where(r => string.Equals(r.Status, s, StringComparison.OrdinalIgnoreCase));
            }

            if (shipmentId.HasValue && shipmentId.Value != Guid.Empty)
            {
                all = all.Where(r => r.ShipmentId == shipmentId.Value);
            }

            var result = new List<WarehouseInboundRequestDto>();
            foreach (var inbound in all.OrderByDescending(r => r.CreatedAt ?? DateTime.MinValue))
            {
                result.Add(await BuildDtoAsync(inbound));
            }
            return result;
        }

        // ----------------------------------------------------------------------------------

        private static void EnsureRole(string userRole, string[] allowed, string message)
        {
            var ok = !string.IsNullOrWhiteSpace(userRole)
                && allowed.Contains(userRole.Trim().ToUpperInvariant());
            if (!ok) throw new UnauthorizedAccessException(message);
        }

        private static bool IsStoreAtVn(Parcel parcel) =>
            string.Equals(parcel.DestinationHandling, StoreAtVn, StringComparison.OrdinalIgnoreCase);

        private static bool IsReceived(Parcel parcel) =>
            string.Equals(parcel.PackageStatus, ReceivedAtDestination, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Kiện chỉ vào được phiếu nhập kho khi kho đã mở lô nhận nó và khách đã nói là gửi lại.
        /// Chặn ở đây để không có chuyện hàng đang trên đường mà đã lập phiếu nhập kho, hoặc kiện
        /// khách chờ nhận ngay lại bị đem cất lên kệ rồi tính tiền lưu kho.
        /// </summary>
        private static void EnsureParcelEligible(Parcel parcel, HashSet<Guid> alreadyInRequest)
        {
            var code = parcel.PackageCode ?? parcel.Id.ToString();

            if (!IsReceived(parcel))
            {
                throw new ArgumentException(
                    $"Kiện {code} chưa được kho xác nhận nhận tại kho đích (hiện tại: {parcel.PackageStatus}).");
            }

            if (!IsStoreAtVn(parcel))
            {
                throw new ArgumentException(
                    $"Kiện {code} không phải hàng khách gửi lại kho VN " +
                    $"(hướng xử lý hiện tại: {parcel.DestinationHandling ?? "chưa phân loại"}). " +
                    "Kiện giao thẳng cho khách thì không nhập kho.");
            }

            if (alreadyInRequest.Contains(parcel.Id))
            {
                throw new ArgumentException($"Kiện {code} đã nằm trong một phiếu nhập kho khác.");
            }
        }

        private static string DescribeStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            StatusPending => "Chờ duyệt",
            StatusApproved => "Đã duyệt",
            StatusRejected => "Bị từ chối",
            _ => status ?? string.Empty
        };

        private async Task<WarehouseInboundRequestDto> BuildDtoAsync(WarehouseInboundRequest inbound)
        {
            var items = (await _unitOfWork.Repository<WarehouseInboundItem>().GetAllAsync())
                .Where(i => i.InboundRequestId == inbound.Id)
                .ToList();
            var parcelIds = items.Select(i => i.ParcelId).ToHashSet();

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToList();

            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id);
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id);
            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToDictionary(b => b.Id);
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id);

            var shipment = inbound.ShipmentId.HasValue
                ? await _unitOfWork.Repository<Shipment>().GetByIdAsync(inbound.ShipmentId.Value)
                : null;
            var warehouse = inbound.WarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(inbound.WarehouseId.Value)
                : null;

            var lines = new List<WarehouseInboundParcelDto>();
            foreach (var parcel in parcels)
            {
                Order? order = null;
                if (parcel.ConsignmentId.HasValue) orders.TryGetValue(parcel.ConsignmentId.Value, out order);

                Customer? customer = null;
                if (order?.CustomerId is Guid cid) customers.TryGetValue(cid, out customer);

                Bin? bin = null;
                if (parcel.BinId.HasValue) bins.TryGetValue(parcel.BinId.Value, out bin);

                lines.Add(new WarehouseInboundParcelDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode ?? string.Empty,
                    PackageStatus = parcel.PackageStatus ?? string.Empty,
                    OrderId = order?.Id,
                    OrderCode = order?.ConsignmentCode,
                    CustomerName = customer?.FullName,
                    BinId = parcel.BinId,
                    BinCode = bin?.BinCode
                });
            }

            string? NameOf(Guid? id) =>
                id.HasValue && users.TryGetValue(id.Value, out var u) ? u.FullName : null;

            return new WarehouseInboundRequestDto
            {
                InboundRequestId = inbound.Id,
                InboundCode = inbound.InboundCode ?? string.Empty,
                ShipmentId = inbound.ShipmentId,
                ShipmentCode = shipment?.ShipmentCode,
                WarehouseId = inbound.WarehouseId,
                WarehouseName = warehouse?.WarehouseName,
                Status = inbound.Status ?? string.Empty,
                StatusText = DescribeStatus(inbound.Status),
                Note = inbound.Note,
                RejectionReason = inbound.RejectionReason,
                CreatedByName = NameOf(inbound.CreatedBy),
                CreatedAt = inbound.CreatedAt,
                ApprovedByName = NameOf(inbound.ApprovedBy),
                ApprovedAt = inbound.ApprovedAt,
                TotalParcels = lines.Count,
                Parcels = lines
            };
        }
    }
}
