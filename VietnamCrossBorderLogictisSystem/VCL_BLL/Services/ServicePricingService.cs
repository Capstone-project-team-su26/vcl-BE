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
    public class ServicePricingService : IServicePricingService
    {
        private readonly IUnitOfWork _unitOfWork;

        public ServicePricingService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<ServicePricingDto>> GetAllAsync()
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var pricings = await pricingRepo.GetAllAsync();

            return pricings.Select(p => new ServicePricingDto
            {
                Id = p.Id,
                CarrierId = p.CarrierId,
                ServiceType = p.ServiceType,
                OriginCountry = p.OriginCountry,
                DestinationCountry = p.DestinationCountry,
                UnitType = p.UnitType,
                Price = p.Price,
                Currency = p.Currency,
                EffectiveDate = p.EffectiveDate
            });
        }

        public async Task<ServicePricingDto> GetByIdAsync(Guid id)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var p = await pricingRepo.GetByIdAsync(id);

            if (p == null)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình bảng giá dịch vụ với ID: {id}");
            }

            return new ServicePricingDto
            {
                Id = p.Id,
                CarrierId = p.CarrierId,
                ServiceType = p.ServiceType,
                OriginCountry = p.OriginCountry,
                DestinationCountry = p.DestinationCountry,
                UnitType = p.UnitType,
                Price = p.Price,
                Currency = p.Currency,
                EffectiveDate = p.EffectiveDate
            };
        }

        public async Task<ServicePricingDto> CreateAsync(CreateServicePricingRequest request)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();

            // Validate carrier if carrierId is provided
            if (request.CarrierId.HasValue)
            {
                var carrierRepo = _unitOfWork.Repository<Carrier>();
                var carrier = await carrierRepo.GetByIdAsync(request.CarrierId.Value);
                if (carrier == null)
                {
                    throw new NotFoundException($"Không tìm thấy nhà vận chuyển với ID: {request.CarrierId.Value}");
                }
            }

            var newPricing = new ServicePricing
            {
                Id = Guid.NewGuid(),
                CarrierId = request.CarrierId,
                ServiceType = request.ServiceType.Trim(),
                OriginCountry = request.OriginCountry.Trim(),
                DestinationCountry = request.DestinationCountry.Trim(),
                UnitType = request.UnitType.Trim(),
                Price = request.Price,
                Currency = request.Currency.Trim(),
                EffectiveDate = request.EffectiveDate ?? DateTime.UtcNow
            };

            await pricingRepo.AddAsync(newPricing);
            await _unitOfWork.SaveChangesAsync();

            return new ServicePricingDto
            {
                Id = newPricing.Id,
                CarrierId = newPricing.CarrierId,
                ServiceType = newPricing.ServiceType,
                OriginCountry = newPricing.OriginCountry,
                DestinationCountry = newPricing.DestinationCountry,
                UnitType = newPricing.UnitType,
                Price = newPricing.Price,
                Currency = newPricing.Currency,
                EffectiveDate = newPricing.EffectiveDate
            };
        }

        public async Task UpdateAsync(Guid id, UpdateServicePricingRequest request)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var p = await pricingRepo.GetByIdAsync(id);

            if (p == null)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình bảng giá dịch vụ với ID: {id}");
            }

            if (request.CarrierId.HasValue)
            {
                var carrierRepo = _unitOfWork.Repository<Carrier>();
                var carrier = await carrierRepo.GetByIdAsync(request.CarrierId.Value);
                if (carrier == null)
                {
                    throw new NotFoundException($"Không tìm thấy nhà vận chuyển với ID: {request.CarrierId.Value}");
                }
            }

            p.CarrierId = request.CarrierId;
            p.ServiceType = request.ServiceType.Trim();
            p.OriginCountry = request.OriginCountry.Trim();
            p.DestinationCountry = request.DestinationCountry.Trim();
            p.UnitType = request.UnitType.Trim();
            p.Price = request.Price;
            p.Currency = request.Currency.Trim();
            p.EffectiveDate = request.EffectiveDate;

            pricingRepo.Update(p);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var p = await pricingRepo.GetByIdAsync(id);

            if (p == null)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình bảng giá dịch vụ với ID: {id}");
            }

            try
            {
                pricingRepo.Delete(p);
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                throw new ArgumentException("Không thể xóa cấu hình bảng giá này vì đang có Báo giá (Quotations) hoặc Quy định giá (Pricing Rules) liên kết với nó.");
            }
        }
    }
}
