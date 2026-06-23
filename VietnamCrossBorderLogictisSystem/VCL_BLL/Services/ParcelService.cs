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
    public class ParcelService : IParcelService
    {
        private readonly IUnitOfWork _unitOfWork;
        private const decimal VolumetricDivisor = 5000m;

        public ParcelService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<ParcelCheckInResponse> CheckInParcelAsync(ParcelCheckInRequest request)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu check-in không được để trống.");

            // 1. Kiểm tra mã gửi hàng hợp lệ (bắt đầu bằng VCL-)
            if (string.IsNullOrWhiteSpace(request.ShippingCode) || 
                !request.ShippingCode.StartsWith("VCL-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Mã gửi hàng không hợp lệ.");
            }

            // 2. Tìm yêu cầu ký gửi
            var orderRepo = _unitOfWork.Repository<Order>();
            var allOrders = await orderRepo.GetAllAsync();
            var order = allOrders.FirstOrDefault(o => 
                string.Equals(o.ConsignmentCode, request.ShippingCode, StringComparison.OrdinalIgnoreCase));

            if (order == null)
            {
                throw new NotFoundException("Không tìm thấy yêu cầu ký gửi.");
            }

            // 3. Chỉ cho check-in yêu cầu ở trạng thái APPROVED
            if (!string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Yêu cầu ký gửi không ở trạng thái APPROVED.");
            }

            if (request.ActualWeight <= 0)
            {
                throw new ArgumentException("Cân nặng thực tế phải lớn hơn 0.");
            }

            // 4. Phát sinh mã kiện hàng không trùng
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var allParcels = await parcelRepo.GetAllAsync();
            var random = new Random();
            string packageCode;
            do
            {
                var now = DateTime.UtcNow;
                var randomSuffix = random.Next(100000, 999999).ToString();
                packageCode = $"PCL-{now:yyyyMMddHHmmss}-{randomSuffix}";
            } while (allParcels.Any(p => string.Equals(p.PackageCode, packageCode, StringComparison.OrdinalIgnoreCase)));

            // 5. Tính toán volumetric weight nếu đủ 3 kích thước
            decimal? volumetricWeight = null;
            if (request.Length.HasValue && request.Width.HasValue && request.Height.HasValue &&
                request.Length.Value > 0 && request.Width.Value > 0 && request.Height.Value > 0)
            {
                volumetricWeight = (request.Length.Value * request.Width.Value * request.Height.Value) / VolumetricDivisor;
            }

            // 6. Tính chargeable weight
            decimal chargeableWeight = request.ActualWeight;
            if (volumetricWeight.HasValue)
            {
                chargeableWeight = Math.Max(request.ActualWeight, volumetricWeight.Value);
            }

            // 7. Tạo Parcel mới và cập nhật trạng thái Parcel thành RECEIVED
            var parcel = new Parcel
            {
                Id = Guid.NewGuid(),
                ConsignmentId = order.Id,
                PackageCode = packageCode,
                ActualWeight = request.ActualWeight,
                Length = request.Length,
                Width = request.Width,
                Height = request.Height,
                VolumetricWeight = volumetricWeight,
                ChargeableWeight = chargeableWeight,
                PackageStatus = "RECEIVED",
                CheckInTime = DateTime.UtcNow
            };

            await parcelRepo.AddAsync(parcel);

            // 8. Cập nhật trạng thái Order thành WAREHOUSE_RECEIVED
            order.Status = "WAREHOUSE_RECEIVED";
            orderRepo.Update(order);

            // 9. Lấy thông tin khách hàng và danh sách mặt hàng đã khai báo để trả về
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.Id == order.CustomerId);

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == order.Id).ToList();

            await _unitOfWork.SaveChangesAsync();

            // 10. Trả về thông tin check-in đầy đủ
            var response = new ParcelCheckInResponse
            {
                ShippingCode = request.ShippingCode,
                ConsignmentId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                CustomerName = customer?.FullName ?? string.Empty,
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                PackageCode = packageCode,
                ActualWeight = request.ActualWeight,
                Length = request.Length,
                Width = request.Width,
                Height = request.Height,
                VolumetricWeight = volumetricWeight.HasValue ? Math.Round(volumetricWeight.Value, 2) : null,
                ChargeableWeight = Math.Round(chargeableWeight, 2),
                PackageStatus = parcel.PackageStatus,
                DeclaredItems = orderItems.Select(item => new DeclaredItemDto
                {
                    ProductName = item.ProductName ?? string.Empty,
                    ProductType = item.ProductType ?? string.Empty,
                    Quantity = item.Quantity ?? 0,
                    Weight = item.Weight,
                    Length = item.Length,
                    Width = item.Width,
                    Height = item.Height,
                    DeclaredValue = item.DeclaredValue
                }).ToList()
            };

            return response;
        }

        public async Task<ParcelDetailResponse> GetParcelDetailAsync(Guid parcelId)
        {
            // 1. Tìm Parcel
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(parcelId);
            if (parcel == null)
                throw new NotFoundException("Không tìm thấy kiện hàng.");

            // 2. Yêu cầu ký gửi + khách hàng liên quan
            Order? order = null;
            if (parcel.ConsignmentId.HasValue)
                order = await _unitOfWork.Repository<Order>().GetByIdAsync(parcel.ConsignmentId.Value);

            Customer? customer = null;
            if (order?.CustomerId != null)
                customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value);

            // 3. Vị trí kho (chỉ khi đã put-away — BinId != null)
            ParcelStorageLocation? location = null;
            if (parcel.BinId.HasValue)
            {
                var bin = await _unitOfWork.Repository<Bin>().GetByIdAsync(parcel.BinId.Value);
                if (bin != null)
                {
                    Shelf? shelf = bin.ShelfId.HasValue
                        ? await _unitOfWork.Repository<Shelf>().GetByIdAsync(bin.ShelfId.Value) : null;
                    Warehouse? warehouse = bin.WarehouseId.HasValue
                        ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(bin.WarehouseId.Value) : null;
                    WarehouseZone? zone = shelf?.ZoneId != null
                        ? await _unitOfWork.Repository<WarehouseZone>().GetByIdAsync(shelf.ZoneId.Value) : null;

                    location = new ParcelStorageLocation
                    {
                        WarehouseName = warehouse?.WarehouseName ?? string.Empty,
                        ZoneName = zone?.ZoneName ?? string.Empty,
                        ShelfCode = shelf?.ShelfCode ?? string.Empty,
                        BinCode = bin.BinCode ?? string.Empty
                    };
                }
            }

            // 4. Map response
            return new ParcelDetailResponse
            {
                ParcelId = parcel.Id,
                PackageCode = parcel.PackageCode ?? string.Empty,
                PackageStatus = parcel.PackageStatus ?? string.Empty,
                ConsignmentId = parcel.ConsignmentId,
                ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                CustomerName = customer?.FullName ?? string.Empty,
                CustomerCode = customer?.CustomerCode ?? string.Empty,
                CustomerEmail = customer?.Email,
                CustomerPhone = customer?.Phone,
                ActualWeight = parcel.ActualWeight,
                Length = parcel.Length,
                Width = parcel.Width,
                Height = parcel.Height,
                VolumetricWeight = parcel.VolumetricWeight.HasValue ? Math.Round(parcel.VolumetricWeight.Value, 2) : null,
                ChargeableWeight = parcel.ChargeableWeight.HasValue ? Math.Round(parcel.ChargeableWeight.Value, 2) : null,
                CheckInTime = parcel.CheckInTime,
                PutAwayTime = parcel.PutAwayTime,
                StorageLocation = location
            };
        }

        public async Task<PaginatedResponse<PendingPutawayParcelDto>> GetPendingPutawayParcelsAsync(
            int pageNumber, 
            int pageSize, 
            string? search, 
            string? status)
        {
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var orderRepo = _unitOfWork.Repository<Order>();
            var customerRepo = _unitOfWork.Repository<Customer>();

            // Get all data into memory - suitable for GenericRepository pattern
            var allParcels = await parcelRepo.GetAllAsync();
            var allOrders = await orderRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();

            var query = allParcels.Where(p => p.BinId == null).AsQueryable();

            // Filter by status if provided
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => string.Equals(p.PackageStatus, status, StringComparison.OrdinalIgnoreCase));
            }

            // Search by package code or consignment code
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p => 
                    (p.PackageCode != null && p.PackageCode.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (p.ConsignmentId.HasValue && 
                     allOrders.Any(o => o.Id == p.ConsignmentId.Value && 
                                       o.ConsignmentCode != null && 
                                       o.ConsignmentCode.Contains(search, StringComparison.OrdinalIgnoreCase))));
            }

            var totalCount = query.Count();

            // Pagination - order by CheckInTime descending
            var paginatedParcels = query
                .OrderByDescending(p => p.CheckInTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Map to DTO
            var resultItems = paginatedParcels.Select(p =>
            {
                var order = allOrders.FirstOrDefault(o => o.Id == p.ConsignmentId);
                var customer = order != null ? allCustomers.FirstOrDefault(c => c.Id == order.CustomerId) : null;

                return new PendingPutawayParcelDto
                {
                    Id = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    CustomerName = customer?.FullName ?? string.Empty,
                    ActualWeight = p.ActualWeight,
                    Length = p.Length,
                    Width = p.Width,
                    Height = p.Height,
                    VolumetricWeight = p.VolumetricWeight.HasValue ? Math.Round(p.VolumetricWeight.Value, 2) : null,
                    ChargeableWeight = p.ChargeableWeight.HasValue ? Math.Round(p.ChargeableWeight.Value, 2) : null,
                    PackageStatus = p.PackageStatus ?? string.Empty,
                    CheckInTime = p.CheckInTime
                };
            }).ToList();

            return new PaginatedResponse<PendingPutawayParcelDto>
            {
                Items = resultItems,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<ParcelPutAwayResponse> PutAwayParcelAsync(Guid parcelId, ParcelPutAwayRequest request)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu nhập vị trí kho không được để trống.");

            // 1. Tìm Parcel theo parcelId
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcel = await parcelRepo.GetByIdAsync(parcelId);
            if (parcel == null)
            {
                throw new NotFoundException("Không tìm thấy kiện hàng.");
            }

            // 2. Không cho nhập vị trí lại nếu Parcel đã STORED
            if (string.Equals(parcel.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Kiện hàng đã được nhập vị trí kho (STORED).");
            }

            // 3. Chỉ cho nhập vị trí kho với Parcel đã check-in (RECEIVED)
            if (!string.Equals(parcel.PackageStatus, "RECEIVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Chỉ cho phép nhập vị trí kho với kiện hàng đã check-in.");
            }

            // 4. Tìm Warehouse, Zone, Shelf, Bin
            var warehouseRepo = _unitOfWork.Repository<Warehouse>();
            var warehouse = await warehouseRepo.GetByIdAsync(request.WarehouseId);
            if (warehouse == null)
            {
                throw new NotFoundException("Không tìm thấy kho hàng.");
            }

            var zoneRepo = _unitOfWork.Repository<WarehouseZone>();
            var zone = await zoneRepo.GetByIdAsync(request.ZoneId);
            if (zone == null)
            {
                throw new NotFoundException("Không tìm thấy khu vực kho (Zone).");
            }

            var shelfRepo = _unitOfWork.Repository<Shelf>();
            var shelf = await shelfRepo.GetByIdAsync(request.ShelfId);
            if (shelf == null)
            {
                throw new NotFoundException("Không tìm thấy kệ kho (Shelf).");
            }

            var binRepo = _unitOfWork.Repository<Bin>();
            var bin = await binRepo.GetByIdAsync(request.BinId);
            if (bin == null)
            {
                throw new NotFoundException("Không tìm thấy ô kệ (Bin).");
            }

            // 5. Kiểm tra tính hợp lệ của mối quan hệ vị trí kho (hierarchy validation)
            if (zone.WarehouseId != warehouse.Id)
            {
                throw new ArgumentException("Khu vực kho không thuộc kho hàng đã chọn.");
            }

            if (shelf.ZoneId != zone.Id)
            {
                throw new ArgumentException("Kệ kho không thuộc khu vực kho đã chọn.");
            }

            if (bin.ShelfId != shelf.Id)
            {
                throw new ArgumentException("Ô kệ không thuộc kệ kho đã chọn.");
            }

            if (bin.WarehouseId != warehouse.Id)
            {
                throw new ArgumentException("Ô kệ không thuộc kho hàng đã chọn.");
            }

            // 6. Kiểm tra sức chứa của Bin trước khi gán
            // Tính thể tích của parcel: Length * Width * Height
            decimal parcelVolume = (parcel.Length ?? 0) * (parcel.Width ?? 0) * (parcel.Height ?? 0);
            if (bin.MaxVolume.HasValue)
            {
                decimal availableVolume = bin.MaxVolume.Value - (bin.CurrentVolume ?? 0);
                if (parcelVolume > availableVolume)
                {
                    throw new ArgumentException($"Ô kệ không đủ sức chứa thể tích. Thể tích kiện hàng: {parcelVolume}, thể tích khả dụng: {availableVolume}.");
                }
            }

            // Tính cân nặng của parcel: ActualWeight
            if (bin.MaxWeight.HasValue)
            {
                var allParcels = await parcelRepo.GetAllAsync();
                var currentWeightInBin = allParcels
                    .Where(p => p.BinId == bin.Id && string.Equals(p.PackageStatus, "STORED", StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.ActualWeight ?? 0);

                decimal availableWeight = bin.MaxWeight.Value - currentWeightInBin;
                decimal parcelWeight = parcel.ActualWeight ?? 0;
                if (parcelWeight > availableWeight)
                {
                    throw new ArgumentException($"Ô kệ không đủ sức chứa tải trọng. Khối lượng kiện hàng: {parcelWeight}, tải trọng khả dụng: {availableWeight}.");
                }
            }

            // 7. Cập nhật thông tin vị trí lưu trữ và trạng thái của Parcel
            var putAwayTime = DateTime.UtcNow;
            parcel.BinId = bin.Id;
            parcel.PackageStatus = "STORED";
            parcel.PutAwayTime = putAwayTime;

            // Cập nhật thể tích hiện tại của Bin
            bin.CurrentVolume = (bin.CurrentVolume ?? 0) + parcelVolume;

            parcelRepo.Update(parcel);
            binRepo.Update(bin);

            await _unitOfWork.SaveChangesAsync();

            // 8. Trả về thông tin kết quả
            return new ParcelPutAwayResponse
            {
                PackageCode = parcel.PackageCode ?? string.Empty,
                WarehouseName = warehouse.WarehouseName ?? string.Empty,
                ZoneName = zone.ZoneName ?? string.Empty,
                ShelfCode = shelf.ShelfCode ?? string.Empty,
                BinCode = bin.BinCode ?? string.Empty,
                PackageStatus = parcel.PackageStatus,
                PutAwayTime = putAwayTime
            };
        }
    }
}
