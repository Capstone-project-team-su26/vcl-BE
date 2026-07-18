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
            var pricings = (await pricingRepo.GetAllAsync()).Where(p => !p.IsDeleted).ToList();

            var rulesRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await rulesRepo.GetAllAsync();
            var activeRules = allRules.Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)).ToList();

            return pricings.Select(p => 
            {
                var associatedRules = activeRules
                    .Where(r => r.ServicePricingId == p.Id)
                    .Where(IsBoxRule)
                    .Select(r => new PricingRuleDto
                    {
                        Id = r.Id,
                        ServicePricingId = r.ServicePricingId,
                        RuleName = r.RuleName,
                        RuleCode = r.RuleCode,
                        RuleType = r.RuleType,
                        ConditionType = r.ConditionType,
                        ConditionValue = r.ConditionValue,
                        CalculationType = r.CalculationType,
                        Value = r.Value,
                        MinAmount = r.MinAmount,
                        MaxAmount = r.MaxAmount,
                        IsRequired = r.IsRequired,
                        Status = r.Status,
                        Description = r.Description,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt
                    })
                    .ToList();

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
                    EffectiveDate = p.EffectiveDate,
                    BoxPricingRules = associatedRules
                };
            });
        }

        public async Task<ServicePricingDto> GetByIdAsync(Guid id)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var p = await pricingRepo.GetByIdAsync(id);

            if (p == null || p.IsDeleted)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình bảng giá dịch vụ với ID: {id}");
            }

            var rulesRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await rulesRepo.GetAllAsync();
            var associatedRules = allRules
                .Where(r => r.ServicePricingId == id && string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .Where(IsBoxRule)
                .Select(r => new PricingRuleDto
                {
                    Id = r.Id,
                    ServicePricingId = r.ServicePricingId,
                    RuleName = r.RuleName,
                    RuleCode = r.RuleCode,
                    RuleType = r.RuleType,
                    ConditionType = r.ConditionType,
                    ConditionValue = r.ConditionValue,
                    CalculationType = r.CalculationType,
                    Value = r.Value,
                    MinAmount = r.MinAmount,
                    MaxAmount = r.MaxAmount,
                    IsRequired = r.IsRequired,
                    Status = r.Status,
                    Description = r.Description,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                })
                .ToList();

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
                EffectiveDate = p.EffectiveDate,
                BoxPricingRules = associatedRules
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
                EffectiveDate = newPricing.EffectiveDate,
                BoxPricingRules = new List<PricingRuleDto>()
            };
        }

        public async Task UpdateAsync(Guid id, UpdateServicePricingRequest request)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var p = await pricingRepo.GetByIdAsync(id);

            if (p == null || p.IsDeleted)
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

            if (p == null || p.IsDeleted)
            {
                throw new NotFoundException($"Không tìm thấy cấu hình bảng giá dịch vụ với ID: {id}");
            }

            p.IsDeleted = true;
            pricingRepo.Update(p);
            await _unitOfWork.SaveChangesAsync();
        }

        private bool IsBoxRule(PricingRule rule)
        {
            if (rule == null) return false;
            var code = rule.RuleCode?.ToUpperInvariant() ?? string.Empty;
            var name = rule.RuleName?.ToUpperInvariant() ?? string.Empty;
            var ruleType = rule.RuleType?.ToUpperInvariant() ?? string.Empty;
            var condType = rule.ConditionType?.ToUpperInvariant() ?? string.Empty;

            return ruleType.Contains("BOX") || ruleType.Contains("CRATE") ||
                   condType.Contains("BOX") ||
                   code.Contains("SMALL") || code.Contains("MEDIUM") || code.Contains("LARGE") || code.Contains("BUBBLE") ||
                   name.Contains("SMALL") || name.Contains("MEDIUM") || name.Contains("LARGE") || name.Contains("BUBBLE") ||
                   name.Contains("NHỎ") || name.Contains("VỪA") || name.Contains("LỚN") || name.Contains("XỐP HƠI") || name.Contains("BÓP NỔ") || name.Contains("MÀNG XỐP") || name.Contains("THÙNG GỖ") || name.Contains("ĐÔNG GỖ") || name.Contains("ĐÓNG GỖ");
        }
    }
}
