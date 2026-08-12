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
    public class InternationalShipmentService : IInternationalShipmentService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Chỉ Staff hoặc Operations được tạo lô vận chuyển quốc tế
        private static readonly string[] AllowedRoles = { "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Cập nhật hành trình lô (trạng thái + xác nhận đến kho đích): thêm cả Sale.
        // Anh Hoàng chốt 11/08 — bên thứ 3 vận chuyển có trang quản lý riêng, sale vào đó xem
        // lô đi tới đâu / thông quan ra sao rồi gõ tay vào hệ thống mình; "vấn đề cho khách xem
        // thì để sale xử lý". Sale không tạo được lô mới, chỉ cập nhật hành trình lô đã có.
        private static readonly string[] TrackingUpdateRoles =
            { "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        // Nhận kiện tại kho đích: thêm cả Warehouse Staff
        private static readonly string[] ReceivingRoles = { "WAREHOUSESTAFF", "STAFF", "OPERATIONSMANAGER", "OPERATIONS", "ADMIN" };

        public InternationalShipmentService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        /// <summary>
        /// Dựng DTO đầy đủ cho một lô. Dùng chung cho cả danh sách lẫn chi tiết để hai
        /// đầu không lệch nhau — FE render cùng một component cho cả hai màn.
        /// </summary>
        private async Task<InternationalShipmentResponseDto> BuildShipmentDtoAsync(Shipment shipment)
        {
            var wroLinks = (await _unitOfWork.Repository<ShipmentWroRequest>().GetAllAsync())
                .Where(sw => sw.ShipmentId == shipment.Id)
                .ToList();
            var wroIds = wroLinks.Select(sw => sw.WroRequestId).ToHashSet();

            var wroRequests = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                .Where(w => wroIds.Contains(w.Id))
                .ToList();

            var allWarehouses = await _unitOfWork.Repository<Warehouse>().GetAllAsync();
            var origin = allWarehouses.FirstOrDefault(w => w.Id == shipment.OriginWarehouseId);
            var destination = allWarehouses.FirstOrDefault(w => w.Id == shipment.DestinationWarehouseId);

            Carrier? carrier = shipment.CarrierId.HasValue
                ? (await _unitOfWork.Repository<Carrier>().GetAllAsync()).FirstOrDefault(c => c.Id == shipment.CarrierId.Value)
                : null;

            // Tuyến không lưu trên Shipment; mọi WRO trong lô đã được validate là cùng tuyến
            // nên lấy của phiếu đầu tiên là đại diện cho cả lô.
            var routeId = wroRequests.Select(w => w.ShippingRouteId).FirstOrDefault(r => r.HasValue);
            ShippingRoute? route = routeId.HasValue
                ? (await _unitOfWork.Repository<ShippingRoute>().GetAllAsync()).FirstOrDefault(r => r.Id == routeId.Value)
                : null;

            // Kiện trong lô: qua WroItem → Inventory → Parcel
            var wroItems = (await _unitOfWork.Repository<WroItem>().GetAllAsync())
                .Where(i => wroIds.Contains(i.WroRequestId))
                .ToList();
            var inventories = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                .ToDictionary(i => i.Id, i => i);
            var allParcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .ToDictionary(p => p.Id, p => p);

            var parcels = new List<InternationalShipmentParcelDto>();
            var orderIds = new HashSet<Guid>();

            foreach (var item in wroItems)
            {
                if (!inventories.TryGetValue(item.InventoryId, out var inv)) continue;

                orderIds.Add(inv.OrderId);

                if (allParcels.TryGetValue(inv.ParcelId, out var parcel))
                {
                    parcels.Add(new InternationalShipmentParcelDto
                    {
                        ParcelId = parcel.Id,
                        PackageCode = parcel.PackageCode ?? string.Empty,
                        Weight = parcel.ActualWeight ?? parcel.ChargeableWeight
                    });
                }
            }

            // Hồ sơ PDF: phiếu nhập kho của các đơn liên quan. Phiếu xuất kho từng WRO
            // sẽ được nối vào đây khi ReleasePdfUrl có mặt.
            var pdfUrls = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => orderIds.Contains(o.Id) && !string.IsNullOrWhiteSpace(o.ReceiptPdfUrl))
                .Select(o => o.ReceiptPdfUrl!)
                .Distinct()
                .ToList();

            // Tờ khai của chính lô là nguồn đúng — mỗi lần bàn giao một tờ. Lô cũ chưa có thì
            // vẫn gom từ các phiếu xuất kho để dữ liệu trước đây không biến mất khỏi màn hình.
            var customsDocs = !string.IsNullOrWhiteSpace(shipment.CustomsDocumentUrls)
                ? shipment.CustomsDocumentUrls.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()
                : wroRequests
                    .Where(w => !string.IsNullOrWhiteSpace(w.CustomsDocumentUrls))
                    .SelectMany(w => w.CustomsDocumentUrls.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    .Distinct()
                    .ToList();

            return new InternationalShipmentResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                OriginWarehouseId = shipment.OriginWarehouseId,
                OriginWarehouseName = origin?.WarehouseName ?? string.Empty,
                DestinationWarehouseId = shipment.DestinationWarehouseId,
                DestinationWarehouseName = destination?.WarehouseName ?? string.Empty,
                CarrierId = shipment.CarrierId,
                CarrierName = carrier?.CarrierName,
                ShippingMethod = shipment.ShippingMethod,
                ShippingRouteId = routeId,
                ShippingRouteName = route?.RouteName ?? route?.RouteCode,
                CarrierTrackingCode = shipment.CarrierTrackingCode,
                TotalPackages = shipment.TotalPackages ?? parcels.Count,
                TotalWeight = shipment.TotalWeight ?? parcels.Sum(p => p.Weight ?? 0m),
                Status = shipment.Status ?? string.Empty,
                StatusTab = MapStatusToTab(shipment.Status),
                ShippedAt = shipment.ShippedAt,
                DeliveredAt = shipment.DeliveredAt,
                PdfUrls = pdfUrls,
                CustomsDocUrls = customsDocs,
                WroRequests = wroRequests
                    .Select(w => new InternationalShipmentWroDto { WroRequestId = w.Id, WroCode = w.WroCode ?? string.Empty })
                    .ToList(),
                Parcels = parcels
            };
        }

        public async Task<PaginatedResponse<InternationalShipmentResponseDto>> GetListAsync(
            int pageNumber, int pageSize, string? statusTab, string? status, Guid? originWarehouseId, string? search)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .Where(s => string.Equals(s.ShipmentStage, "INTERNATIONAL", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(s => string.Equals(s.Status, status.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(statusTab))
            {
                var tab = statusTab.Trim();
                query = query.Where(s => string.Equals(MapStatusToTab(s.Status), tab, StringComparison.OrdinalIgnoreCase));
            }

            if (originWarehouseId.HasValue)
            {
                query = query.Where(s => s.OriginWarehouseId == originWarehouseId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(s => (s.ShipmentCode ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query.OrderByDescending(s => s.ShippedAt ?? DateTime.MinValue).ToList();
            var totalCount = ordered.Count;

            var page = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

            var items = new List<InternationalShipmentResponseDto>();
            foreach (var shipment in page)
            {
                items.Add(await BuildShipmentDtoAsync(shipment));
            }

            return new PaginatedResponse<InternationalShipmentResponseDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<InternationalShipmentResponseDto> GetByIdAsync(Guid shipmentId)
        {
            var shipment = await _unitOfWork.Repository<Shipment>().GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            return await BuildShipmentDtoAsync(shipment);
        }

        public async Task<InternationalShipmentResponseDto> CreateAsync(Guid userId, string userRole, CreateInternationalShipmentDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu lô vận chuyển không được để trống.");

            // 1. Phân quyền
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && AllowedRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException("Chỉ Staff hoặc Operations được tạo lô vận chuyển quốc tế.");
            }

            // 2. Validate danh sách yêu cầu xuất kho
            if (request.WroRequestIds == null || request.WroRequestIds.Count == 0)
            {
                throw new ArgumentException("Phải chọn ít nhất một yêu cầu xuất kho.");
            }
            var wroRequestIds = request.WroRequestIds.Distinct().ToList();

            var wroRepo = _unitOfWork.Repository<WroRequest>();
            var shipmentWroRepo = _unitOfWork.Repository<ShipmentWroRequest>();
            var allShipmentWros = await shipmentWroRepo.GetAllAsync();

            var wroRequests = new List<WroRequest>();
            foreach (var wroId in wroRequestIds)
            {
                var wro = await wroRepo.GetByIdAsync(wroId);
                if (wro == null)
                {
                    throw new NotFoundException($"Không tìm thấy yêu cầu xuất kho với ID {wroId}");
                }

                // Nhận yêu cầu đã xuất kho xong: RELEASED (do /complete đặt) hoặc HANDED_OVER
                // (do /handover đặt sau đó). Trước đây chỉ chấp nhận RELEASED, mà bàn giao cho nhà xe
                // lại ghi đè sang HANDED_OVER — đi đúng thứ tự nghiệp vụ là không bao giờ tạo được lô,
                // hàng đứng vĩnh viễn ở kho nguồn.
                var eligible = new[] { "RELEASED", "HANDED_OVER" };
                if (!eligible.Contains(wro.Status?.Trim().ToUpperInvariant()))
                {
                    throw new ArgumentException(
                        $"Yêu cầu xuất kho {wro.WroCode ?? wroId.ToString()} chưa đủ điều kiện " +
                        $"(phải đã xuất kho xong — RELEASED hoặc HANDED_OVER; hiện tại: {wro.Status}).");
                }

                // Không cho gán cùng một yêu cầu xuất kho vào nhiều lô vận chuyển
                if (allShipmentWros.Any(sw => sw.WroRequestId == wroId))
                {
                    throw new ArgumentException($"Yêu cầu xuất kho {wro.WroCode ?? wroId.ToString()} đã thuộc một lô vận chuyển khác.");
                }

                wroRequests.Add(wro);
            }

            // 2b. Mọi WRO trong lô phải cùng kho xuất và cùng tuyến, nếu không thì lô này
            // không thể đi chung một chuyến. Kho xuất suy từ vị trí thật của hàng
            // (WroItem → Inventory → Bin → Warehouse) chứ không tin số client khai.
            var routeIds = wroRequests
                .Select(w => w.ShippingRouteId)
                .Distinct()
                .ToList();

            if (routeIds.Count > 1)
            {
                throw new ArgumentException(
                    "Tất cả phiếu WRO trong Lô phải cùng Kho xuất và cùng Tuyến vận chuyển! " +
                    $"Đang có {routeIds.Count} tuyến khác nhau trong danh sách đã chọn.");
            }

            var wroItemsForCheck = (await _unitOfWork.Repository<WroItem>().GetAllAsync())
                .Where(i => wroRequestIds.Contains(i.WroRequestId))
                .ToList();

            var inventoriesForCheck = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                .ToDictionary(i => i.Id, i => i);

            var binsForCheck = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .ToDictionary(b => b.Id, b => b);

            var sourceWarehouseIds = new HashSet<Guid>();
            foreach (var wroItem in wroItemsForCheck)
            {
                if (inventoriesForCheck.TryGetValue(wroItem.InventoryId, out var inv) &&
                    binsForCheck.TryGetValue(inv.BinId, out var bin) &&
                    bin.WarehouseId.HasValue)
                {
                    sourceWarehouseIds.Add(bin.WarehouseId.Value);
                }
            }

            if (sourceWarehouseIds.Count > 1)
            {
                throw new ArgumentException(
                    "Tất cả phiếu WRO trong Lô phải cùng Kho xuất và cùng Tuyến vận chuyển! " +
                    $"Đang có {sourceWarehouseIds.Count} kho xuất khác nhau trong danh sách đã chọn.");
            }

            if (sourceWarehouseIds.Count == 1 && !sourceWarehouseIds.Contains(request.OriginWarehouseId))
            {
                throw new ArgumentException(
                    "Kho đi khai báo không khớp với kho đang chứa hàng của các phiếu WRO đã chọn.");
            }

            // 3. Validate kho đi/kho đến
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var originWarehouse = await warehouseRepo.GetByIdAsync(request.OriginWarehouseId);
            if (originWarehouse == null)
                throw new NotFoundException($"Không tìm thấy kho đi với ID {request.OriginWarehouseId}");
            var destinationWarehouse = await warehouseRepo.GetByIdAsync(request.DestinationWarehouseId);
            if (destinationWarehouse == null)
                throw new NotFoundException($"Không tìm thấy kho đến với ID {request.DestinationWarehouseId}");

            Carrier? carrier = null;
            if (request.CarrierId.HasValue)
            {
                carrier = await _unitOfWork.Repository<Carrier>().GetByIdAsync(request.CarrierId.Value);
                if (carrier == null)
                    throw new NotFoundException($"Không tìm thấy đơn vị vận chuyển với ID {request.CarrierId.Value}");
            }

            // 4. Gom danh sách Parcel từ các WRO (WroItem -> Inventory -> Parcel)
            var wroItemRepo = _unitOfWork.Repository<WroItem>();
            var allWroItems = await wroItemRepo.GetAllAsync();
            var inventoryRepo = _unitOfWork.Repository<Inventory>();
            var allInventories = await inventoryRepo.GetAllAsync();
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();

            var parcelIds = new HashSet<Guid>();
            foreach (var wro in wroRequests)
            {
                var items = allWroItems.Where(i => i.WroRequestId == wro.Id);
                foreach (var item in items)
                {
                    var inv = allInventories.FirstOrDefault(x => x.Id == item.InventoryId);
                    if (inv != null && inv.ParcelId != Guid.Empty)
                    {
                        parcelIds.Add(inv.ParcelId);
                    }
                }
            }

            var parcels = allParcels.Where(p => parcelIds.Contains(p.Id)).ToList();
            var totalPackages = parcels.Count;
            var totalWeight = parcels.Sum(p => p.ActualWeight ?? p.ChargeableWeight ?? 0m);

            // 5. Tạo lô vận chuyển (Shipment) trạng thái CREATED
            var shipmentCode = $"INTL-{DateTime.UtcNow:yyyyMMddHHmmss}-{new Random().Next(1000, 9999)}";
            var shipment = new Shipment
            {
                Id = Guid.NewGuid(),
                ShipmentCode = shipmentCode,
                ShipmentType = "INTERNATIONAL",
                ShipmentStage = "INTERNATIONAL",
                Status = "CREATED",
                OriginWarehouseId = request.OriginWarehouseId,
                DestinationWarehouseId = request.DestinationWarehouseId,
                CarrierId = request.CarrierId,
                ShippingMethod = request.ShippingMethod,
                TotalPackages = totalPackages,
                TotalWeight = totalWeight,
                // Tờ khai thông quan thuộc về lần bàn giao, không thuộc từng phiếu xuất kho.
                CustomsDocumentUrls = request.CustomsDocumentUrls is { Count: > 0 }
                    ? string.Join(';', request.CustomsDocumentUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
                    : null
            };
            await _unitOfWork.Repository<Shipment>().AddAsync(shipment);

            // 6. Liên kết WRO và Parcel vào lô
            foreach (var wro in wroRequests)
            {
                await shipmentWroRepo.AddAsync(new ShipmentWroRequest
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    WroRequestId = wro.Id
                });
            }

            var shipmentParcelRepo = _unitOfWork.Repository<ShipmentParcel>();
            foreach (var parcel in parcels)
            {
                await shipmentParcelRepo.AddAsync(new ShipmentParcel
                {
                    Id = Guid.NewGuid(),
                    ShipmentId = shipment.Id,
                    ParcelId = parcel.Id
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 7. Trả về kết quả
            return new InternationalShipmentResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode,
                OriginWarehouseId = shipment.OriginWarehouseId,
                OriginWarehouseName = originWarehouse.WarehouseName ?? string.Empty,
                DestinationWarehouseId = shipment.DestinationWarehouseId,
                DestinationWarehouseName = destinationWarehouse.WarehouseName ?? string.Empty,
                CarrierId = shipment.CarrierId,
                CarrierName = carrier?.CarrierName,
                ShippingMethod = shipment.ShippingMethod,
                TotalPackages = totalPackages,
                TotalWeight = totalWeight,
                Status = shipment.Status,
                WroRequests = wroRequests.Select(w => new InternationalShipmentWroDto
                {
                    WroRequestId = w.Id,
                    WroCode = w.WroCode ?? string.Empty
                }).ToList(),
                Parcels = parcels.Select(p => new InternationalShipmentParcelDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    Weight = p.ActualWeight ?? p.ChargeableWeight
                }).ToList()
            };
        }

        /// <summary>
        /// Vòng đời lô vận chuyển quốc tế, bám theo nghiệp vụ thực tế:
        ///
        ///   CREATED → MANIFESTED → CUSTOMS_EXPORT_PENDING → IN_TRANSIT
        ///           → CUSTOMS_IMPORT_PENDING → ARRIVED_DESTINATION
        ///
        /// Hai chặng thông quan tách riêng vì mỗi chặng có thể bị treo độc lập:
        /// xuất tại nước nguồn và nhập tại Việt Nam.
        ///
        /// Giữ lại đường tắt MANIFESTED → IN_TRANSIT và IN_TRANSIT → ARRIVED_DESTINATION
        /// để client cũ chưa cập nhật vẫn chạy được.
        /// </summary>
        private static readonly Dictionary<string, string[]> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MANIFESTED"] = new[] { "CREATED" },
            ["CUSTOMS_EXPORT_PENDING"] = new[] { "MANIFESTED", "CUSTOMS_REJECTED", "HOLD" },
            ["IN_TRANSIT"] = new[] { "CUSTOMS_EXPORT_PENDING", "MANIFESTED", "HOLD" },
            ["CUSTOMS_IMPORT_PENDING"] = new[] { "IN_TRANSIT", "CUSTOMS_REJECTED", "HOLD" },
            ["ARRIVED_DESTINATION"] = new[] { "CUSTOMS_IMPORT_PENDING", "IN_TRANSIT" },

            // Trạng thái sự cố: vào được từ mọi chặng chưa kết thúc, và gỡ ra được.
            ["CUSTOMS_REJECTED"] = new[] { "CUSTOMS_EXPORT_PENDING", "CUSTOMS_IMPORT_PENDING" },
            ["HOLD"] = new[] { "CREATED", "MANIFESTED", "CUSTOMS_EXPORT_PENDING", "IN_TRANSIT", "CUSTOMS_IMPORT_PENDING", "CUSTOMS_REJECTED" },
            ["ISSUE"] = new[] { "CREATED", "MANIFESTED", "CUSTOMS_EXPORT_PENDING", "IN_TRANSIT", "CUSTOMS_IMPORT_PENDING", "CUSTOMS_REJECTED", "HOLD" }
        };

        /// <summary>
        /// Gom trạng thái về đúng 5 tab của màn Vận chuyển bên mobile, để FE lọc theo
        /// một giá trị thay vì phải tự nhớ trạng thái nào thuộc tab nào.
        /// </summary>
        public static string MapStatusToTab(string? status)
        {
            var s = status?.Trim().ToUpperInvariant() ?? string.Empty;

            return s switch
            {
                "CREATED" or "MANIFESTED" or "LOT_CREATED" or "PREPARING" => "PREPARING",
                "CUSTOMS_EXPORT_PENDING" or "CUSTOMS_IMPORT_PENDING" => "CUSTOMS",
                "IN_TRANSIT" => "IN_TRANSIT",
                "ARRIVED_DESTINATION" or "ARRIVED_IN_VN" => "ARRIVED",
                "ISSUE" or "CUSTOMS_REJECTED" or "HOLD" => "ISSUE",
                _ => "PREPARING"
            };
        }

        public async Task<UpdateInternationalShipmentStatusResponseDto> UpdateStatusAsync(
            Guid shipmentId, Guid userId, string userRole, UpdateInternationalShipmentStatusDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            // 1. Phân quyền
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && TrackingUpdateRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException("Chỉ Sale, Staff hoặc Operations được cập nhật trạng thái lô vận chuyển.");
            }

            // 2. Tìm lô vận chuyển (404 nếu không có)
            var shipmentRepo = _unitOfWork.Repository<Shipment>();
            var shipment = await shipmentRepo.GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            // 3. Validate trạng thái mới + đúng thứ tự
            var targetStatus = request.Status.Trim().ToUpperInvariant();
            if (!AllowedTransitions.ContainsKey(targetStatus))
            {
                throw new ArgumentException(
                    $"Trạng thái không hợp lệ. Hợp lệ: {string.Join(", ", AllowedTransitions.Keys)}.");
            }

            var currentStatus = shipment.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var allowedFrom = AllowedTransitions[targetStatus];
            if (!allowedFrom.Contains(currentStatus, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Không thể chuyển sang {targetStatus} khi lô đang ở trạng thái {shipment.Status}. " +
                    $"Chỉ chuyển được từ: {string.Join(", ", allowedFrom)}.");
            }

            // 4. Cập nhật trạng thái + mốc thời gian tương ứng
            var now = DateTime.UtcNow;
            shipment.Status = targetStatus;
            if (targetStatus == "IN_TRANSIT") shipment.ShippedAt = now;
            if (targetStatus == "ARRIVED_DESTINATION") shipment.DeliveredAt = now;

            // Mã vận đơn bên đối tác: sale nhập tay, thường chỉ có từ lúc lô bắt đầu đi. Bỏ
            // trống thì giữ nguyên mã cũ — lần cập nhật trạng thái sau không được xoá mất mã
            // mà sale đã nhập, vì đó là thứ duy nhất tra được lô trên trang của đối tác.
            if (!string.IsNullOrWhiteSpace(request.CarrierTrackingCode))
            {
                shipment.CarrierTrackingCode = request.CarrierTrackingCode.Trim();
            }

            shipmentRepo.Update(shipment);

            // 5. Lưu lịch sử cập nhật (Tracking)
            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = targetStatus,
                TrackingStage = "INTERNATIONAL",
                Note = request.Note,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            // 6. Lấy tên người cập nhật
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);

            return new UpdateInternationalShipmentStatusResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                Status = shipment.Status,
                StatusTab = MapStatusToTab(shipment.Status),
                Note = request.Note,
                CarrierTrackingCode = shipment.CarrierTrackingCode,
                UpdatedAt = now,
                UpdatedByName = user?.FullName ?? string.Empty
            };
        }

        public async Task<ConfirmShipmentArrivalResponseDto> ConfirmArrivalAsync(
            Guid shipmentId, Guid userId, string userRole, ConfirmShipmentArrivalDto request)
        {
            // 1. Phân quyền
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && TrackingUpdateRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException("Chỉ Sale, Staff hoặc Operations được xác nhận lô đến kho đích.");
            }

            // 2. Tìm lô (404)
            var shipmentRepo = _unitOfWork.Repository<Shipment>();
            var shipment = await shipmentRepo.GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            // 3. Chỉ xác nhận khi đang IN_TRANSIT (đồng thời chặn xác nhận nhiều lần vì đã ARRIVED sẽ khác IN_TRANSIT)
            var currentStatus = shipment.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (currentStatus == "ARRIVED_DESTINATION")
            {
                throw new ArgumentException("Lô vận chuyển này đã được xác nhận đến kho đích trước đó.");
            }
            // Khớp đúng bảng AllowedTransitions ở trên: ARRIVED_DESTINATION vào được từ IN_TRANSIT
            // hoặc CUSTOMS_IMPORT_PENDING. Trước đây chỉ nhận IN_TRANSIT, nên lô nào đi đúng vòng
            // đời có chặng thông quan nhập là không xác nhận tới kho được — kẹt vĩnh viễn.
            var arrivableFrom = new[] { "IN_TRANSIT", "CUSTOMS_IMPORT_PENDING" };
            if (!arrivableFrom.Contains(currentStatus))
            {
                throw new ArgumentException(
                    $"Lô chưa đủ điều kiện xác nhận đến kho đích (phải đang IN_TRANSIT hoặc " +
                    $"CUSTOMS_IMPORT_PENDING, hiện tại: {shipment.Status}).");
            }

            var now = DateTime.UtcNow;

            // 4. Cập nhật lô -> ARRIVED_DESTINATION
            shipment.Status = "ARRIVED_DESTINATION";
            shipment.DeliveredAt = now;
            shipmentRepo.Update(shipment);

            // 5. Cập nhật các Parcel trong lô -> ARRIVED_DESTINATION (sẵn sàng cho nhập kho tại kho đích)
            var shipmentParcelRepo = _unitOfWork.Repository<ShipmentParcel>();
            var allShipmentParcels = await shipmentParcelRepo.GetAllAsync();
            var parcelIds = allShipmentParcels
                .Where(sp => sp.ShipmentId == shipment.Id)
                .Select(sp => sp.ParcelId)
                .ToHashSet();

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var parcels = allParcels.Where(p => parcelIds.Contains(p.Id)).ToList();
            // Kiện đã rời kho nguồn nên phải nhả chỗ trong ô kệ. Thiếu bước này thì
            // Bin.CurrentVolume chỉ tăng mà không bao giờ giảm, lâu dần mọi ô đều trông như đã đầy.
            var binRepo = _unitOfWork.Repository<Bin>();
            var allBins = await binRepo.GetAllAsync();
            var binsById = allBins.ToDictionary(b => b.Id);

            foreach (var parcel in parcels)
            {
                if (parcel.BinId.HasValue && binsById.TryGetValue(parcel.BinId.Value, out var sourceBin))
                {
                    var released = (parcel.Length ?? 0m) * (parcel.Width ?? 0m) * (parcel.Height ?? 0m);
                    var remaining = (sourceBin.CurrentVolume ?? 0m) - released;
                    sourceBin.CurrentVolume = remaining < 0m ? 0m : remaining;
                    binRepo.Update(sourceBin);
                }

                parcel.BinId = null;
                parcel.PackageStatus = "ARRIVED_DESTINATION";
                parcelRepo.Update(parcel);
            }

            // 6. Lưu lịch sử (Tracking)
            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = "ARRIVED_DESTINATION",
                TrackingStage = "INTERNATIONAL",
                Note = request?.Note,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            // 7. Thông tin kho đích + người xác nhận
            var destinationWarehouse = shipment.DestinationWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(shipment.DestinationWarehouseId.Value)
                : null;
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);

            return new ConfirmShipmentArrivalResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                DestinationWarehouseId = shipment.DestinationWarehouseId,
                DestinationWarehouseName = destinationWarehouse?.WarehouseName ?? string.Empty,
                Status = shipment.Status,
                ArrivedAt = now,
                ConfirmedByName = user?.FullName ?? string.Empty,
                Note = request?.Note,
                TotalPackages = parcels.Count,
                Parcels = parcels.Select(p => new InternationalShipmentParcelDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    Weight = p.ActualWeight ?? p.ChargeableWeight
                }).ToList()
            };
        }

        public async Task<DestinationReceivingResponseDto> ConfirmDestinationReceivingAsync(
            Guid shipmentId, Guid userId, string userRole, DestinationReceivingDto request)
        {
            // 1. Phân quyền (Warehouse Staff / Staff / Operations)
            bool allowed = !string.IsNullOrWhiteSpace(userRole)
                && ReceivingRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!allowed)
            {
                throw new UnauthorizedAccessException("Chỉ Warehouse Staff, Staff hoặc Operations được xác nhận nhận kiện tại kho đích.");
            }

            // 2. Tìm lô (404)
            var shipmentRepo = _unitOfWork.Repository<Shipment>();
            var shipment = await shipmentRepo.GetByIdAsync(shipmentId);
            if (shipment == null)
            {
                throw new NotFoundException($"Không tìm thấy lô vận chuyển với ID {shipmentId}");
            }

            // 3. Chỉ nhận kiện khi lô đã ARRIVED_DESTINATION
            if (!string.Equals(shipment.Status, "ARRIVED_DESTINATION", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Lô chưa đến kho đích (phải ở trạng thái ARRIVED_DESTINATION, hiện tại: {shipment.Status}).");
            }

            // 4. Lấy các Parcel thuộc lô
            var shipmentParcelRepo = _unitOfWork.Repository<ShipmentParcel>();
            var allShipmentParcels = await shipmentParcelRepo.GetAllAsync();
            var shipmentParcelIds = allShipmentParcels
                .Where(sp => sp.ShipmentId == shipment.Id)
                .Select(sp => sp.ParcelId)
                .ToHashSet();

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var shipmentParcels = allParcels.Where(p => shipmentParcelIds.Contains(p.Id)).ToList();

            // 5. Xác định danh sách Parcel cần nhận
            List<Parcel> targets;
            if (request?.ParcelIds != null && request.ParcelIds.Count > 0)
            {
                var requestedIds = request.ParcelIds.Distinct().ToList();
                targets = new List<Parcel>();
                foreach (var pid in requestedIds)
                {
                    var parcel = shipmentParcels.FirstOrDefault(p => p.Id == pid);
                    if (parcel == null)
                    {
                        throw new ArgumentException($"Parcel {pid} không thuộc lô vận chuyển này.");
                    }
                    if (string.Equals(parcel.PackageStatus, "RECEIVED_AT_DESTINATION", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException($"Parcel {parcel.PackageCode ?? pid.ToString()} đã được xác nhận nhận tại kho đích trước đó.");
                    }
                    targets.Add(parcel);
                }
            }
            else
            {
                // Nhận tất cả kiện chưa nhận
                targets = shipmentParcels
                    .Where(p => !string.Equals(p.PackageStatus, "RECEIVED_AT_DESTINATION", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (targets.Count == 0)
                {
                    throw new ArgumentException("Tất cả kiện trong lô đã được xác nhận nhận tại kho đích.");
                }
            }

            // 6. Cập nhật trạng thái Parcel + thời gian nhận
            var now = DateTime.UtcNow;
            foreach (var parcel in targets)
            {
                parcel.PackageStatus = "RECEIVED_AT_DESTINATION";
                parcel.CheckInTime = now;
                parcelRepo.Update(parcel);
            }

            // 7. Lưu lịch sử nhận kiện tại kho đích
            await _unitOfWork.Repository<Tracking>().AddAsync(new Tracking
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                TrackingStatus = "RECEIVED_AT_DESTINATION",
                TrackingStage = "DESTINATION_RECEIVING",
                Note = request?.Note,
                TrackingTime = now
            });

            await _unitOfWork.SaveChangesAsync();

            var destinationWarehouse = shipment.DestinationWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(shipment.DestinationWarehouseId.Value)
                : null;
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);

            return new DestinationReceivingResponseDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode ?? string.Empty,
                DestinationWarehouseId = shipment.DestinationWarehouseId,
                DestinationWarehouseName = destinationWarehouse?.WarehouseName ?? string.Empty,
                ReceivedAt = now,
                ReceivedByName = user?.FullName ?? string.Empty,
                Note = request?.Note,
                ReceivedCount = targets.Count,
                Parcels = targets.Select(p => new DestinationReceivingParcelDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    PackageStatus = p.PackageStatus ?? string.Empty
                }).ToList()
            };
        }
    }
}
