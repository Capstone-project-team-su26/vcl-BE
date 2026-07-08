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
    public class CarrierService : ICarrierService
    {
        private readonly IUnitOfWork _unitOfWork;

        public CarrierService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<CarrierDto>> GetAllAsync()
        {
            var carrierRepo = _unitOfWork.Repository<Carrier>();
            var carriers = await carrierRepo.GetAllAsync();

            return carriers.Select(MapToDto);
        }

        public async Task<IEnumerable<CarrierDto>> GetActiveAsync()
        {
            var carrierRepo = _unitOfWork.Repository<Carrier>();
            var carriers = await carrierRepo.GetAllAsync();

            return carriers.Where(c => c.IsActive).Select(MapToDto);
        }

        public async Task<CarrierDto> GetByIdAsync(Guid id)
        {
            var carrierRepo = _unitOfWork.Repository<Carrier>();
            var carrier = await carrierRepo.GetByIdAsync(id);

            if (carrier == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn vị vận chuyển với ID: {id}");
            }

            return MapToDto(carrier);
        }

        public async Task<CarrierDto> CreateAsync(CreateCarrierRequestDto request)
        {
            var carrierRepo = _unitOfWork.Repository<Carrier>();
            var allCarriers = await carrierRepo.GetAllAsync();

            // Check duplicate CarrierCode
            var isDuplicate = allCarriers.Any(c =>
                string.Equals(c.CarrierCode?.Trim(), request.CarrierCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException($"Mã đơn vị vận chuyển '{request.CarrierCode}' đã tồn tại trong hệ thống.");
            }

            var newCarrier = new Carrier
            {
                Id = Guid.NewGuid(),
                CarrierName = request.CarrierName.Trim(),
                CarrierCode = request.CarrierCode.Trim(),
                CarrierType = request.CarrierType.Trim(),
                ApiUrl = request.ApiUrl?.Trim(),
                ContactEmail = request.ContactEmail?.Trim(),
                ContactPhone = request.ContactPhone?.Trim(),
                SupportedShippingMethods = request.SupportedShippingMethods?.Trim(),
                SupportedRegions = request.SupportedRegions?.Trim(),
                IsActive = request.IsActive,
                InternalNotes = request.InternalNotes?.Trim()
            };

            await carrierRepo.AddAsync(newCarrier);
            await _unitOfWork.SaveChangesAsync();

            return MapToDto(newCarrier);
        }

        public async Task UpdateAsync(Guid id, UpdateCarrierRequestDto request)
        {
            var carrierRepo = _unitOfWork.Repository<Carrier>();
            var carrier = await carrierRepo.GetByIdAsync(id);

            if (carrier == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn vị vận chuyển với ID: {id}");
            }

            // Check duplicate CarrierCode (excluding itself)
            var allCarriers = await carrierRepo.GetAllAsync();
            var isDuplicate = allCarriers.Any(c =>
                c.Id != id &&
                string.Equals(c.CarrierCode?.Trim(), request.CarrierCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException($"Mã đơn vị vận chuyển '{request.CarrierCode}' đã được sử dụng bởi một đơn vị khác.");
            }

            carrier.CarrierName = request.CarrierName.Trim();
            carrier.CarrierCode = request.CarrierCode.Trim();
            carrier.CarrierType = request.CarrierType.Trim();
            carrier.ApiUrl = request.ApiUrl?.Trim();
            carrier.ContactEmail = request.ContactEmail?.Trim();
            carrier.ContactPhone = request.ContactPhone?.Trim();
            carrier.SupportedShippingMethods = request.SupportedShippingMethods?.Trim();
            carrier.SupportedRegions = request.SupportedRegions?.Trim();
            carrier.IsActive = request.IsActive;
            carrier.InternalNotes = request.InternalNotes?.Trim();

            carrierRepo.Update(carrier);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var carrierRepo = _unitOfWork.Repository<Carrier>();
            var carrier = await carrierRepo.GetByIdAsync(id);

            if (carrier == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn vị vận chuyển với ID: {id}");
            }

            // Check references in ServicePricing
            var servicePricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allServicePricings = await servicePricingRepo.GetAllAsync();
            var hasServicePricing = allServicePricings.Any(sp => sp.CarrierId == id);

            // Check references in Shipment
            var shipmentRepo = _unitOfWork.Repository<Shipment>();
            var allShipments = await shipmentRepo.GetAllAsync();
            var hasShipments = allShipments.Any(s => s.CarrierId == id);

            // Check references in ShipmentHandover
            var handoverRepo = _unitOfWork.Repository<ShipmentHandover>();
            var allHandovers = await handoverRepo.GetAllAsync();
            var hasHandovers = allHandovers.Any(sh => sh.FromCarrierId == id || sh.ToCarrierId == id);

            if (hasServicePricing || hasShipments || hasHandovers)
            {
                throw new ArgumentException("Không thể xóa đơn vị vận chuyển này vì đang được liên kết với các bảng giá dịch vụ hoặc lô hàng đang vận hành. Vui lòng vô hiệu hóa thay thế.");
            }

            carrierRepo.Delete(carrier);
            await _unitOfWork.SaveChangesAsync();
        }

        private CarrierDto MapToDto(Carrier carrier)
        {
            return new CarrierDto
            {
                Id = carrier.Id,
                CarrierName = carrier.CarrierName,
                CarrierCode = carrier.CarrierCode,
                CarrierType = carrier.CarrierType,
                ApiUrl = carrier.ApiUrl,
                ContactEmail = carrier.ContactEmail,
                ContactPhone = carrier.ContactPhone,
                SupportedShippingMethods = carrier.SupportedShippingMethods,
                SupportedRegions = carrier.SupportedRegions,
                IsActive = carrier.IsActive,
                InternalNotes = carrier.InternalNotes
            };
        }
    }
}
