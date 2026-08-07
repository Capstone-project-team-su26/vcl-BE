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

        /// <summary>
        /// Các rule chỉ dành cho luồng mua hộ — không được hiện ở danh sách dịch vụ bổ sung
        /// của đơn ký gửi. Nhận diện theo mã/loại rule thay vì ID để không phụ thuộc GUID seed,
        /// vì Id của rule lệch nhau giữa DB chung và DB dựng mới.
        /// </summary>
        private static readonly string[] PurchaseOnlyRuleKeywords = { "PURCHASE", "VAT", "IMPORT_TAX" };

        private static bool IsPurchaseOnlyRule(PricingRule rule)
        {
            var code = rule.RuleCode?.ToUpperInvariant() ?? string.Empty;
            var type = rule.RuleType?.ToUpperInvariant() ?? string.Empty;

            return PurchaseOnlyRuleKeywords.Any(keyword => code.Contains(keyword) || type.Contains(keyword));
        }

        public async Task<IEnumerable<PricingRuleDto>> GetAllAsync(string? orderType = null)
        {
            await EnsureDefaultRulesAsync();

            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var rules = await pricingRepo.GetAllAsync();

            var normalizedOrderType = orderType?.Trim().ToUpperInvariant();
            if (normalizedOrderType == "CONSIGNMENT")
            {
                rules = rules.Where(r => !IsPurchaseOnlyRule(r)).ToList();
            }
            else if (normalizedOrderType == "PURCHASE")
            {
                rules = rules.Where(IsPurchaseOnlyRule).ToList();
            }

            return rules.Select(r => new PricingRuleDto
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
            });
        }

        private async Task EnsureDefaultRulesAsync()
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var existingRules = await pricingRepo.GetAllAsync();
            bool hasChanges = false;

            // 1. Phí vận chuyển nội địa
            if (!existingRules.Any(r => string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase)))
            {
                await pricingRepo.AddAsync(new PricingRule
                {
                    Id = Guid.NewGuid(),
                    RuleName = "Phí vận chuyển nội địa",
                    RuleCode = "DOMESTIC_FEE",
                    RuleType = "DOMESTIC_FEE",
                    CalculationType = "FIXED",
                    Value = 5000m,
                    IsRequired = true,
                    Status = "ACTIVE",
                    Description = "Phí vận chuyển nội địa Việt Nam ước tính mặc định",
                    CreatedAt = DateTime.UtcNow
                });
                hasChanges = true;
            }

            // 2. Thuế và phí nhập khẩu (VAT)
            if (!existingRules.Any(r => string.Equals(r.RuleCode, "VAT", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(r.RuleType, "VAT", StringComparison.OrdinalIgnoreCase)))
            {
                await pricingRepo.AddAsync(new PricingRule
                {
                    Id = Guid.NewGuid(),
                    RuleName = "Thuế và phí nhập khẩu (VAT 8%)",
                    RuleCode = "VAT",
                    RuleType = "VAT",
                    CalculationType = "PERCENTAGE",
                    Value = 8m,
                    IsRequired = true,
                    Status = "ACTIVE",
                    Description = "Thuế giá trị gia tăng và phí làm thủ tục nhập khẩu mặc định (8%)",
                    CreatedAt = DateTime.UtcNow
                });
                hasChanges = true;
            }

            // 3. Phí đóng lại kiện carton
            if (!existingRules.Any(r => string.Equals(r.RuleCode, "CARTON_REPACK", StringComparison.OrdinalIgnoreCase)))
            {
                await pricingRepo.AddAsync(new PricingRule
                {
                    Id = Guid.NewGuid(),
                    RuleName = "Phí đóng lại kiện carton",
                    RuleCode = "CARTON_REPACK",
                    RuleType = "PACKING",
                    CalculationType = "FIXED",
                    Value = 25500m,
                    IsRequired = false,
                    Status = "ACTIVE",
                    Description = "Phụ phí đóng lại kiện carton",
                    CreatedAt = DateTime.UtcNow
                });
                hasChanges = true;
            }

            if (hasChanges)
            {
                await _unitOfWork.SaveChangesAsync();
            }
        }

        public async Task<PricingRuleDto> GetByIdAsync(Guid id)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var r = await pricingRepo.GetByIdAsync(id);

            if (r == null)
            {
                throw new NotFoundException($"Không tìm thấy rule phụ phí với ID: {id}");
            }

            return new PricingRuleDto
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
            };
        }

        public async Task<PricingRuleDto> CreateAsync(CreatePricingRuleRequest request)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRepo.GetAllAsync();

            // Validate ruleCode uniqueness
            var isDuplicate = allRules.Any(r =>
                string.Equals(r.RuleCode?.Trim(), request.RuleCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException($"Mã ruleCode '{request.RuleCode.Trim()}' đã tồn tại trong hệ thống. Vui lòng nhập mã khác.");
            }

            var newRule = new PricingRule
            {
                Id = Guid.NewGuid(),
                ServicePricingId = request.ServicePricingId,
                RuleName = request.RuleName.Trim(),
                RuleCode = request.RuleCode.Trim(),
                RuleType = request.RuleType.Trim(),
                ConditionType = request.ConditionType?.Trim(),
                ConditionValue = request.ConditionValue?.Trim(),
                CalculationType = request.CalculationType.Trim(),
                Value = request.Value,
                MinAmount = request.MinAmount,
                MaxAmount = request.MaxAmount,
                IsRequired = request.IsRequired,
                Status = request.Status?.Trim() ?? "ACTIVE",
                Description = request.Description?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await pricingRepo.AddAsync(newRule);
            await _unitOfWork.SaveChangesAsync();

            return new PricingRuleDto
            {
                Id = newRule.Id,
                ServicePricingId = newRule.ServicePricingId,
                RuleName = newRule.RuleName,
                RuleCode = newRule.RuleCode,
                RuleType = newRule.RuleType,
                ConditionType = newRule.ConditionType,
                ConditionValue = newRule.ConditionValue,
                CalculationType = newRule.CalculationType,
                Value = newRule.Value,
                MinAmount = newRule.MinAmount,
                MaxAmount = newRule.MaxAmount,
                IsRequired = newRule.IsRequired,
                Status = newRule.Status,
                Description = newRule.Description,
                CreatedAt = newRule.CreatedAt,
                UpdatedAt = newRule.UpdatedAt
            };
        }

        public async Task UpdateAsync(Guid id, UpdatePricingRuleRequest request)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var r = await pricingRepo.GetByIdAsync(id);

            if (r == null)
            {
                throw new NotFoundException($"Không tìm thấy rule phụ phí với ID: {id}");
            }

            var allRules = await pricingRepo.GetAllAsync();

            // Validate ruleCode uniqueness excluding current rule
            var isDuplicate = allRules.Any(x =>
                x.Id != id &&
                string.Equals(x.RuleCode?.Trim(), request.RuleCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                throw new ArgumentException($"Mã ruleCode '{request.RuleCode.Trim()}' đã tồn tại trong hệ thống. Vui lòng nhập mã khác.");
            }

            r.ServicePricingId = request.ServicePricingId;
            r.RuleName = request.RuleName.Trim();
            r.RuleCode = request.RuleCode.Trim();
            r.RuleType = request.RuleType.Trim();
            r.ConditionType = request.ConditionType?.Trim();
            r.ConditionValue = request.ConditionValue?.Trim();
            r.CalculationType = request.CalculationType.Trim();
            r.Value = request.Value;
            r.MinAmount = request.MinAmount;
            r.MaxAmount = request.MaxAmount;
            r.IsRequired = request.IsRequired;
            r.Status = request.Status?.Trim() ?? "ACTIVE";
            r.Description = request.Description?.Trim();
            r.UpdatedAt = DateTime.UtcNow;

            pricingRepo.Update(r);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            var pricingRepo = _unitOfWork.Repository<PricingRule>();
            var r = await pricingRepo.GetByIdAsync(id);

            if (r == null)
            {
                throw new NotFoundException($"Không tìm thấy rule phụ phí với ID: {id}");
            }

            pricingRepo.Delete(r);
            await _unitOfWork.SaveChangesAsync();
        }
    }
}
