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

        public InternationalShipmentService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
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

                // Chỉ nhận yêu cầu đã RELEASED
                if (!string.Equals(wro.Status, "RELEASED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Yêu cầu xuất kho {wro.WroCode ?? wroId.ToString()} chưa đủ điều kiện (phải ở trạng thái RELEASED).");
                }

                // Không cho gán cùng một yêu cầu xuất kho vào nhiều lô vận chuyển
                if (allShipmentWros.Any(sw => sw.WroRequestId == wroId))
                {
                    throw new ArgumentException($"Yêu cầu xuất kho {wro.WroCode ?? wroId.ToString()} đã thuộc một lô vận chuyển khác.");
                }

                wroRequests.Add(wro);
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
                TotalWeight = totalWeight
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
    }
}
