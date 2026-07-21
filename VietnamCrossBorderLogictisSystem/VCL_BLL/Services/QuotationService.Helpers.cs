using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class QuotationService
    {
        /// <summary>
        /// Lấy hệ số quy đổi thể tích từ activeRules (VOLUMETRIC_DIVISOR),
        /// hoặc từ file cấu hình (PricingSettings:VolumetricDivisor),
        /// hoặc mặc định là 5000.
        /// </summary>
        private decimal GetVolumetricDivisor(IEnumerable<PricingRule> activeRules)
        {
            var rule = activeRules.FirstOrDefault(r =>
                string.Equals(r.RuleCode, "VOLUMETRIC_DIVISOR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RuleType, "VOLUMETRIC_DIVISOR", StringComparison.OrdinalIgnoreCase));

            if (rule != null && rule.Value > 0)
            {
                return rule.Value;
            }

            if (_configuration != null)
            {
                var configValStr = _configuration["PricingSettings:VolumetricDivisor"];
                if (decimal.TryParse(configValStr, out var configVal) && configVal > 0)
                {
                    return configVal;
                }
            }

            return 5000m;
        }

        /// <summary>
        /// Nhận diện phương án vận chuyển nhanh (Express) — hỗ trợ cả tên tiếng Anh ("Express")
        /// lẫn tiếng Việt ("Hỏa Tốc"). Dùng cho các mức giá/phí mặc định khi ServicePricing chưa có Price.
        /// </summary>
        private static bool IsExpressServiceType(string consignmentType)
        {
            if (string.IsNullOrWhiteSpace(consignmentType)) return false;
            var s = consignmentType.Trim();
            return s.Contains("Express", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Hỏa Tốc", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Lấy cân nhỏ nhất (minimum weight) cấu hình trong PricingRules cho matched pricing, mặc định là 1.0kg.
        /// </summary>
        private decimal GetMinimumWeight(IEnumerable<PricingRule> activeRules, ServicePricing matchedPricing)
        {
            decimal minimumWeight = 1.0m;
            if (matchedPricing == null) return minimumWeight;

            var minWeightRule = activeRules.FirstOrDefault(r => 
                (r.ServicePricingId == matchedPricing.Id || r.ServicePricingId == null) &&
                (string.Equals(r.ConditionType, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(r.RuleType, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(r.RuleCode, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase))
            );

            if (minWeightRule != null)
            {
                if (minWeightRule.Value > 0)
                {
                    minimumWeight = minWeightRule.Value;
                }
                else if (decimal.TryParse(minWeightRule.ConditionValue, out var val))
                {
                    minimumWeight = val;
                }
            }
            return minimumWeight;
        }

        /// <summary>
        /// Ước tính phí vận chuyển nội địa từ PricingRules.
        /// </summary>
        private decimal GetDomesticShippingFeeEstimate(IEnumerable<PricingRule> activeRules, ServicePricing matchedPricing)
        {
            decimal domesticFee = 0m;
            if (matchedPricing == null) return domesticFee;

            var domesticRule = activeRules.FirstOrDefault(r =>
                (r.ServicePricingId == matchedPricing.Id || r.ServicePricingId == null) &&
                (string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(r.ConditionType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                 r.RuleName.Contains("nội địa", StringComparison.OrdinalIgnoreCase))
            );

            if (domesticRule != null)
            {
                domesticFee = domesticRule.Value;
            }
            return domesticFee;
        }

        /// <summary>
        /// Tra bảng giá ServicePricing theo loại dịch vụ (ConsignmentType).
        /// Nếu không tìm thấy, trả về giá mặc định.
        /// </summary>
        private async Task<decimal> GetFreightRateAsync(string consignmentType)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allPricings = (await pricingRepo.GetAllAsync()).Where(p => !p.IsDeleted).ToList();

            // Tìm bảng giá khớp với ConsignmentType và còn hiệu lực
            var pricing = allPricings
                .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault();

            if (pricing?.Price != null)
                return pricing.Price.Value;

            // Fallback: giá mặc định
            return IsExpressServiceType(consignmentType)
                ? DefaultExpressRate
                : DefaultStandardRate;
        }

        /// <summary>
        /// Trả về phí dịch vụ cố định theo loại ký gửi.
        /// </summary>
        private static decimal GetServiceFee(string consignmentType)
        {
            return IsExpressServiceType(consignmentType)
                ? ExpressServiceFee
                : StandardServiceFee;
        }

        private QuotationFeeResponseDto MapToFeeResponseDto(QuotationFee f, Dictionary<Guid, PricingRule> ruleMap, int packageCount)
        {
            var dto = new QuotationFeeResponseDto
            {
                Id = f.Id,
                PricingRuleId = f.PricingRuleId,
                FeeId = f.PricingRuleId ?? f.Id,
                FeeName = f.FeeName,
                Label = f.FeeName,
                FeeType = f.FeeType,
                CalculationType = f.CalculationType,
                FeeCalculationType = (f.CalculationType ?? string.Empty).ToUpper(),
                Value = f.Value,
                UnitPrice = f.Value,
                Amount = f.Amount,
                Note = f.Note,
                CreatedAt = f.CreatedAt,
                ParcelId = f.ParcelId,
                ChargeableWeight = f.ChargeableWeight,
                ShippingFee = f.ShippingFee,
                TotalAmount = f.TotalAmount
            };

            // Mặc định cho Enabled và IsRequired
            if (string.Equals(f.FeeType, "MAIN_SERVICE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.FeeType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.FeeType, "SERVICE_FEE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.FeeType, "TAX", StringComparison.OrdinalIgnoreCase))
            {
                dto.Enabled = true;
                dto.IsRequired = true;
            }
            else
            {
                dto.Enabled = f.Amount > 0;
                dto.IsRequired = false;
            }

            // Gán Code và IsRequired từ Rule
            PricingRule rule = null;
            if (f.PricingRuleId.HasValue && ruleMap.TryGetValue(f.PricingRuleId.Value, out rule))
            {
                dto.Code = rule.RuleCode ?? f.FeeType;
                dto.IsRequired = rule.IsRequired;
                if (rule.IsRequired)
                {
                    dto.Enabled = true;
                }
            }
            else
            {
                dto.Code = f.FeeType;
            }

            // Gán Quantity
            if (string.Equals(dto.FeeCalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
            {
                dto.Quantity = null;
            }
            else // FIXED, PER_KG, PER_CBM, PER_PRODUCT
            {
                if (string.Equals(dto.FeeCalculationType, "FIXED", StringComparison.OrdinalIgnoreCase))
                {
                    dto.Quantity = packageCount > 0 ? packageCount : 1;
                }
                else
                {
                    dto.Quantity = 1;
                }
            }

            // Gán UnitNoun
            if (string.Equals(dto.Code, "WOOD_CRATE", StringComparison.OrdinalIgnoreCase) ||
                (f.FeeName ?? string.Empty).Contains("thùng gỗ", StringComparison.OrdinalIgnoreCase) ||
                (f.FeeName ?? string.Empty).Contains("đóng gỗ", StringComparison.OrdinalIgnoreCase))
            {
                dto.UnitNoun = "kiện";
            }
            else if (string.Equals(dto.Code, "INSPECTION", StringComparison.OrdinalIgnoreCase) ||
                     (f.FeeName ?? string.Empty).Contains("kiểm", StringComparison.OrdinalIgnoreCase))
            {
                dto.UnitNoun = "lần";
            }
            else
            {
                dto.UnitNoun = string.Equals(dto.FeeCalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase) ? null : "kiện";
            }

            return dto;
        }

        private class ParcelCalculationResult
        {
            public List<ParcelQuotationDetailDto> ParcelDetails { get; set; } = new();
            public List<QuotationFee> MainServiceFees { get; set; } = new();
            public decimal TotalWeight { get; set; }
            public decimal TotalVolume { get; set; }
            public decimal VolumetricWeight { get; set; }
            public decimal ChargeableWeight { get; set; }
            public decimal TotalFreightCharge { get; set; }
        }

        private ParcelCalculationResult CalculateParcelFees(
            List<Parcel> parcels, 
            decimal divisor, 
            decimal freightRate, 
            DateTime now)
        {
            var result = new ParcelCalculationResult();

            foreach (var p in parcels)
            {
                if (!p.ActualWeight.HasValue || p.ActualWeight.Value <= 0 ||
                    !p.Length.HasValue || p.Length.Value <= 0 ||
                    !p.Width.HasValue || p.Width.Value <= 0 ||
                    !p.Height.HasValue || p.Height.Value <= 0)
                {
                    throw new ArgumentException($"Kiện hàng '{p.PackageCode}' thiếu trọng lượng hoặc kích thước cần thiết để tính phí.");
                }

                var actualWeight = p.ActualWeight.Value;
                var length = p.Length.Value;
                var width = p.Width.Value;
                var height = p.Height.Value;
                
                var volume = length * width * height;
                var volumetricWeight = Math.Round(volume / divisor, 4);
                var chargeableWeight = Math.Max(actualWeight, volumetricWeight);
                var shippingFee = Math.Round(chargeableWeight * freightRate, 2);

                result.TotalWeight += actualWeight;
                result.TotalVolume += volume;
                result.VolumetricWeight += volumetricWeight;
                result.ChargeableWeight += chargeableWeight;
                result.TotalFreightCharge += shippingFee;

                result.ParcelDetails.Add(new ParcelQuotationDetailDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    ActualWeight = actualWeight,
                    Length = length,
                    Width = width,
                    Height = height,
                    VolumetricWeight = volumetricWeight,
                    ChargeableWeight = chargeableWeight,
                    ShippingFee = shippingFee
                });

                result.MainServiceFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = $"Cước vận chuyển kiện {p.PackageCode}",
                    FeeType = "MAIN_SERVICE",
                    CalculationType = "PER_KG",
                    Value = freightRate,
                    Amount = shippingFee,
                    ParcelId = p.Id,
                    ChargeableWeight = chargeableWeight,
                    ShippingFee = shippingFee,
                    TotalAmount = shippingFee,
                    Note = $"Cước vận chuyển kiện {p.PackageCode} (Cân nặng tính cước: {Math.Round(chargeableWeight, 2)} kg)",
                    CreatedAt = now
                });
            }

            return result;
        }

        private async Task<(decimal ImportTax, decimal VatAmount, List<QuotationFee> TaxFees)> CalculateTaxesAsync(
            List<OrderItem> orderItems,
            decimal estimatedFreightCharge,
            decimal serviceFee,
            DateTime now)
        {
            var categories = await _unitOfWork.Repository<ProductType>().GetAllAsync();
            decimal importTax = 0m;
            var taxFees = new List<QuotationFee>();

            foreach (var item in orderItems)
            {
                if (string.IsNullOrWhiteSpace(item.ProductType))
                {
                    throw new ArgumentException("Mặt hàng phải có thông tin phân loại sản phẩm (ProductType).");
                }

                ProductType category = null;
                if (Guid.TryParse(item.ProductType.Trim(), out var guidId))
                {
                    category = categories.FirstOrDefault(c => c.Id == guidId);
                }
                else
                {
                    category = categories.FirstOrDefault(c =>
                        string.Equals(c.Name?.Trim(), item.ProductType?.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (category == null || !category.ImportTaxRate.HasValue)
                {
                    throw new ArgumentException($"Loại hàng '{item.ProductType}' chưa được cấu hình thuế nhập khẩu hợp lệ.");
                }

                var itemImportTax = Math.Round((item.DeclaredValue ?? 0m) * category.ImportTaxRate.Value, 2);
                importTax += itemImportTax;

                if (itemImportTax > 0)
                {
                    taxFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = $"Thuế nhập khẩu - {category.Name}",
                        FeeType = "TAX",
                        CalculationType = "PERCENTAGE",
                        Value = category.ImportTaxRate.Value * 100m,
                        Amount = itemImportTax,
                        Note = $"Thuế nhập khẩu {category.ImportTaxRate.Value * 100m}% cho mặt hàng {item.ProductName ?? "không tên"}",
                        CreatedAt = now
                    });
                }
            }

            decimal vatAmount = Math.Round((estimatedFreightCharge + serviceFee) * 0.08m, 2);
            if (vatAmount > 0)
            {
                taxFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Thuế giá trị gia tăng (VAT 8%)",
                    FeeType = "TAX",
                    CalculationType = "PERCENTAGE",
                    Value = 8m,
                    Amount = vatAmount,
                    Note = "8% phí dịch vụ logistics",
                    CreatedAt = now
                });
            }

            return (importTax, vatAmount, taxFees);
        }

        /// <summary>Map cấu hình thùng của item sang DTO để trả trong response (null nếu item chưa gắn thùng).</summary>
        private static PackageConfigurationResponseDto? MapPackageConfig(
            Guid? packageConfigurationId, IReadOnlyDictionary<Guid, PackageConfiguration> configMap)
        {
            if (!packageConfigurationId.HasValue) return null;
            if (!configMap.TryGetValue(packageConfigurationId.Value, out var c)) return null;

            return new PackageConfigurationResponseDto
            {
                Id = c.Id,
                ConfigCode = c.ConfigCode,
                ConfigName = c.ConfigName,
                Length = c.Length,
                Width = c.Width,
                Height = c.Height,
                MaxWeight = c.MaxWeight,
                PackageFee = c.PackageFee,
                Status = c.Status
            };
        }

        private async Task<List<QuotationFee>> CalculatePackageFeesAsync(IEnumerable<OrderItem> orderItems, DateTime now)
        {
            var fees = new List<QuotationFee>();
            var configRepo = _unitOfWork.Repository<PackageConfiguration>();
            var allConfigs = await configRepo.GetAllAsync();
            var configMap = allConfigs.ToDictionary(c => c.Id, c => c);

            foreach (var item in orderItems)
            {
                if (item.PackageConfigurationId.HasValue && configMap.TryGetValue(item.PackageConfigurationId.Value, out var config))
                {
                    var qty = item.Quantity ?? 1;
                    bool isCustomBox = string.Equals(config.ConfigCode, "CUSTOM", StringComparison.OrdinalIgnoreCase);

                    if (isCustomBox)
                    {
                        // Thùng CUSTOM: phí theo thể tích khách nhập (dims cm) —
                        // PackageFee là đơn giá cho mỗi 1.000 cm³, làm tròn lên.
                        var feePerBox = VCL_BLL.Interface.IPackageConfigurationService.ComputeCustomBoxFee(
                            config.PackageFee, item.Length ?? 0m, item.Width ?? 0m, item.Height ?? 0m);
                        if (feePerBox <= 0m) continue;

                        var volumeCm3 = (item.Length ?? 0m) * (item.Width ?? 0m) * (item.Height ?? 0m);
                        fees.Add(new QuotationFee
                        {
                            Id = Guid.NewGuid(),
                            FeeName = $"Phí đóng thùng ({config.ConfigName})",
                            FeeType = "PACKING_FEE",
                            CalculationType = "PER_VOLUME",
                            Value = config.PackageFee,
                            Amount = feePerBox * qty,
                            Note = $"Thùng custom {volumeCm3:N0} cm³ ({config.PackageFee:N0}đ/1.000cm³) cho {qty} kiện {item.ProductName}",
                            CreatedAt = now
                        });
                    }
                    else if (config.PackageFee > 0)
                    {
                        var totalFee = config.PackageFee * qty;
                        fees.Add(new QuotationFee
                        {
                            Id = Guid.NewGuid(),
                            FeeName = $"Phí đóng thùng ({config.ConfigName})",
                            FeeType = "PACKING_FEE",
                            CalculationType = "FIXED",
                            Value = config.PackageFee,
                            Amount = totalFee,
                            Note = $"Phí đóng gói {config.ConfigName} cho {qty} kiện {item.ProductName}",
                            CreatedAt = now
                        });
                    }
                }
            }

            return fees;
        }
    }
}
