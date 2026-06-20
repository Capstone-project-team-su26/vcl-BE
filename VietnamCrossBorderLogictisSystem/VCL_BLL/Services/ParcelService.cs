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
                PackageStatus = "RECEIVED"
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
    }
}
