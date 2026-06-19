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
    public class PricingRuleService : IPricingRuleService
    {
        private readonly IUnitOfWork _unitOfWork;

        public PricingRuleService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IEnumerable<PricingRuleDto>> GetAllAsync()
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var rules = await pricingRepo.GetAllAsync();

            return rules.Select(r => new PricingRuleDto
            {
                Id = r.Id,
                ShippingServiceType = r.ShippingServiceType,
                ConsignmentType = r.ConsignmentType,
                Route = r.Route,
                UnitType = r.UnitType,
                PricePerWeight = r.PricePerWeight,
                PricePerVolume = r.PricePerVolume,
                ServiceFee = r.ServiceFee,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt
            });
        }

        public async Task<PricingRuleDto> GetByIdAsync(Guid id)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var r = await pricingRepo.GetByIdAsync(id);

            if (r == null)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình giá với ID: {id}");
            }

            return new PricingRuleDto
            {
                Id = r.Id,
                ShippingServiceType = r.ShippingServiceType,
                ConsignmentType = r.ConsignmentType,
                Route = r.Route,
                UnitType = r.UnitType,
                PricePerWeight = r.PricePerWeight,
                PricePerVolume = r.PricePerVolume,
                ServiceFee = r.ServiceFee,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt
            };
        }

        public async Task<PricingRuleDto> CreateAsync(CreatePricingRuleRequest request)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRepo.GetAllAsync();

            // Check duplicate pricing rule
            var isDuplicate = allRules.Any(r =>
                string.Equals(r.ShippingServiceType?.Trim(), request.ShippingServiceType.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ConsignmentType?.Trim(), request.ConsignmentType.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Route?.Trim() ?? string.Empty, request.Route?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException("Không cho tạo trùng cấu hình giá cùng loại dịch vụ, loại ký gửi và tuyến vận chuyển.");
            }

            var newRule = new PricingRule
            {
                Id = Guid.NewGuid(),
                ShippingServiceType = request.ShippingServiceType.Trim(),
                ConsignmentType = request.ConsignmentType.Trim(),
                Route = request.Route?.Trim(),
                UnitType = request.UnitType?.Trim(),
                PricePerWeight = request.PricePerWeight,
                PricePerVolume = request.PricePerVolume,
                ServiceFee = request.ServiceFee,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            await pricingRepo.AddAsync(newRule);
            await _unitOfWork.SaveChangesAsync();

            return new PricingRuleDto
            {
                Id = newRule.Id,
                ShippingServiceType = newRule.ShippingServiceType,
                ConsignmentType = newRule.ConsignmentType,
                Route = newRule.Route,
                UnitType = newRule.UnitType,
                PricePerWeight = newRule.PricePerWeight,
                PricePerVolume = newRule.PricePerVolume,
                ServiceFee = newRule.ServiceFee,
                IsActive = newRule.IsActive,
                CreatedAt = newRule.CreatedAt
            };
        }

        public async Task UpdateAsync(Guid id, UpdatePricingRuleRequest request)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var r = await pricingRepo.GetByIdAsync(id);

            if (r == null)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình giá với ID: {id}");
            }

            var allRules = await pricingRepo.GetAllAsync();

            // Check duplicate
            var isDuplicate = allRules.Any(x =>
                x.Id != id &&
                string.Equals(x.ShippingServiceType?.Trim(), request.ShippingServiceType.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ConsignmentType?.Trim(), request.ConsignmentType.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Route?.Trim() ?? string.Empty, request.Route?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException("Cấu hình giá đã tồn tại cùng loại dịch vụ, loại ký gửi và tuyến vận chuyển.");
            }

            r.ShippingServiceType = request.ShippingServiceType.Trim();
            r.ConsignmentType = request.ConsignmentType.Trim();
            r.Route = request.Route?.Trim();
            r.UnitType = request.UnitType?.Trim();
            r.PricePerWeight = request.PricePerWeight;
            r.PricePerVolume = request.PricePerVolume;
            r.ServiceFee = request.ServiceFee;
            r.IsActive = request.IsActive;

            pricingRepo.Update(r);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var r = await pricingRepo.GetByIdAsync(id);

            if (r == null)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình giá với ID: {id}");
            }

            pricingRepo.Delete(r);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
