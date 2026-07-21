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
            var allPricings = (await servicePricingRepo.GetAllAsync()).Where(p => !p.IsDeleted).ToList();

            ServicePricing matchedPricing = null;
            var targetPricingId = request.ServicePricingId ?? request.Quotation?.ServicePricingId;
            if (targetPricingId.HasValue && targetPricingId.Value != Guid.Empty)
            {
                matchedPricing = allPricings.FirstOrDefault(p => p.Id == targetPricingId.Value);
            }

            if (matchedPricing == null)
            {
                matchedPricing = allPricings
                    .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                    .OrderByDescending(p => p.EffectiveDate)
                    .FirstOrDefault(p =>
                        string.IsNullOrWhiteSpace(route) ||
                        string.Equals(
                            OrderService.NormalizeRoute($"{p.OriginCountry}-{p.DestinationCountry}"), 
                            OrderService.NormalizeRoute(route), 
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            OrderService.NormalizeRoute(p.OriginCountry), 
                            OrderService.NormalizeRoute(route), 
                            StringComparison.OrdinalIgnoreCase)
                    );
            }

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
                    // Convert m³ to cm³. If the input value is already > 1000, we assume it is already in cm³
                    totalVolumeCm3 = request.VolumeM3.Value > 1000m ? request.VolumeM3.Value : request.VolumeM3.Value * 1000000m;
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

            decimal baseServiceFee;
            decimal serviceFee;
            decimal taxAndDuty;
            decimal totalEstimatedCost;

            bool isConsignment = string.IsNullOrEmpty(order.OrderType) || 
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            var quotationDetails = request.Quotation;
            if (isConsignment)
            {
                baseServiceFee = domesticShippingFee;
            }
            else
            {
                estimatedFreightCharge = quotationDetails?.EstimatedFreightCharge ?? estimatedFreightCharge;
                baseServiceFee = quotationDetails?.ServiceFee ?? domesticShippingFee;
            }

            decimal totalSurcharges = 0m;
            var surchargeFees = new List<QuotationFee>();
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

                        if (isConsignment && (string.Equals(rule.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(rule.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }

                    decimal feeAmount = feeDto.Enabled ? feeDto.Amount : 0m;
                    totalSurcharges += feeAmount;

                    surchargeFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        PricingRuleId = feeDto.FeeId,
                        FeeName = feeDto.Label ?? rule?.RuleName ?? "Phụ phí bổ sung",
                        FeeType = "SURCHARGE",
                        CalculationType = rule?.CalculationType ?? "FIXED",
                        Value = rule?.Value ?? (feeDto.Amount > 0 ? feeDto.Amount : 0m),
                        Amount = feeAmount,
                        Note = rule?.Description ?? "Phụ phí bổ sung",
                        CreatedAt = now
                    });
                }
            }

            var packageFees = await CalculatePackageFeesAsync(orderItems, now);
            quotationFees.AddRange(packageFees);
            decimal totalPackageFees = packageFees.Sum(f => f.Amount);

            serviceFee = baseServiceFee + totalSurcharges + totalPackageFees;

            decimal vatAmount = 0m;
            decimal importTax = 0m;
            if (quotationDetails != null)
            {
                vatAmount = quotationDetails.Vat ?? 0m;
                importTax = quotationDetails.ImportTax ?? 0m;
            }

            if (vatAmount == 0m && importTax == 0m)
            {
                var taxResult = await CalculateTaxesAsync(orderItems, estimatedFreightCharge, serviceFee, now, activeRules);
                vatAmount = taxResult.VatAmount;
                importTax = taxResult.ImportTax;
                quotationFees.AddRange(taxResult.TaxFees);
            }
            else
            {
                var vatRatePercent = GetVatRatePercent(activeRules);
                if (vatAmount > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = $"Thuế giá trị gia tăng (VAT {vatRatePercent:G29}%)",
                        FeeType = "TAX",
                        CalculationType = "PERCENTAGE",
                        Value = vatRatePercent,
                        Amount = vatAmount,
                        Note = $"{vatRatePercent:G29}% phí dịch vụ logistics",
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
            if (baseServiceFee > 0)
            {
                var domesticRule = activeRules.FirstOrDefault(r =>
                    (r.ServicePricingId == matchedPricing?.Id || r.ServicePricingId == null) &&
                    (string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.ConditionType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     r.RuleName.Contains("nội địa", StringComparison.OrdinalIgnoreCase))
                );

                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    PricingRuleId = isConsignment ? domesticRule?.Id : null,
                    FeeName = isConsignment ? (domesticRule?.RuleName ?? "Phí vận chuyển nội địa") : "Phí dịch vụ cơ bản",
                    FeeType = isConsignment ? "DOMESTIC_FEE" : "SERVICE_FEE",
                    CalculationType = isConsignment ? (domesticRule?.CalculationType ?? "FIXED") : "FIXED",
                    Value = isConsignment ? (domesticRule?.Value ?? baseServiceFee) : baseServiceFee,
                    Amount = baseServiceFee,
                    Note = isConsignment ? (domesticRule?.Description ?? "Phí vận chuyển nội địa chính thức") : "Phí dịch vụ cơ bản chính thức",
                    CreatedAt = now
                });
            }

            quotationFees.AddRange(surchargeFees);

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
                ServicePricingId = matchedPricing?.Id,
                SalesNote = request.SalesNote ?? request.Quotation?.SalesNote,
                WarehouseId = request.WarehouseId
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
                    CreatedAt = order.CreatedAt,
                    WarehouseId = request.WarehouseId
                }
            };
        }
    }
}
