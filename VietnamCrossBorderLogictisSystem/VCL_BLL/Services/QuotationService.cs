using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class QuotationService : IQuotationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;

        // Giá mặc định khi không tìm thấy ServicePricing trong DB (VND/kg)
        private const decimal DefaultExpressRate = 50000m;
        private const decimal DefaultStandardRate = 30000m;

        // Phí dịch vụ cố định (VND)
        private const decimal ExpressServiceFee = 50000m;
        private const decimal StandardServiceFee = 30000m;

        // Tỷ lệ thuế tạm tính trên tổng declared value
        private const decimal TaxRate = 0.10m;

        // Báo giá tạm tính có hiệu lực 7 ngày
        private const int QuotationValidDays = 7;

        public QuotationService(IUnitOfWork unitOfWork, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
        }

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

        /// <inheritdoc/>
        public async Task<EstimateQuotationResponse> CreateEstimateQuotationAsync(Guid orderId)
        {
            // 1. Tìm Order theo orderId
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);

            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            // 2. Lấy danh sách OrderItems thuộc Order
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == orderId).ToList();

            if (!orderItems.Any())
                throw new ArgumentException("Đơn hàng chưa có sản phẩm nào. Vui lòng thêm sản phẩm trước khi tạo báo giá.");

            // 3. Tính toán cân nặng & thể tích
            var itemDetails = new List<OrderItemDetail>();
            decimal totalWeight = 0m;
            decimal totalVolume = 0m;
            decimal totalDeclaredValue = 0m;

            foreach (var item in orderItems)
            {
                var qty = item.Quantity ?? 1;
                var itemWeight = item.Weight ?? 0m;
                var itemLength = item.Length ?? 0m;
                var itemWidth = item.Width ?? 0m;
                var itemHeight = item.Height ?? 0m;
                var itemVolume = itemLength * itemWidth * itemHeight;
                var itemDeclaredValue = item.DeclaredValue ?? 0m;

                totalWeight += itemWeight;
                totalVolume += itemVolume * qty;
                totalDeclaredValue += itemDeclaredValue * qty;

                itemDetails.Add(new OrderItemDetail
                {
                    Id = item.Id,
                    ProductName = item.ProductName ?? string.Empty,
                    Weight = item.Weight,
                    Length = item.Length,
                    Width = item.Width,
                    Height = item.Height,
                    DeclaredValue = item.DeclaredValue,
                    ItemVolume = itemVolume
                });
            }

            // 5. Tìm customer & route
            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = order.CustomerId.HasValue
                ? await customerRepo.GetByIdAsync(order.CustomerId.Value)
                : null;
            var customerCountry = customer?.Country;

            // 6. Tìm cấu hình bảng giá phù hợp từ ServicePricing (giá vận chuyển chính)
            var consignmentType = order.ConsignmentType ?? "Express";
            var route = order.Route ?? customerCountry;

            var servicePricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allPricings = await servicePricingRepo.GetAllAsync();

            var matchedPricing = allPricings
                .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault(p =>
                    string.IsNullOrWhiteSpace(route) ||
                    string.Equals($"{p.OriginCountry}-{p.DestinationCountry}", route, StringComparison.OrdinalIgnoreCase) ||
                    (p.OriginCountry != null && route.Contains(p.OriginCountry, StringComparison.OrdinalIgnoreCase)) ||
                    (p.DestinationCountry != null && route.Contains(p.DestinationCountry, StringComparison.OrdinalIgnoreCase))
                );

            if (matchedPricing == null)
            {
                // Fallback to general service type pricing
                matchedPricing = allPricings
                    .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                    .OrderByDescending(p => p.EffectiveDate)
                    .FirstOrDefault();
            }

            // Fetch pricing rules
            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var activeRules = allRules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 4. Tính Volumetric Weight, Minimum Weight & Net Weight (Chargeable Weight)
            var divisor = GetVolumetricDivisor(activeRules);
            var freightRate = matchedPricing?.Price ?? (IsExpressServiceType(consignmentType) ? DefaultExpressRate : DefaultStandardRate);

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcels = (await parcelRepo.GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .ToList();

            var quotationFees = new List<QuotationFee>();
            var now = DateTime.UtcNow;

            var parcelCalculationDetails = new List<ParcelQuotationDetailDto>();

            decimal volumetricWeight = 0m;
            decimal chargeableWeight = 0m;
            decimal estimatedFreightCharge = 0m;
            decimal serviceFee = 0m;
            decimal taxAndDuty = 0m;
            decimal totalEstimatedCost = 0m;
            bool isConsignment = string.IsNullOrEmpty(order.OrderType) || 
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            if (parcels.Any())
            {
                var parcelResult = CalculateParcelFees(parcels, divisor, freightRate, now);
                totalWeight = parcelResult.TotalWeight;
                totalVolume = parcelResult.TotalVolume;
                volumetricWeight = parcelResult.VolumetricWeight;
                chargeableWeight = parcelResult.ChargeableWeight;
                estimatedFreightCharge = parcelResult.TotalFreightCharge;
                parcelCalculationDetails = parcelResult.ParcelDetails;

                quotationFees.AddRange(parcelResult.MainServiceFees);
            }
            else
            {
                foreach (var detail in itemDetails)
                {
                    var item = orderItems.FirstOrDefault(oi => oi.Id == detail.Id);
                    var qty = item?.Quantity ?? 1;
                    detail.VolumetricWeight = Math.Round((detail.ItemVolume * qty) / divisor, 4);
                    detail.Quantity = qty;
                }
                volumetricWeight = totalVolume / divisor;
                var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);
                chargeableWeight = Math.Max(totalWeight, Math.Max(volumetricWeight, minimumWeight));

                if (matchedPricing != null && matchedPricing.Price.HasValue)
                {
                    estimatedFreightCharge = Math.Round(chargeableWeight * matchedPricing.Price.Value, 2);
                }
                else
                {
                    estimatedFreightCharge = Math.Round(chargeableWeight * freightRate, 2);
                }

                if (estimatedFreightCharge > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = "Cước vận chuyển quốc tế",
                        FeeType = "MAIN_SERVICE",
                        CalculationType = matchedPricing != null ? "PER_KG" : "FIXED",
                        Value = freightRate,
                        Amount = estimatedFreightCharge,
                        Note = $"Cước vận chuyển quốc tế dựa trên cân nặng tính cước {Math.Round(chargeableWeight, 2)} kg",
                        CreatedAt = now
                    });
                }
            }

            if (isConsignment)
            {
                var domesticShippingFee = GetDomesticShippingFeeEstimate(activeRules, matchedPricing);
                serviceFee = domesticShippingFee;
                taxAndDuty = 0m;
                totalEstimatedCost = estimatedFreightCharge + serviceFee;

                var domesticRule = activeRules.FirstOrDefault(r =>
                    (r.ServicePricingId == matchedPricing?.Id || r.ServicePricingId == null) &&
                    (string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.ConditionType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     r.RuleName.Contains("nội địa", StringComparison.OrdinalIgnoreCase))
                );

                if (domesticShippingFee > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        PricingRuleId = domesticRule?.Id,
                        FeeName = domesticRule?.RuleName ?? "Phí vận chuyển nội địa",
                        FeeType = "DOMESTIC_FEE",
                        CalculationType = domesticRule?.CalculationType ?? "FIXED",
                        Value = domesticRule?.Value ?? domesticShippingFee,
                        Amount = domesticShippingFee,
                        Note = domesticRule?.Description ?? "Phí vận chuyển nội địa ước tính",
                        CreatedAt = now
                    });
                }
            }
            else
            {
                decimal baseServiceFee = IsExpressServiceType(consignmentType)
                    ? ExpressServiceFee
                    : StandardServiceFee;

                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = IsExpressServiceType(consignmentType) ? "Phí dịch vụ Hỏa Tốc" : "Phí dịch vụ Chuẩn",
                    FeeType = "SERVICE_FEE",
                    CalculationType = "FIXED",
                    Value = baseServiceFee,
                    Amount = baseServiceFee,
                    Note = "Phí dịch vụ cơ bản",
                    CreatedAt = now
                });

                decimal totalSurcharges = 0m;
                foreach (var rule in activeRules)
                {
                    // Nếu rule liên kết với một ServicePricing cụ thể, kiểm tra xem có khớp không
                    if (rule.ServicePricingId.HasValue)
                    {
                        if (matchedPricing == null || matchedPricing.Id != rule.ServicePricingId.Value)
                        {
                            continue; // Không khớp dịch vụ cụ thể
                        }
                    }

                    // Kiểm tra điều kiện áp dụng
                    bool isConditionMet = false;
                    if (string.IsNullOrWhiteSpace(rule.ConditionType) || string.Equals(rule.ConditionType, "ALWAYS", StringComparison.OrdinalIgnoreCase))
                    {
                        isConditionMet = true;
                    }
                    else if (string.Equals(rule.ConditionType, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(rule.ConditionValue, out var minWeight))
                        {
                            isConditionMet = chargeableWeight >= minWeight;
                        }
                    }
                    else if (string.Equals(rule.ConditionType, "MIN_VOLUME", StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(rule.ConditionValue, out var minVolume))
                        {
                            var totalCbm = totalVolume / 1000000m;
                            isConditionMet = totalCbm >= minVolume;
                        }
                    }
                    else if (string.Equals(rule.ConditionType, "MIN_DECLARED_VALUE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(rule.ConditionValue, out var minVal))
                        {
                            isConditionMet = totalDeclaredValue >= minVal;
                        }
                    }
                    else if (string.Equals(rule.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase))
                    {
                        isConditionMet = order.RequiresInspection;
                    }
                    else if (string.Equals(rule.ConditionType, "REQUIRES_WOODEN_BOX", StringComparison.OrdinalIgnoreCase))
                    {
                        var note = order.Note?.ToLower() ?? string.Empty;
                        isConditionMet = note.Contains("wood") || note.Contains("đóng gỗ") || note.Contains("thùng gỗ");
                    }

                    // Xác định xem rule có được áp dụng hay không
                    bool isApplicable = false;
                    if (rule.IsRequired)
                    {
                        isApplicable = isConditionMet;
                    }
                    else
                    {
                        // Phụ phí tùy chọn - áp dụng nếu được yêu cầu rõ ràng hoặc note khớp
                        if (string.Equals(rule.RuleType, "INSPECTION", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(rule.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase))
                        {
                            isApplicable = order.RequiresInspection;
                        }
                        else if (string.Equals(rule.RuleType, "WOODEN_BOX", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(rule.ConditionType, "REQUIRES_WOODEN_BOX", StringComparison.OrdinalIgnoreCase))
                        {
                            var note = order.Note?.ToLower() ?? string.Empty;
                            isApplicable = note.Contains("wood") || note.Contains("đóng gỗ") || note.Contains("thùng gỗ");
                        }
                        else if (string.Equals(rule.RuleType, "INSURANCE", StringComparison.OrdinalIgnoreCase))
                        {
                            var note = order.Note?.ToLower() ?? string.Empty;
                            isApplicable = note.Contains("insurance") || note.Contains("bảo hiểm") || isConditionMet;
                        }
                        else
                        {
                            var note = order.Note?.ToLower() ?? string.Empty;
                            isApplicable = !string.IsNullOrWhiteSpace(rule.RuleCode) && note.Contains(rule.RuleCode.ToLower());
                        }
                    }

                    decimal surchargeAmount = 0m;
                    if (isApplicable)
                    {
                        if (string.Equals(rule.CalculationType, "FIXED", StringComparison.OrdinalIgnoreCase))
                        {
                            surchargeAmount = rule.Value;
                        }
                        else if (string.Equals(rule.CalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseAmount = totalDeclaredValue > 0 ? totalDeclaredValue : estimatedFreightCharge;
                            surchargeAmount = baseAmount * (rule.Value / 100m);
                        }
                        else if (string.Equals(rule.CalculationType, "PER_KG", StringComparison.OrdinalIgnoreCase))
                        {
                            surchargeAmount = chargeableWeight * rule.Value;
                        }
                        else if (string.Equals(rule.CalculationType, "PER_CBM", StringComparison.OrdinalIgnoreCase))
                        {
                            surchargeAmount = (totalVolume / 1000000m) * rule.Value;
                        }
                        else if (string.Equals(rule.CalculationType, "PER_PRODUCT", StringComparison.OrdinalIgnoreCase))
                        {
                            var totalQty = orderItems.Sum(item => item.Quantity ?? 1);
                            surchargeAmount = totalQty * rule.Value;
                        }

                        // Áp dụng giới hạn Min/Max nếu có cấu hình
                        if (rule.MinAmount.HasValue && surchargeAmount < rule.MinAmount.Value)
                        {
                            surchargeAmount = rule.MinAmount.Value;
                        }
                        if (rule.MaxAmount.HasValue && surchargeAmount > rule.MaxAmount.Value)
                        {
                            surchargeAmount = rule.MaxAmount.Value;
                        }

                        totalSurcharges += surchargeAmount;
                    }

                    // Luôn add để lưu trữ vào DB (Enabled = false nếu Amount = 0)
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        PricingRuleId = rule.Id,
                        FeeName = rule.RuleName,
                        FeeType = "SURCHARGE",
                        CalculationType = rule.CalculationType,
                        Value = rule.Value,
                        Amount = surchargeAmount,
                        Note = rule.Description,
                        CreatedAt = now
                    });
                }

                serviceFee = baseServiceFee + totalSurcharges;
            }

            var taxResult = await CalculateTaxesAsync(orderItems, estimatedFreightCharge, serviceFee, now);
            decimal vatAmount = taxResult.VatAmount;
            decimal importTax = taxResult.ImportTax;
            taxAndDuty = importTax + vatAmount;
            totalEstimatedCost = estimatedFreightCharge + serviceFee + vatAmount + importTax;

            quotationFees.AddRange(taxResult.TaxFees);

            // 7. Tạo Quotation record trong DB
            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                QuoteType = "ESTIMATE",
                Status = "DRAFT",
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                Vat = vatAmount,
                ImportTax = importTax,
                TotalVolume = totalVolume,
                CreatedAt = now,
                ExpiredAt = now.AddDays(QuotationValidDays),
                ServicePricingId = matchedPricing?.Id
            };

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            await quotationRepo.AddAsync(quotation);

            // Save Quotation Fees to DB
            foreach (var qf in quotationFees)
            {
                qf.QuotationId = quotation.Id;
                await _unitOfWork.Repository<QuotationFee>().AddAsync(qf);
            }

            // Cập nhật tổng cân nặng & thể tích trên Order
            order.TotalWeight = totalWeight;
            order.TotalVolume = totalVolume;
            orderRepo.Update(order);

            await _unitOfWork.SaveChangesAsync();

            // 8. Trả về response
            return new EstimateQuotationResponse
            {
                QuotationId = quotation.Id,
                OrderId = orderId,
                ConsignmentType = consignmentType,
                QuoteType = quotation.QuoteType,
                Status = quotation.Status,
                TotalWeight = totalWeight,
                TotalVolume = totalVolume,
                VolumetricWeight = Math.Round(volumetricWeight, 4),
                ChargeableWeight = Math.Round(chargeableWeight, 4),
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                Vat = vatAmount,
                ImportTax = importTax,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = now,
                ExpiredAt = quotation.ExpiredAt,
                Items = itemDetails,
                Parcels = parcelCalculationDetails,
                AdditionalFees = quotationFees
                    .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase))
                    .Select(f => MapToFeeResponseDto(f, activeRules.ToDictionary(r => r.Id, r => r), orderItems.Sum(item => item.Quantity ?? 1)))
                    .ToList()
            };
        }

        /// <summary>
        /// Tra bảng giá ServicePricing theo loại dịch vụ (ConsignmentType).
        /// Nếu không tìm thấy, trả về giá mặc định.
        /// </summary>
        private async Task<decimal> GetFreightRateAsync(string consignmentType)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allPricings = await pricingRepo.GetAllAsync();

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

        /// <inheritdoc/>
        public async Task<AcceptQuotationResponse> AcceptQuotationAsync(Guid quotationId, Guid userId)
        {
            // 1. Tìm Quotation
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);

            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            // 2. Kiểm tra trạng thái — DRAFT hoặc PENDING mới được accept
            if (string.Equals(quotation.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Báo giá này đã được xác nhận trước đó.");

            if (string.Equals(quotation.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Báo giá này đã bị hủy, không thể xác nhận.");

            if (!string.Equals(quotation.Status, "DRAFT", StringComparison.OrdinalIgnoreCase) && 
                !string.Equals(quotation.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {quotation.Status}");

            // 3. Kiểm tra hết hạn
            if (quotation.ExpiredAt.HasValue && quotation.ExpiredAt.Value < DateTime.UtcNow)
                throw new ArgumentException("Báo giá đã hết hạn. Vui lòng tạo báo giá mới.");

            // 4. Kiểm tra Quotation thuộc đúng khách đang đăng nhập
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);

            if (order == null)
                throw new NotFoundException("Không tìm thấy đơn hàng liên quan đến báo giá.");

            // Tra ngược: Order.CustomerId → Customer.UserId == userId
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.Id == order.CustomerId);

            if (customer == null || customer.UserId != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận báo giá này.");

            // 5. Cập nhật trạng thái
            var now = DateTime.UtcNow;

            quotation.Status = "ACCEPTED";
            quotationRepo.Update(quotation);

            order.Status = "QUOTATION_CONFIRMED";
            orderRepo.Update(order);

            await _unitOfWork.SaveChangesAsync();

            // 6. Trả về response
            return new AcceptQuotationResponse
            {
                QuotationId = quotation.Id,
                OrderId = order.Id,
                QuotationStatus = quotation.Status,
                OrderStatus = order.Status,
                AcceptedAt = now
            };
        }

        /// <inheritdoc/>
        public async Task<EstimateQuotationResponse> CreateEstimateQuotationWithBodyAsync(Guid orderId, CreateQuotationRequest request)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            decimal totalWeight = request.WeightKg ?? 0m;
            decimal totalVolume = 0m;
            if (request.VolumeM3.HasValue)
            {
                totalVolume = request.VolumeM3.Value * 1000000m; // Convert m³ to cm³
            }
            decimal totalDeclaredValue = request.DeclaredValue ?? 0m;

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == orderId).ToList();

            var itemDetails = new List<OrderItemDetail>();
            if (totalWeight == 0m && totalVolume == 0m)
            {
                foreach (var item in orderItems)
                {
                    var qty = item.Quantity ?? 1;
                    var itemWeight = item.Weight ?? 0m;
                    var itemLength = item.Length ?? 0m;
                    var itemWidth = item.Width ?? 0m;
                    var itemHeight = item.Height ?? 0m;
                    var itemVolume = itemLength * itemWidth * itemHeight;
                    var itemDeclaredValue = item.DeclaredValue ?? 0m;

                    totalWeight += itemWeight;
                    totalVolume += itemVolume * qty;
                    totalDeclaredValue += itemDeclaredValue * qty;

                    itemDetails.Add(new OrderItemDetail
                    {
                        Id = item.Id,
                        ProductName = item.ProductName ?? string.Empty,
                        Weight = item.Weight,
                        Length = item.Length,
                        Width = item.Width,
                        Height = item.Height,
                        DeclaredValue = item.DeclaredValue,
                        ItemVolume = itemVolume
                    });
                }
            }
            else
            {
                foreach (var item in orderItems)
                {
                    itemDetails.Add(new OrderItemDetail
                    {
                        Id = item.Id,
                        ProductName = item.ProductName ?? string.Empty,
                        Weight = item.Weight,
                        Length = item.Length,
                        Width = item.Width,
                        Height = item.Height,
                        DeclaredValue = item.DeclaredValue,
                        ItemVolume = (item.Length ?? 0) * (item.Width ?? 0) * (item.Height ?? 0)
                    });
                }
            }

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = order.CustomerId.HasValue
                ? await customerRepo.GetByIdAsync(order.CustomerId.Value)
                : null;
            var customerCountry = customer?.Country;

            var consignmentType = request.ServiceType ?? order.ConsignmentType ?? "Express";
            var route = order.Route ?? customerCountry;

            var servicePricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allPricings = await servicePricingRepo.GetAllAsync();

            var matchedPricing = allPricings
                .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault(p =>
                    string.IsNullOrWhiteSpace(route) ||
                    string.Equals($"{p.OriginCountry}-{p.DestinationCountry}", route, StringComparison.OrdinalIgnoreCase) ||
                    (p.OriginCountry != null && route.Contains(p.OriginCountry, StringComparison.OrdinalIgnoreCase)) ||
                    (p.DestinationCountry != null && route.Contains(p.DestinationCountry, StringComparison.OrdinalIgnoreCase))
                );

            if (matchedPricing == null)
            {
                matchedPricing = allPricings
                    .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                    .OrderByDescending(p => p.EffectiveDate)
                    .FirstOrDefault();
            }

            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var activeRules = allRules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 4. Tính Volumetric Weight, Minimum Weight & Net Weight (Chargeable Weight)
            var divisor = GetVolumetricDivisor(activeRules);
            var freightRate = matchedPricing?.Price ?? (IsExpressServiceType(consignmentType) ? DefaultExpressRate : DefaultStandardRate);

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcels = (await parcelRepo.GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .ToList();

            var quotationFees = new List<QuotationFee>();
            var now = DateTime.UtcNow;

            var parcelCalculationDetails = new List<ParcelQuotationDetailDto>();

            decimal volumetricWeight = 0m;
            decimal chargeableWeight = 0m;
            decimal estimatedFreightCharge = 0m;
            decimal serviceFee = 0m;
            decimal taxAndDuty = 0m;
            decimal totalEstimatedCost = 0m;
            bool isConsignment = string.IsNullOrEmpty(order.OrderType) || 
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            if (parcels.Any())
            {
                var parcelResult = CalculateParcelFees(parcels, divisor, freightRate, now);
                totalWeight = parcelResult.TotalWeight;
                totalVolume = parcelResult.TotalVolume;
                volumetricWeight = parcelResult.VolumetricWeight;
                chargeableWeight = parcelResult.ChargeableWeight;
                estimatedFreightCharge = parcelResult.TotalFreightCharge;
                parcelCalculationDetails = parcelResult.ParcelDetails;

                foreach (var qf in parcelResult.MainServiceFees)
                {
                    qf.Id = Guid.Empty;
                    quotationFees.Add(qf);
                }
            }
            else
            {
                foreach (var detail in itemDetails)
                {
                    var item = orderItems.FirstOrDefault(oi => oi.Id == detail.Id);
                    var qty = item?.Quantity ?? 1;
                    detail.VolumetricWeight = Math.Round((detail.ItemVolume * qty) / divisor, 4);
                    detail.Quantity = qty;
                }
                volumetricWeight = totalVolume / divisor;
                var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);
                chargeableWeight = Math.Max(totalWeight, Math.Max(volumetricWeight, minimumWeight));

                if (matchedPricing != null && matchedPricing.Price.HasValue)
                {
                    estimatedFreightCharge = Math.Round(chargeableWeight * matchedPricing.Price.Value, 2);
                }
                else
                {
                    estimatedFreightCharge = Math.Round(chargeableWeight * freightRate, 2);
                }

                if (estimatedFreightCharge > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.Empty,
                        FeeName = "Cước vận chuyển quốc tế",
                        FeeType = "MAIN_SERVICE",
                        CalculationType = matchedPricing != null ? "PER_KG" : "FIXED",
                        Value = freightRate,
                        Amount = estimatedFreightCharge,
                        Note = $"Cước vận chuyển quốc tế dựa trên cân nặng tính cước {Math.Round(chargeableWeight, 2)} kg",
                        CreatedAt = now
                    });
                }
            }

            if (isConsignment)
            {
                var domesticShippingFee = GetDomesticShippingFeeEstimate(activeRules, matchedPricing);
                serviceFee = domesticShippingFee;
                taxAndDuty = 0m;
                totalEstimatedCost = estimatedFreightCharge + serviceFee;

                var domesticRule = activeRules.FirstOrDefault(r =>
                    (r.ServicePricingId == matchedPricing?.Id || r.ServicePricingId == null) &&
                    (string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.ConditionType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     r.RuleName.Contains("nội địa", StringComparison.OrdinalIgnoreCase))
                );

                if (domesticShippingFee > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.Empty,
                        PricingRuleId = domesticRule?.Id,
                        FeeName = domesticRule?.RuleName ?? "Phí vận chuyển nội địa",
                        FeeType = "DOMESTIC_FEE",
                        CalculationType = domesticRule?.CalculationType ?? "FIXED",
                        Value = domesticRule?.Value ?? domesticShippingFee,
                        Amount = domesticShippingFee,
                        Note = domesticRule?.Description ?? "Phí vận chuyển nội địa ước tính",
                        CreatedAt = now
                    });
                }
            }
            else
            {
                decimal baseServiceFee = IsExpressServiceType(consignmentType)
                    ? ExpressServiceFee
                    : StandardServiceFee;

                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.Empty,
                    FeeName = IsExpressServiceType(consignmentType) ? "Phí dịch vụ Hỏa Tốc" : "Phí dịch vụ Chuẩn",
                    FeeType = "SERVICE_FEE",
                    CalculationType = "FIXED",
                    Value = baseServiceFee,
                    Amount = baseServiceFee,
                    Note = "Phí dịch vụ cơ bản",
                    CreatedAt = now
                });

                decimal totalSurcharges = 0m;
                foreach (var rule in activeRules)
                {
                    if (rule.ServicePricingId.HasValue && (matchedPricing == null || matchedPricing.Id != rule.ServicePricingId.Value))
                        continue;

                    bool isConditionMet = false;
                    if (string.IsNullOrWhiteSpace(rule.ConditionType) || string.Equals(rule.ConditionType, "ALWAYS", StringComparison.OrdinalIgnoreCase))
                    {
                        isConditionMet = true;
                    }
                    else if (string.Equals(rule.ConditionType, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(rule.ConditionValue, out var minWeight))
                            isConditionMet = chargeableWeight >= minWeight;
                    }
                    else if (string.Equals(rule.ConditionType, "MIN_VOLUME", StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(rule.ConditionValue, out var minVolume))
                            isConditionMet = (totalVolume / 1000000m) >= minVolume;
                    }
                    else if (string.Equals(rule.ConditionType, "MIN_DECLARED_VALUE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(rule.ConditionValue, out var minVal))
                            isConditionMet = totalDeclaredValue >= minVal;
                    }
                    else if (string.Equals(rule.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase))
                    {
                        isConditionMet = order.RequiresInspection;
                    }

                    bool isApplicable = rule.IsRequired ? isConditionMet : false;

                    decimal surchargeAmount = 0m;
                    if (isApplicable)
                    {
                        if (string.Equals(rule.CalculationType, "FIXED", StringComparison.OrdinalIgnoreCase))
                            surchargeAmount = rule.Value;
                        else if (string.Equals(rule.CalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseAmount = totalDeclaredValue > 0 ? totalDeclaredValue : estimatedFreightCharge;
                            surchargeAmount = baseAmount * (rule.Value / 100m);
                        }
                        else if (string.Equals(rule.CalculationType, "PER_KG", StringComparison.OrdinalIgnoreCase))
                            surchargeAmount = chargeableWeight * rule.Value;

                        totalSurcharges += surchargeAmount;
                    }

                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.Empty,
                        PricingRuleId = rule.Id,
                        FeeName = rule.RuleName,
                        FeeType = "SURCHARGE",
                        CalculationType = rule.CalculationType,
                        Value = rule.Value,
                        Amount = surchargeAmount,
                        Note = rule.Description,
                        CreatedAt = now
                    });
                }

            }

            var taxResult = await CalculateTaxesAsync(orderItems, estimatedFreightCharge, serviceFee, now);
            decimal vatAmount = taxResult.VatAmount;
            decimal importTax = taxResult.ImportTax;
            taxAndDuty = importTax + vatAmount;
            totalEstimatedCost = estimatedFreightCharge + serviceFee + vatAmount + importTax;

            // Map IDs to Guid.Empty as this is a non-persisted estimate with custom body
            foreach (var tf in taxResult.TaxFees)
            {
                tf.Id = Guid.Empty;
                quotationFees.Add(tf);
            }

            var activeRulesDict = activeRules.ToDictionary(r => r.Id, r => r);
            var totalQty = orderItems.Sum(item => item.Quantity ?? 1);
            var additionalFees = quotationFees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase))
                .Select(f => MapToFeeResponseDto(f, activeRulesDict, totalQty))
                .ToList();

            return new EstimateQuotationResponse
            {
                QuotationId = Guid.Empty,
                OrderId = orderId,
                ConsignmentType = consignmentType,
                QuoteType = "ESTIMATE",
                Status = "DRAFT",
                TotalWeight = totalWeight,
                TotalVolume = totalVolume,
                VolumetricWeight = Math.Round(volumetricWeight, 4),
                ChargeableWeight = Math.Round(chargeableWeight, 4),
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                Vat = vatAmount,
                ImportTax = importTax,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = DateTime.UtcNow,
                ExpiredAt = DateTime.UtcNow.AddDays(QuotationValidDays),
                Items = itemDetails,
                Parcels = parcelCalculationDetails,
                AdditionalFees = additionalFees
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> SendQuotationAsync(Guid orderId, CreateQuotationRequest request)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = order.CustomerId.HasValue
                ? await customerRepo.GetByIdAsync(order.CustomerId.Value)
                : null;
            var customerCountry = customer?.Country;

            var consignmentType = request.ServiceType ?? order.ConsignmentType ?? "Express";
            var route = order.Route ?? customerCountry;

            var servicePricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allPricings = await servicePricingRepo.GetAllAsync();

            var matchedPricing = allPricings
                .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault(p =>
                    string.IsNullOrWhiteSpace(route) ||
                    string.Equals($"{p.OriginCountry}-{p.DestinationCountry}", route, StringComparison.OrdinalIgnoreCase) ||
                    (p.OriginCountry != null && route.Contains(p.OriginCountry, StringComparison.OrdinalIgnoreCase)) ||
                    (p.DestinationCountry != null && route.Contains(p.DestinationCountry, StringComparison.OrdinalIgnoreCase))
                );

            if (matchedPricing == null)
            {
                matchedPricing = allPricings
                    .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                    .OrderByDescending(p => p.EffectiveDate)
                    .FirstOrDefault();
            }

            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var activeRules = allRules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var divisor = GetVolumetricDivisor(activeRules);

            // Calculate actual measured data from warehouse parcels
            decimal totalActualWeight = 0m;
            decimal totalVolumetricWeight = 0m;
            decimal totalVolumeCm3 = 0m;
            decimal domesticShippingFee = 0m;

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcels = (await parcelRepo.GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .ToList();

            decimal freightRate;
            if (matchedPricing != null && matchedPricing.Price.HasValue)
            {
                freightRate = matchedPricing.Price.Value;
            }
            else
            {
                freightRate = IsExpressServiceType(consignmentType)
                    ? DefaultExpressRate
                    : DefaultStandardRate;
            }

            var now = DateTime.UtcNow;
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == orderId).ToList();

            var quotationFees = new List<QuotationFee>();

            decimal estimatedFreightCharge = 0m;
            decimal netWeight = 0m;

            if (parcels.Any())
            {
                var parcelResult = CalculateParcelFees(parcels, divisor, freightRate, now);
                totalActualWeight = parcelResult.TotalWeight;
                totalVolumetricWeight = parcelResult.VolumetricWeight;
                totalVolumeCm3 = parcelResult.TotalVolume;
                estimatedFreightCharge = parcelResult.TotalFreightCharge;
                netWeight = parcelResult.ChargeableWeight;

                var parcelIds = parcels.Select(p => p.Id).ToList();
                var waybillRepo = _unitOfWork.Repository<DomesticWaybill>();
                var allWaybills = await waybillRepo.GetAllAsync();
                domesticShippingFee = allWaybills
                    .Where(w => w.ParcelId.HasValue && parcelIds.Contains(w.ParcelId.Value))
                    .Sum(w => w.DeliveryFee ?? 0m);

                quotationFees.AddRange(parcelResult.MainServiceFees);
            }
            else
            {
                // Fallback to request details or items
                totalActualWeight = request.WeightKg ?? order.TotalWeight ?? 0m;

                if (request.VolumeM3.HasValue)
                {
                    totalVolumeCm3 = request.VolumeM3.Value * 1000000m;
                }
                else if (order.TotalVolume.HasValue)
                {
                    totalVolumeCm3 = order.TotalVolume.Value;
                }
                else
                {
                    totalVolumeCm3 = orderItems.Sum(item => (item.Length ?? 0m) * (item.Width ?? 0m) * (item.Height ?? 0m));
                }
                totalVolumetricWeight = totalVolumeCm3 / divisor;

                domesticShippingFee = GetDomesticShippingFeeEstimate(activeRules, matchedPricing);

                var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);
                netWeight = Math.Max(totalActualWeight, Math.Max(totalVolumetricWeight, minimumWeight));

                estimatedFreightCharge = Math.Round(netWeight * freightRate, 2);

                if (estimatedFreightCharge > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = "Cước vận chuyển quốc tế",
                        FeeType = "MAIN_SERVICE",
                        CalculationType = matchedPricing != null ? "PER_KG" : "FIXED",
                        Value = freightRate,
                        Amount = estimatedFreightCharge,
                        Note = $"Cước vận chuyển quốc tế chính thức",
                        CreatedAt = now
                    });
                }
            }

            decimal serviceFee;
            decimal taxAndDuty;
            decimal totalEstimatedCost;

            bool isConsignment = string.IsNullOrEmpty(order.OrderType) || 
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            var quotationDetails = request.Quotation;
            if (isConsignment)
            {
                serviceFee = domesticShippingFee;
            }
            else
            {
                estimatedFreightCharge = quotationDetails?.EstimatedFreightCharge ?? estimatedFreightCharge;
                serviceFee = quotationDetails?.ServiceFee ?? domesticShippingFee;
            }

            decimal vatAmount = 0m;
            decimal importTax = 0m;
            if (quotationDetails != null)
            {
                vatAmount = quotationDetails.Vat ?? 0m;
                importTax = quotationDetails.ImportTax ?? 0m;
            }

            if (vatAmount == 0m && importTax == 0m)
            {
                var taxResult = await CalculateTaxesAsync(orderItems, estimatedFreightCharge, serviceFee, now);
                vatAmount = taxResult.VatAmount;
                importTax = taxResult.ImportTax;
                quotationFees.AddRange(taxResult.TaxFees);
            }
            else
            {
                if (vatAmount > 0)
                {
                    quotationFees.Add(new QuotationFee
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
                if (importTax > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = "Thuế nhập khẩu",
                        FeeType = "TAX",
                        CalculationType = "FIXED",
                        Value = 0m,
                        Amount = importTax,
                        Note = "Thuế nhập khẩu chốt",
                        CreatedAt = now
                    });
                }
            }

            taxAndDuty = importTax + vatAmount;
            totalEstimatedCost = estimatedFreightCharge + serviceFee + taxAndDuty;

            // Save service fee
            if (serviceFee > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = isConsignment ? "Phí vận chuyển nội địa" : "Phí dịch vụ cơ bản",
                    FeeType = isConsignment ? "DOMESTIC_FEE" : "SERVICE_FEE",
                    CalculationType = "FIXED",
                    Value = serviceFee,
                    Amount = serviceFee,
                    Note = isConsignment ? "Phí vận chuyển nội địa chính thức" : "Phí dịch vụ cơ bản chính thức",
                    CreatedAt = now
                });
            }

            if (request.Quotation != null && request.Quotation.AdditionalFees != null)
            {
                foreach (var feeDto in request.Quotation.AdditionalFees)
                {
                    PricingRule rule = null;
                    if (feeDto.FeeId.HasValue)
                    {
                        rule = await _unitOfWork.Repository<PricingRule>().GetByIdAsync(feeDto.FeeId.Value);
                        if (rule == null)
                        {
                            throw new ArgumentException($"Không tìm thấy quy định phí với ID: {feeDto.FeeId.Value}");
                        }
                        if (!string.Equals(rule.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ArgumentException($"Quy định phí '{rule.RuleName}' không còn hoạt động.");
                        }
                    }

                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        PricingRuleId = feeDto.FeeId,
                        FeeName = feeDto.Label ?? rule?.RuleName ?? "Phụ phí bổ sung",
                        FeeType = "SURCHARGE",
                        CalculationType = rule?.CalculationType ?? "FIXED",
                        Value = rule?.Value ?? (feeDto.Amount > 0 ? feeDto.Amount : 0m),
                        Amount = feeDto.Enabled ? feeDto.Amount : 0m,
                        Note = rule?.Description ?? "Phụ phí bổ sung",
                        CreatedAt = now
                    });
                }
            }

            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                QuoteType = "OFFICIAL",
                Status = "PENDING",
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                Vat = vatAmount,
                ImportTax = importTax,
                TotalVolume = totalVolumeCm3,
                CreatedAt = now,
                ExpiredAt = now.AddDays(QuotationValidDays),
                ServicePricingId = matchedPricing?.Id
            };

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            await quotationRepo.AddAsync(quotation);

            foreach (var qf in quotationFees)
            {
                qf.QuotationId = quotation.Id;
                await _unitOfWork.Repository<QuotationFee>().AddAsync(qf);
            }

            order.Status = "QUOTATION_SENT";
            order.TotalWeight = totalActualWeight > 0m ? totalActualWeight : order.TotalWeight;
            order.TotalVolume = totalVolumeCm3 > 0m ? totalVolumeCm3 : order.TotalVolume;
            orderRepo.Update(order);

            await _unitOfWork.SaveChangesAsync();

            return new QuotationActionResponse
            {
                Message = "Đã gửi báo giá cho khách hàng.",
                Status = "QUOTATION_SENT",
                Consignment = new ConsignmentResponseDto
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode,
                    Status = order.Status,
                    ConsignmentType = order.ConsignmentType,
                    TotalVolume = order.TotalVolume,
                    TotalWeight = order.TotalWeight,
                    Route = order.Route,
                    CreatedAt = order.CreatedAt
                }
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> RejectQuotationByOrderIdAsync(Guid orderId, string reason)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            order.Status = "QUOTATION_REJECTED";
            order.Note = $"Từ chối báo giá: {reason}";
            orderRepo.Update(order);

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var latestQuotation = allQuotations
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (latestQuotation != null)
            {
                latestQuotation.Status = "REJECTED";
                quotationRepo.Update(latestQuotation);
            }

            await _unitOfWork.SaveChangesAsync();

            return new QuotationActionResponse
            {
                Message = "Đã từ chối báo giá.",
                Status = "QUOTATION_REJECTED",
                RejectionReason = reason,
                Consignment = new ConsignmentResponseDto
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode,
                    Status = order.Status,
                    ConsignmentType = order.ConsignmentType,
                    TotalVolume = order.TotalVolume,
                    TotalWeight = order.TotalWeight,
                    Route = order.Route,
                    CreatedAt = order.CreatedAt
                }
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> RejectQuotationByIdAsync(Guid quotationId, string reason)
        {
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);
            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            quotation.Status = "REJECTED";
            quotationRepo.Update(quotation);

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);
            if (order != null)
            {
                order.Status = "QUOTATION_REJECTED";
                order.Note = $"Từ chối báo giá: {reason}";
                orderRepo.Update(order);
            }

            await _unitOfWork.SaveChangesAsync();

            return new QuotationActionResponse
            {
                Message = "Đã từ chối báo giá.",
                Status = "QUOTATION_REJECTED",
                RejectionReason = reason,
                Consignment = order != null ? new ConsignmentResponseDto
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode,
                    Status = order.Status,
                    ConsignmentType = order.ConsignmentType,
                    TotalVolume = order.TotalVolume,
                    TotalWeight = order.TotalWeight,
                    Route = order.Route,
                    CreatedAt = order.CreatedAt
                } : null
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationDetailResponse> GetQuotationByOrderIdAsync(Guid orderId)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();

            // Lấy báo giá mới nhất của đơn hàng (ưu tiên SENT/PENDING, sau đó DRAFT/ACCEPTED)
            var quotation = allQuotations
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault(q =>
                    string.Equals(q.Status, "SENT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Status, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Status, "DRAFT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase));

            if (quotation == null)
                throw new NotFoundException("Đơn hàng chưa có báo giá nào. Vui lòng liên hệ nhân viên hỗ trợ.");

            // Fetch rules first to get VolumetricDivisor
            var rules = await _unitOfWork.Repository<PricingRule>().GetAllAsync();
            var ruleMap = rules.ToDictionary(r => r.Id, r => r);

            var allActiveRules = rules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Tính VolumetricWeight từ tổng thể tích đơn hàng (TotalVolume lưu theo cm³)
            var divisor = GetVolumetricDivisor(allActiveRules);
            var volumetricWeight = Math.Round((order.TotalVolume ?? 0m) / divisor, 4);

            // Bổ sung các PricingRule đang hoạt động nếu chưa có trong DB của báo giá này
            var activeRules = allActiveRules
                .Where(r => !string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Fetch associated quotation fees
            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            var allFees = await feeRepo.GetAllAsync();
            var fees = allFees.Where(f => f.QuotationId == quotation.Id).ToList();

            var itemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await itemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == order.Id).ToList();
            var packageCount = orderItems.Sum(item => item.Quantity ?? 1);

            var feeDtos = fees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase))
                .Select(f => MapToFeeResponseDto(f, ruleMap, packageCount))
                .ToList();

            var additionalSurchargesAmount = fees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase) && (f.Amount > 0 || f.PricingRuleId.HasValue)) // keep original logic or check enabled/amount
                .Sum(f => f.Amount);

            var totalEstimatedCost = (quotation.EstimatedFreightCharge ?? 0m)
                                   + (quotation.ServiceFee ?? 0m)
                                   + (quotation.TaxAndDuty ?? 0m)
                                   + additionalSurchargesAmount;

            var parcelDetails = new List<ParcelQuotationDetailDto>();
            var mainServiceFees = fees.Where(f => string.Equals(f.FeeType, "MAIN_SERVICE", StringComparison.OrdinalIgnoreCase) && f.ParcelId.HasValue).ToList();
            if (mainServiceFees.Any())
            {
                var parcelRepo = _unitOfWork.Repository<Parcel>();
                var parcelIds = mainServiceFees.Select(f => f.ParcelId!.Value).ToList();
                var associatedParcels = (await parcelRepo.GetAllAsync())
                    .Where(p => parcelIds.Contains(p.Id))
                    .ToDictionary(p => p.Id, p => p);

                foreach (var fee in mainServiceFees)
                {
                    if (associatedParcels.TryGetValue(fee.ParcelId!.Value, out var parcel))
                    {
                        parcelDetails.Add(new ParcelQuotationDetailDto
                        {
                            ParcelId = parcel.Id,
                            PackageCode = parcel.PackageCode ?? string.Empty,
                            ActualWeight = parcel.ActualWeight ?? 0m,
                            Length = parcel.Length ?? 0m,
                            Width = parcel.Width ?? 0m,
                            Height = parcel.Height ?? 0m,
                            VolumetricWeight = parcel.VolumetricWeight ?? 0m,
                            ChargeableWeight = fee.ChargeableWeight ?? parcel.ChargeableWeight ?? 0m,
                            ShippingFee = fee.Amount
                        });
                    }
                }
            }

            return new QuotationDetailResponse
            {
                QuotationId = quotation.Id,
                OrderId = orderId,
                QuoteType = quotation.QuoteType ?? string.Empty,
                Status = quotation.Status ?? string.Empty,
                ConsignmentType = order.ConsignmentType ?? string.Empty,
                ConsignmentCode = order.ConsignmentCode,
                TotalWeight = order.TotalWeight ?? 0m,
                TotalVolume = order.TotalVolume ?? 0m,
                VolumetricWeight = volumetricWeight,
                ChargeableWeight = Math.Max(order.TotalWeight ?? 0m, volumetricWeight),
                EstimatedFreightCharge = quotation.EstimatedFreightCharge ?? 0m,
                ServiceFee = quotation.ServiceFee ?? 0m,
                TaxAndDuty = quotation.TaxAndDuty ?? 0m,
                Vat = quotation.Vat ?? 0m,
                ImportTax = quotation.ImportTax ?? 0m,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                ExpiredAt = quotation.ExpiredAt,
                SalesNote = null,
                AdditionalFees = feeDtos,
                Parcels = parcelDetails
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationDetailResponse> GetQuotationByIdAsync(Guid quotationId)
        {
            // 1. Tìm Quotation
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);
            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            // 2. Tìm Order
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng liên quan đến báo giá.");

            // 3. Lấy Route & Khách hàng
            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = order.CustomerId.HasValue
                ? await customerRepo.GetByIdAsync(order.CustomerId.Value)
                : null;
            var customerCountry = customer?.Country;

            // 4. Lấy cấu hình hệ thống (Pricing rules, Volumetric Divisor)
            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var allActiveRules = allRules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var divisor = GetVolumetricDivisor(allActiveRules);
            var volumetricWeight = Math.Round((order.TotalVolume ?? 0m) / divisor, 4);

            var activeRules = allActiveRules
                .Where(r => !string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var ruleMap = activeRules.ToDictionary(r => r.Id, r => r);

            // Fetch associated quotation fees
            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            var allFees = await feeRepo.GetAllAsync();
            var fees = allFees.Where(f => f.QuotationId == quotation.Id).ToList();

            var itemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await itemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == order.Id).ToList();
            var packageCount = orderItems.Sum(item => item.Quantity ?? 1);

            var feeDtos = fees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase))
                .Select(f => MapToFeeResponseDto(f, ruleMap, packageCount))
                .ToList();

            var additionalSurchargesAmount = fees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase) && (f.Amount > 0 || f.PricingRuleId.HasValue))
                .Sum(f => f.Amount);

            var totalEstimatedCost = (quotation.EstimatedFreightCharge ?? 0m)
                                   + (quotation.ServiceFee ?? 0m)
                                   + (quotation.TaxAndDuty ?? 0m)
                                   + additionalSurchargesAmount;

            var parcelDetails = new List<ParcelQuotationDetailDto>();
            var mainServiceFees = fees.Where(f => string.Equals(f.FeeType, "MAIN_SERVICE", StringComparison.OrdinalIgnoreCase) && f.ParcelId.HasValue).ToList();
            if (mainServiceFees.Any())
            {
                var parcelRepo = _unitOfWork.Repository<Parcel>();
                var parcelIds = mainServiceFees.Select(f => f.ParcelId!.Value).ToList();
                var associatedParcels = (await parcelRepo.GetAllAsync())
                    .Where(p => parcelIds.Contains(p.Id))
                    .ToDictionary(p => p.Id, p => p);

                foreach (var fee in mainServiceFees)
                {
                    if (associatedParcels.TryGetValue(fee.ParcelId!.Value, out var parcel))
                    {
                        parcelDetails.Add(new ParcelQuotationDetailDto
                        {
                            ParcelId = parcel.Id,
                            PackageCode = parcel.PackageCode ?? string.Empty,
                            ActualWeight = parcel.ActualWeight ?? 0m,
                            Length = parcel.Length ?? 0m,
                            Width = parcel.Width ?? 0m,
                            Height = parcel.Height ?? 0m,
                            VolumetricWeight = parcel.VolumetricWeight ?? 0m,
                            ChargeableWeight = fee.ChargeableWeight ?? parcel.ChargeableWeight ?? 0m,
                            ShippingFee = fee.Amount
                        });
                    }
                }
            }

            return new QuotationDetailResponse
            {
                QuotationId = quotation.Id,
                OrderId = order.Id,
                QuoteType = quotation.QuoteType ?? string.Empty,
                Status = quotation.Status ?? string.Empty,
                ConsignmentType = order.ConsignmentType ?? string.Empty,
                ConsignmentCode = order.ConsignmentCode,
                TotalWeight = order.TotalWeight ?? 0m,
                TotalVolume = order.TotalVolume ?? 0m,
                VolumetricWeight = volumetricWeight,
                ChargeableWeight = Math.Max(order.TotalWeight ?? 0m, volumetricWeight),
                EstimatedFreightCharge = quotation.EstimatedFreightCharge ?? 0m,
                ServiceFee = quotation.ServiceFee ?? 0m,
                TaxAndDuty = quotation.TaxAndDuty ?? 0m,
                Vat = quotation.Vat ?? 0m,
                ImportTax = quotation.ImportTax ?? 0m,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                ExpiredAt = quotation.ExpiredAt,
                SalesNote = null,
                AdditionalFees = feeDtos,
                Parcels = parcelDetails
            };
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
    }
}
