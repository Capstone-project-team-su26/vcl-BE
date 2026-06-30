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
        private const decimal DefaultConsolidationRate = 30000m;

        // Phí dịch vụ cố định (VND)
        private const decimal ExpressServiceFee = 50000m;
        private const decimal ConsolidationServiceFee = 30000m;

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
                var itemWeight = item.Weight ?? 0m;
                var itemLength = item.Length ?? 0m;
                var itemWidth = item.Width ?? 0m;
                var itemHeight = item.Height ?? 0m;
                var itemVolume = itemLength * itemWidth * itemHeight;
                var itemDeclaredValue = item.DeclaredValue ?? 0m;

                totalWeight += itemWeight;
                totalVolume += itemVolume;
                totalDeclaredValue += itemDeclaredValue;

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

            // 4. Tính Volumetric Weight & Chargeable Weight
            var volumetricWeight = totalVolume / VolumetricDivisor;
            var chargeableWeight = Math.Max(totalWeight, volumetricWeight);

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
                    (p.DestinationCountry != null && route.Contains(p.DestinationCountry, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(p.OriginCountry, "CN", StringComparison.OrdinalIgnoreCase) && route.Contains("TQ", StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(p.OriginCountry, "China", StringComparison.OrdinalIgnoreCase) && route.Contains("TQ", StringComparison.OrdinalIgnoreCase))
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

            decimal estimatedFreightCharge;
            decimal baseServiceFee = string.Equals(consignmentType, "Express", StringComparison.OrdinalIgnoreCase)
                ? ExpressServiceFee
                : ConsolidationServiceFee;

            if (matchedPricing != null && matchedPricing.Price.HasValue)
            {
                estimatedFreightCharge = Math.Round(chargeableWeight * matchedPricing.Price.Value, 2);
            }
            else
            {
                var freightRate = string.Equals(consignmentType, "Express", StringComparison.OrdinalIgnoreCase)
                    ? DefaultExpressRate
                    : DefaultConsolidationRate;
                estimatedFreightCharge = Math.Round(chargeableWeight * freightRate, 2);
            }

            // 6b. Tìm và áp dụng các rule phụ phí từ PricingRule
            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var activeRules = allRules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

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

                if (isApplicable)
                {
                    decimal surchargeAmount = 0m;
                    if (string.Equals(rule.CalculationType, "FIXED", StringComparison.OrdinalIgnoreCase))
                    {
                        surchargeAmount = rule.Value;
                    }
                    else if (string.Equals(rule.CalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                    {
                        surchargeAmount = totalDeclaredValue * (rule.Value / 100m);
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
            }

            decimal serviceFee = baseServiceFee + totalSurcharges;
            var taxAndDuty = Math.Round(totalDeclaredValue * TaxRate, 2);
            var totalEstimatedCost = estimatedFreightCharge + serviceFee + taxAndDuty;

            // 7. Tạo Quotation record trong DB
            var now = DateTime.UtcNow;
            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                QuoteType = "ESTIMATE",
                Status = "DRAFT",
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                TotalVolume = totalVolume,
                CreatedAt = now,
                ExpiredAt = now.AddDays(QuotationValidDays)
            };

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            await quotationRepo.AddAsync(quotation);

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
                VolumetricWeight = Math.Round(volumetricWeight, 2),
                ChargeableWeight = Math.Round(chargeableWeight, 2),
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = now,
                ExpiredAt = quotation.ExpiredAt,
                Items = itemDetails
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
            return string.Equals(consignmentType, "Express", StringComparison.OrdinalIgnoreCase)
                ? DefaultExpressRate
                : DefaultConsolidationRate;
        }

        /// <summary>
        /// Trả về phí dịch vụ cố định theo loại ký gửi.
        /// </summary>
        private static decimal GetServiceFee(string consignmentType)
        {
            return string.Equals(consignmentType, "Express", StringComparison.OrdinalIgnoreCase)
                ? ExpressServiceFee
                : ConsolidationServiceFee;
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
            decimal totalVolume = request.VolumeM3 ?? 0m;
            decimal totalDeclaredValue = request.DeclaredValue ?? 0m;

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == orderId).ToList();

            var itemDetails = new List<OrderItemDetail>();
            if (totalWeight == 0m && totalVolume == 0m)
            {
                foreach (var item in orderItems)
                {
                    var itemWeight = item.Weight ?? 0m;
                    var itemLength = item.Length ?? 0m;
                    var itemWidth = item.Width ?? 0m;
                    var itemHeight = item.Height ?? 0m;
                    var itemVolume = itemLength * itemWidth * itemHeight;
                    var itemDeclaredValue = item.DeclaredValue ?? 0m;

                    totalWeight += itemWeight;
                    totalVolume += itemVolume;
                    totalDeclaredValue += itemDeclaredValue;

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

            var volumetricWeight = totalVolume / VolumetricDivisor;
            var chargeableWeight = Math.Max(totalWeight, volumetricWeight);

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

            decimal estimatedFreightCharge;
            decimal baseServiceFee = string.Equals(consignmentType, "Express", StringComparison.OrdinalIgnoreCase)
                ? ExpressServiceFee
                : ConsolidationServiceFee;

            if (matchedPricing != null && matchedPricing.Price.HasValue)
            {
                estimatedFreightCharge = Math.Round(chargeableWeight * matchedPricing.Price.Value, 2);
            }
            else
            {
                var freightRate = string.Equals(consignmentType, "Express", StringComparison.OrdinalIgnoreCase)
                    ? DefaultExpressRate
                    : DefaultConsolidationRate;
                estimatedFreightCharge = Math.Round(chargeableWeight * freightRate, 2);
            }

            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var activeRules = allRules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

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

                if (isApplicable)
                {
                    decimal surchargeAmount = 0m;
                    if (string.Equals(rule.CalculationType, "FIXED", StringComparison.OrdinalIgnoreCase))
                        surchargeAmount = rule.Value;
                    else if (string.Equals(rule.CalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                        surchargeAmount = totalDeclaredValue * (rule.Value / 100m);
                    else if (string.Equals(rule.CalculationType, "PER_KG", StringComparison.OrdinalIgnoreCase))
                        surchargeAmount = chargeableWeight * rule.Value;

                    totalSurcharges += surchargeAmount;
                }
            }

            decimal serviceFee = baseServiceFee + totalSurcharges;
            var taxAndDuty = Math.Round(totalDeclaredValue * TaxRate, 2);
            var totalEstimatedCost = estimatedFreightCharge + serviceFee + taxAndDuty;

            return new EstimateQuotationResponse
            {
                QuotationId = Guid.Empty,
                OrderId = orderId,
                ConsignmentType = consignmentType,
                QuoteType = "ESTIMATE",
                Status = "DRAFT",
                TotalWeight = totalWeight,
                TotalVolume = totalVolume,
                VolumetricWeight = Math.Round(volumetricWeight, 2),
                ChargeableWeight = Math.Round(chargeableWeight, 2),
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = DateTime.UtcNow,
                ExpiredAt = DateTime.UtcNow.AddDays(QuotationValidDays),
                Items = itemDetails
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> SendQuotationAsync(Guid orderId, CreateQuotationRequest request)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var quotationDetails = request.Quotation;
            var now = DateTime.UtcNow;

            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                QuoteType = "OFFICIAL",
                Status = "PENDING",
                EstimatedFreightCharge = quotationDetails?.EstimatedFreightCharge ?? 0m,
                ServiceFee = quotationDetails?.ServiceFee ?? 0m,
                TaxAndDuty = quotationDetails?.TotalEstimatedCost.HasValue ?? false
                    ? Math.Max(0m, quotationDetails.TotalEstimatedCost.Value - (quotationDetails.EstimatedFreightCharge ?? 0m) - (quotationDetails.ServiceFee ?? 0m))
                    : 0m,
                TotalVolume = request.VolumeM3 ?? order.TotalVolume ?? 0m,
                CreatedAt = now,
                ExpiredAt = now.AddDays(QuotationValidDays)
            };

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            await quotationRepo.AddAsync(quotation);

            order.Status = "QUOTATION_SENT";
            if (request.WeightKg.HasValue) order.TotalWeight = request.WeightKg.Value;
            if (request.VolumeM3.HasValue) order.TotalVolume = request.VolumeM3.Value;
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
    }
}
