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
    public class ShippingRouteService : IShippingRouteService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ShippingRouteService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<List<ShippingRouteDto>> GetAllAsync(string? transportMode, bool? isActive)
        {
            var all = await _unitOfWork.Repository<ShippingRoute>().GetAllAsync();
            var query = all.AsQueryable();

            if (!string.IsNullOrWhiteSpace(transportMode))
            {
                query = query.Where(r => string.Equals(r.TransportMode, transportMode, StringComparison.OrdinalIgnoreCase));
            }

            if (isActive.HasValue)
            {
                query = query.Where(r => r.IsActive == isActive.Value);
            }

            return await BuildDtosAsync(query.OrderBy(r => r.RouteName).ToList());
        }

        public async Task<ShippingRouteDto> GetByIdAsync(Guid id)
        {
            var route = await _unitOfWork.Repository<ShippingRoute>().GetByIdAsync(id);
            if (route == null)
            {
                throw new NotFoundException($"Không tìm thấy tuyến vận chuyển với ID: {id}");
            }

            var dtos = await BuildDtosAsync(new List<ShippingRoute> { route });
            return dtos.First();
        }

        public async Task<ShippingRouteDto> CreateAsync(CreateShippingRouteRequest request)
        {
            var repo = _unitOfWork.Repository<ShippingRoute>();
            var all = await repo.GetAllAsync();

            var code = request.RouteCode.Trim();
            if (all.Any(r => string.Equals(r.RouteCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã tuyến \"{code}\" đã tồn tại.");
            }

            await ValidateReferencesAsync(request);

            var route = new ShippingRoute
            {
                Id = Guid.NewGuid(),
                RouteCode = code,
                RouteName = request.RouteName.Trim(),
                OriginCountry = request.OriginCountry.Trim(),
                OriginWarehouseId = request.OriginWarehouseId,
                DestinationCountry = request.DestinationCountry.Trim(),
                DestinationWarehouseId = request.DestinationWarehouseId,
                TransportMode = request.TransportMode.Trim().ToUpperInvariant(),
                CarrierId = request.CarrierId,
                SupplierId = request.SupplierId,
                EstimatedTransitDays = request.EstimatedTransitDays,
                Note = request.Note?.Trim() ?? string.Empty,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await repo.AddAsync(route);
            await _unitOfWork.SaveChangesAsync();

            var dtos = await BuildDtosAsync(new List<ShippingRoute> { route });
            return dtos.First();
        }

        public async Task UpdateAsync(Guid id, UpdateShippingRouteRequest request)
        {
            var repo = _unitOfWork.Repository<ShippingRoute>();
            var route = await repo.GetByIdAsync(id);
            if (route == null)
            {
                throw new NotFoundException($"Không tìm thấy tuyến vận chuyển với ID: {id}");
            }

            var all = await repo.GetAllAsync();
            var code = request.RouteCode.Trim();
            if (all.Any(r => r.Id != id && string.Equals(r.RouteCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Mã tuyến \"{code}\" đã tồn tại.");
            }

            await ValidateReferencesAsync(request);

            route.RouteCode = code;
            route.RouteName = request.RouteName.Trim();
            route.OriginCountry = request.OriginCountry.Trim();
            route.OriginWarehouseId = request.OriginWarehouseId;
            route.DestinationCountry = request.DestinationCountry.Trim();
            route.DestinationWarehouseId = request.DestinationWarehouseId;
            route.TransportMode = request.TransportMode.Trim().ToUpperInvariant();
            route.CarrierId = request.CarrierId;
            route.SupplierId = request.SupplierId;
            route.EstimatedTransitDays = request.EstimatedTransitDays;
            route.Note = request.Note?.Trim() ?? string.Empty;
            route.IsActive = request.IsActive;
            route.UpdatedAt = DateTime.UtcNow;

            repo.Update(route);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var repo = _unitOfWork.Repository<ShippingRoute>();
            var route = await repo.GetByIdAsync(id);
            if (route == null)
            {
                throw new NotFoundException($"Không tìm thấy tuyến vận chuyển với ID: {id}");
            }

            repo.Delete(route);
            await _unitOfWork.SaveChangesAsync();
        }

        /// <summary>
        /// Kiểm tra kho, đơn vị vận chuyển và nhà cung cấp được tham chiếu có tồn tại hay không.
        /// </summary>
        private async Task ValidateReferencesAsync(CreateShippingRouteRequest request)
        {
            if (request.OriginWarehouseId.HasValue)
            {
                var originWarehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(request.OriginWarehouseId.Value);
                if (originWarehouse == null)
                {
                    throw new ArgumentException("Không tìm thấy kho đi được chọn.");
                }
            }

            if (request.DestinationWarehouseId.HasValue)
            {
                var destinationWarehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(request.DestinationWarehouseId.Value);
                if (destinationWarehouse == null)
                {
                    throw new ArgumentException("Không tìm thấy kho đến được chọn.");
                }
            }

            if (request.OriginWarehouseId.HasValue
                && request.DestinationWarehouseId.HasValue
                && request.OriginWarehouseId.Value == request.DestinationWarehouseId.Value)
            {
                throw new ArgumentException("Kho đi và kho đến phải khác nhau.");
            }

            if (request.CarrierId.HasValue)
            {
                var carrier = await _unitOfWork.Repository<Carrier>().GetByIdAsync(request.CarrierId.Value);
                if (carrier == null)
                {
                    throw new ArgumentException("Không tìm thấy đơn vị vận chuyển được chọn.");
                }
            }

            if (request.SupplierId.HasValue)
            {
                var supplier = await _unitOfWork.Repository<Supplier>().GetByIdAsync(request.SupplierId.Value);
                if (supplier == null)
                {
                    throw new ArgumentException("Không tìm thấy nhà cung cấp được chọn.");
                }
            }
        }

        /// <summary>
        /// Bổ sung tên kho, tên đơn vị vận chuyển và tên nhà cung cấp cho danh sách tuyến.
        /// </summary>
        private async Task<List<ShippingRouteDto>> BuildDtosAsync(List<ShippingRoute> routes)
        {
            if (routes == null || routes.Count == 0)
            {
                return new List<ShippingRouteDto>();
            }

            var warehouseIds = routes.Select(r => r.OriginWarehouseId)
                .Concat(routes.Select(r => r.DestinationWarehouseId))
                .OfType<Guid>()
                .ToHashSet();

            var warehouses = warehouseIds.Count == 0
                ? new Dictionary<Guid, Warehouse>()
                : (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                    .Where(w => warehouseIds.Contains(w.Id))
                    .ToDictionary(w => w.Id);

            var carrierIds = routes.Select(r => r.CarrierId).OfType<Guid>().ToHashSet();
            var carriers = carrierIds.Count == 0
                ? new Dictionary<Guid, Carrier>()
                : (await _unitOfWork.Repository<Carrier>().GetAllAsync())
                    .Where(c => carrierIds.Contains(c.Id))
                    .ToDictionary(c => c.Id);

            var supplierIds = routes.Select(r => r.SupplierId).OfType<Guid>().ToHashSet();
            var suppliers = supplierIds.Count == 0
                ? new Dictionary<Guid, Supplier>()
                : (await _unitOfWork.Repository<Supplier>().GetAllAsync())
                    .Where(s => supplierIds.Contains(s.Id))
                    .ToDictionary(s => s.Id);

            var result = new List<ShippingRouteDto>();

            foreach (var route in routes)
            {
                Warehouse? originWarehouse = null;
                if (route.OriginWarehouseId is Guid originId)
                {
                    warehouses.TryGetValue(originId, out originWarehouse);
                }

                Warehouse? destinationWarehouse = null;
                if (route.DestinationWarehouseId is Guid destinationId)
                {
                    warehouses.TryGetValue(destinationId, out destinationWarehouse);
                }

                Carrier? carrier = null;
                if (route.CarrierId is Guid carrierId)
                {
                    carriers.TryGetValue(carrierId, out carrier);
                }

                Supplier? supplier = null;
                if (route.SupplierId is Guid supplierId)
                {
                    suppliers.TryGetValue(supplierId, out supplier);
                }

                result.Add(new ShippingRouteDto
                {
                    Id = route.Id,
                    RouteCode = route.RouteCode ?? string.Empty,
                    RouteName = route.RouteName ?? string.Empty,
                    OriginCountry = route.OriginCountry ?? string.Empty,
                    OriginWarehouseId = route.OriginWarehouseId,
                    OriginWarehouseName = originWarehouse?.WarehouseName ?? string.Empty,
                    DestinationCountry = route.DestinationCountry ?? string.Empty,
                    DestinationWarehouseId = route.DestinationWarehouseId,
                    DestinationWarehouseName = destinationWarehouse?.WarehouseName ?? string.Empty,
                    TransportMode = route.TransportMode ?? string.Empty,
                    CarrierId = route.CarrierId,
                    CarrierName = carrier?.CarrierName ?? string.Empty,
                    SupplierId = route.SupplierId,
                    SupplierName = supplier?.SupplierName ?? string.Empty,
                    EstimatedTransitDays = route.EstimatedTransitDays,
                    Note = route.Note ?? string.Empty,
                    IsActive = route.IsActive,
                    CreatedAt = route.CreatedAt
                });
            }

            return result;
        }
    }
}
