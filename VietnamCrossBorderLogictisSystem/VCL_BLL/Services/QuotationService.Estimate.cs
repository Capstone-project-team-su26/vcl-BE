using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class QuotationService
    {
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
            var allPricings = (await servicePricingRepo.GetAllAsync()).Where(p => !p.IsDeleted).ToList();

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
                    detail.VolumetricWeight = Math.Round(detail.ItemVolume / divisor, 4);
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
                // Convert m³ to cm³. If the input value is already > 1000, we assume it is already in cm³
                totalVolume = request.VolumeM3.Value > 1000m ? request.VolumeM3.Value : request.VolumeM3.Value * 1000000m;
            }
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == orderId).ToList();

            decimal totalDeclaredValue = request.DeclaredValue ?? 0m;
            if (totalDeclaredValue == 0m)
            {
                totalDeclaredValue = orderItems.Sum(item => item.DeclaredValue ?? 0m);
            }

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
                    totalVolume += itemVolume;

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
            var allPricings = (await servicePricingRepo.GetAllAsync()).Where(p => !p.IsDeleted).ToList();

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
                    detail.VolumetricWeight = Math.Round(detail.ItemVolume / divisor, 4);
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
    }
}
