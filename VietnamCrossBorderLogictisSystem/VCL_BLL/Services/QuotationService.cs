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
    public class QuotationService : IQuotationService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Giá mặc định khi không tìm thấy ServicePricing trong DB (VND/kg)
        private const decimal DefaultExpressRate = 50000m;
        private const decimal DefaultStandardRate = 30000m;

        // Phí dịch vụ cố định (VND)
        private const decimal ExpressServiceFee = 50000m;
        private const decimal StandardServiceFee = 30000m;

        // Tỷ lệ thuế tạm tính trên tổng declared value
        private const decimal TaxRate = 0.10m;

        // Hệ số quy đổi thể tích → cân nặng (tiêu chuẩn IATA)
        private const decimal VolumetricDivisor = 5000m;

        // Báo giá tạm tính có hiệu lực 7 ngày
        private const int QuotationValidDays = 7;

        public QuotationService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
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

                totalWeight += itemWeight * qty;
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
            var volumetricWeight = totalVolume / VolumetricDivisor;
            var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);
            var chargeableWeight = Math.Max(totalWeight, Math.Max(volumetricWeight, minimumWeight));

            decimal estimatedFreightCharge;

            if (matchedPricing != null && matchedPricing.Price.HasValue)
            {
                estimatedFreightCharge = Math.Round(chargeableWeight * matchedPricing.Price.Value, 2);
            }
            else
            {
                var freightRate = IsExpressServiceType(consignmentType)
                    ? DefaultExpressRate
                    : DefaultStandardRate;
                estimatedFreightCharge = Math.Round(chargeableWeight * freightRate, 2);
            }

            decimal serviceFee;
            decimal taxAndDuty;
            decimal totalEstimatedCost;

            bool isConsignment = string.IsNullOrEmpty(order.OrderType) || 
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            var quotationFees = new List<QuotationFee>();
            var now = DateTime.UtcNow;

            if (estimatedFreightCharge > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Cước vận chuyển quốc tế",
                    FeeType = "MAIN_SERVICE",
                    CalculationType = matchedPricing != null ? "PER_KG" : "FIXED",
                    Value = matchedPricing?.Price ?? (IsExpressServiceType(consignmentType) ? DefaultExpressRate : DefaultStandardRate),
                    Amount = estimatedFreightCharge,
                    Note = $"Cước vận chuyển quốc tế dựa trên cân nặng tính cước {Math.Round(chargeableWeight, 2)} kg",
                    CreatedAt = now
                });
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
                taxAndDuty = Math.Round(totalDeclaredValue * TaxRate, 2);
                totalEstimatedCost = estimatedFreightCharge + serviceFee + taxAndDuty;

                if (taxAndDuty > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = "Thuế & phí hải quan tạm tính",
                        FeeType = "TAX",
                        CalculationType = "PERCENTAGE",
                        Value = TaxRate * 100m,
                        Amount = taxAndDuty,
                        Note = "10% giá trị khai báo hàng hóa",
                        CreatedAt = now
                    });
                }
            }

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
                TotalVolume = totalVolume / 1000000m,
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
            order.TotalVolume = totalVolume / 1000000m;
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
                TotalVolume = totalVolume / 1000000m,
                VolumetricWeight = Math.Round(volumetricWeight, 2),
                ChargeableWeight = Math.Round(chargeableWeight, 2),
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = now,
                ExpiredAt = quotation.ExpiredAt,
                Items = itemDetails,
                AdditionalFees = quotationFees.Select(f => MapToFeeResponseDto(f, activeRules.ToDictionary(r => r.Id, r => r), orderItems.Sum(item => item.Quantity ?? 1))).ToList()
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

                    totalWeight += itemWeight * qty;
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
            var volumetricWeight = totalVolume / VolumetricDivisor;
            var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);
            var chargeableWeight = Math.Max(totalWeight, Math.Max(volumetricWeight, minimumWeight));

            decimal estimatedFreightCharge;

            if (matchedPricing != null && matchedPricing.Price.HasValue)
            {
                estimatedFreightCharge = Math.Round(chargeableWeight * matchedPricing.Price.Value, 2);
            }
            else
            {
                var freightRate = IsExpressServiceType(consignmentType)
                    ? DefaultExpressRate
                    : DefaultStandardRate;
                estimatedFreightCharge = Math.Round(chargeableWeight * freightRate, 2);
            }

            decimal serviceFee;
            decimal taxAndDuty;
            decimal totalEstimatedCost;

            bool isConsignment = string.IsNullOrEmpty(order.OrderType) || 
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            var quotationFees = new List<QuotationFee>();
            var now = DateTime.UtcNow;

            if (estimatedFreightCharge > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.Empty,
                    FeeName = "Cước vận chuyển quốc tế",
                    FeeType = "MAIN_SERVICE",
                    CalculationType = matchedPricing != null ? "PER_KG" : "FIXED",
                    Value = matchedPricing?.Price ?? (IsExpressServiceType(consignmentType) ? DefaultExpressRate : DefaultStandardRate),
                    Amount = estimatedFreightCharge,
                    Note = $"Cước vận chuyển quốc tế dựa trên cân nặng tính cước {Math.Round(chargeableWeight, 2)} kg",
                    CreatedAt = now
                });
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

                serviceFee = baseServiceFee + totalSurcharges;
                taxAndDuty = Math.Round(totalDeclaredValue * TaxRate, 2);
                totalEstimatedCost = estimatedFreightCharge + serviceFee + taxAndDuty;

                if (taxAndDuty > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.Empty,
                        FeeName = "Thuế & phí hải quan tạm tính",
                        FeeType = "TAX",
                        CalculationType = "PERCENTAGE",
                        Value = TaxRate * 100m,
                        Amount = taxAndDuty,
                        Note = "10% giá trị khai báo hàng hóa",
                        CreatedAt = now
                    });
                }
            }

            var activeRulesDict = activeRules.ToDictionary(r => r.Id, r => r);
            var totalQty = orderItems.Sum(item => item.Quantity ?? 1);
            var additionalFees = quotationFees.Select(f => MapToFeeResponseDto(f, activeRulesDict, totalQty)).ToList();

            return new EstimateQuotationResponse
            {
                QuotationId = Guid.Empty,
                OrderId = orderId,
                ConsignmentType = consignmentType,
                QuoteType = "ESTIMATE",
                Status = "DRAFT",
                TotalWeight = totalWeight,
                TotalVolume = totalVolume / 1000000m,
                VolumetricWeight = Math.Round(volumetricWeight, 2),
                ChargeableWeight = Math.Round(chargeableWeight, 2),
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = DateTime.UtcNow,
                ExpiredAt = DateTime.UtcNow.AddDays(QuotationValidDays),
                Items = itemDetails,
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

            // Calculate actual measured data from warehouse parcels
            decimal totalActualWeight = 0m;
            decimal totalVolumetricWeight = 0m;
            decimal totalVolumeCm3 = 0m;
            decimal domesticShippingFee = 0m;

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcels = (await parcelRepo.GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .ToList();

            if (parcels.Any())
            {
                totalActualWeight = parcels.Sum(p => p.ActualWeight ?? 0m);
                totalVolumetricWeight = parcels.Sum(p => p.VolumetricWeight ?? 
                    (((p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m)) / VolumetricDivisor));
                totalVolumeCm3 = parcels.Sum(p => (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m));

                var parcelIds = parcels.Select(p => p.Id).ToList();
                var waybillRepo = _unitOfWork.Repository<DomesticWaybill>();
                var allWaybills = await waybillRepo.GetAllAsync();
                domesticShippingFee = allWaybills
                    .Where(w => w.ParcelId.HasValue && parcelIds.Contains(w.ParcelId.Value))
                    .Sum(w => w.DeliveryFee ?? 0m);
            }
            else
            {
                // Fallback to request details or items
                totalActualWeight = request.WeightKg ?? order.TotalWeight ?? 0m;

                var orderItemRepo = _unitOfWork.Repository<OrderItem>();
                var allItems = await orderItemRepo.GetAllAsync();
                var orderItems = allItems.Where(i => i.OrderId == orderId).ToList();

                if (request.VolumeM3.HasValue)
                {
                    totalVolumeCm3 = request.VolumeM3.Value * 1000000m;
                }
                else if (order.TotalVolume.HasValue)
                {
                    totalVolumeCm3 = order.TotalVolume.Value * 1000000m;
                }
                else
                {
                    totalVolumeCm3 = orderItems.Sum(item => (item.Length ?? 0m) * (item.Width ?? 0m) * (item.Height ?? 0m));
                }
                totalVolumetricWeight = totalVolumeCm3 / VolumetricDivisor;

                domesticShippingFee = GetDomesticShippingFeeEstimate(activeRules, matchedPricing);
            }

            var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);
            var netWeight = Math.Max(totalActualWeight, Math.Max(totalVolumetricWeight, minimumWeight));

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

            decimal estimatedFreightCharge;
            decimal serviceFee;
            decimal taxAndDuty;
            decimal totalEstimatedCost;

            bool isConsignment = string.IsNullOrEmpty(order.OrderType) || 
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            var quotationDetails = request.Quotation;
            if (isConsignment)
            {
                estimatedFreightCharge = Math.Round(netWeight * freightRate, 2);
                serviceFee = domesticShippingFee;
                taxAndDuty = 0m;
                totalEstimatedCost = estimatedFreightCharge + serviceFee;
            }
            else
            {
                estimatedFreightCharge = quotationDetails?.EstimatedFreightCharge ?? 0m;
                serviceFee = quotationDetails?.ServiceFee ?? 0m;
                taxAndDuty = quotationDetails?.TotalEstimatedCost.HasValue ?? false
                    ? Math.Max(0m, quotationDetails.TotalEstimatedCost.Value - (quotationDetails.EstimatedFreightCharge ?? 0m) - (quotationDetails.ServiceFee ?? 0m))
                    : 0m;
                totalEstimatedCost = estimatedFreightCharge + serviceFee + taxAndDuty;
            }

            var now = DateTime.UtcNow;
            var quotationFees = new List<QuotationFee>();

            // Save main freight charge
            if (estimatedFreightCharge > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Cước vận chuyển quốc tế",
                    FeeType = "MAIN_SERVICE",
                    CalculationType = matchedPricing != null ? "PER_KG" : "FIXED",
                    Value = matchedPricing?.Price ?? (IsExpressServiceType(consignmentType) ? DefaultExpressRate : DefaultStandardRate),
                    Amount = estimatedFreightCharge,
                    Note = $"Cước vận chuyển quốc tế chính thức",
                    CreatedAt = now
                });
            }

            // Save tax and duty
            if (taxAndDuty > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Thuế & phí hải quan tạm tính",
                    FeeType = "TAX",
                    CalculationType = "PERCENTAGE",
                    Value = TaxRate * 100m,
                    Amount = taxAndDuty,
                    Note = "10% giá trị khai báo hàng hóa",
                    CreatedAt = now
                });
            }

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
                TotalVolume = totalVolumeCm3 / 1000000m,
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
            order.TotalVolume = totalVolumeCm3 > 0m ? totalVolumeCm3 / 1000000m : order.TotalVolume;
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

            // Tính VolumetricWeight từ tổng thể tích đơn hàng (TotalVolume lưu theo m³)
            var volumetricWeight = Math.Round((order.TotalVolume ?? 0m) * 1_000_000m / VolumetricDivisor, 2);
            var totalEstimatedCost = (quotation.EstimatedFreightCharge ?? 0m)
                                   + (quotation.ServiceFee ?? 0m)
                                   + (quotation.TaxAndDuty ?? 0m);

            // Fetch associated quotation fees
            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            var allFees = await feeRepo.GetAllAsync();
            var fees = allFees.Where(f => f.QuotationId == quotation.Id).ToList();

            var rules = await _unitOfWork.Repository<PricingRule>().GetAllAsync();
            var ruleMap = rules.ToDictionary(r => r.Id, r => r);

            // Bổ sung các PricingRule đang hoạt động nếu chưa có trong DB của báo giá này
            var activeRules = rules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .Where(r => !string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var existingRuleIds = fees
                .Where(f => f.PricingRuleId.HasValue)
                .Select(f => f.PricingRuleId!.Value)
                .ToHashSet();

            foreach (var rule in activeRules)
            {
                if (rule.ServicePricingId.HasValue && quotation.ServicePricingId.HasValue && rule.ServicePricingId.Value != quotation.ServicePricingId.Value)
                {
                    continue;
                }

                if (!existingRuleIds.Contains(rule.Id))
                {
                    fees.Add(new QuotationFee
                    {
                        Id = Guid.Empty,
                        QuotationId = quotation.Id,
                        PricingRuleId = rule.Id,
                        FeeName = rule.RuleName,
                        FeeType = "SURCHARGE",
                        CalculationType = rule.CalculationType,
                        Value = rule.Value,
                        Amount = 0m,
                        Note = rule.Description,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            var itemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await itemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == order.Id).ToList();
            var packageCount = orderItems.Sum(item => item.Quantity ?? 1);

            var feeDtos = fees.Select(f => MapToFeeResponseDto(f, ruleMap, packageCount)).ToList();

            return new QuotationDetailResponse
            {
                QuotationId = quotation.Id,
                OrderId = orderId,
                QuoteType = quotation.QuoteType ?? string.Empty,
                Status = quotation.Status ?? string.Empty,
                ConsignmentType = order.ConsignmentType ?? string.Empty,
                TotalWeight = order.TotalWeight ?? 0m,
                TotalVolume = order.TotalVolume ?? 0m,
                VolumetricWeight = volumetricWeight,
                ChargeableWeight = Math.Max(order.TotalWeight ?? 0m, volumetricWeight),
                EstimatedFreightCharge = quotation.EstimatedFreightCharge ?? 0m,
                ServiceFee = quotation.ServiceFee ?? 0m,
                TaxAndDuty = quotation.TaxAndDuty ?? 0m,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                ExpiredAt = quotation.ExpiredAt,
                SalesNote = null,
                AdditionalFees = feeDtos
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
                CreatedAt = f.CreatedAt
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

    }
}
