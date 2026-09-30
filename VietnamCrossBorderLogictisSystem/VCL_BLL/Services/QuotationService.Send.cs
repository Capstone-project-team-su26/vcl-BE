using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class QuotationService
    {
        // Chênh lệch dưới 1đ là do làm tròn, không coi là Sales nhập giá tay.
        private const decimal OverrideTolerance = 1m;

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> SendQuotationAsync(Guid orderId, CreateQuotationRequest request, Guid actorId)
        {
            request ??= new CreateQuotationRequest();

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var orderQuotations = (await quotationRepo.GetAllAsync()).Where(q => q.OrderId == orderId).ToList();

            // Đơn phải còn ở mốc báo giá; báo giá đã tới tay khách (gửi thẳng hoặc Admin đã duyệt giá ngoại
            // lệ) thì không lập đè — chỉ lập lại khi khách từ chối, hết hạn, hoặc Admin từ chối giá ngoại lệ.
            var requote = QuotationAcceptanceRules.EvaluateRequote(order, orderQuotations, DateTime.UtcNow);
            if (!requote.Allowed)
            {
                if (requote.State == QuotationAcceptanceRules.RequoteBlockedSentToCustomer
                    || requote.State == QuotationAcceptanceRules.RequoteBlockedApprovedSent)
                {
                    throw new ConflictException(requote.Reason!);
                }

                throw new ArgumentException(requote.Reason);
            }

            var warehouse = await ResolveQuotationWarehouseAsync(request.WarehouseId);

            var customerRepo = _unitOfWork.Repository<Customer>();
            var customer = order.CustomerId.HasValue
                ? await customerRepo.GetByIdAsync(order.CustomerId.Value)
                : null;
            var customerCountry = customer?.Country;

            var consignmentType = request.ServiceType ?? order.ConsignmentType;
            if (string.IsNullOrWhiteSpace(consignmentType))
                throw new ArgumentException("Đơn chưa có phương án vận chuyển nên chưa chọn được bảng giá.");

            var route = order.Route ?? customerCountry;
            var matchedPricing = await ResolveServicePricingAsync(request, consignmentType, route);
            var freightRate = matchedPricing.Price!.Value;

            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var activeRules = allRules
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var divisor = GetVolumetricDivisor(activeRules, matchedPricing.Id);

            decimal totalActualWeight;
            decimal totalVolumeCm3;
            decimal domesticShippingFee = 0m;

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            // Kiện gốc đã tách (SPLIT) hoặc đã gộp (MERGED) vẫn nằm trong bảng; kiện mới đã thay thế
            // chúng. Không loại ra thì đơn từng tách/gộp kiện bị tính cước hai lần.
            var parcels = (await parcelRepo.GetAllAsync())
                .Where(p => p.ConsignmentId == orderId
                            && !string.Equals(p.PackageStatus, "SPLIT", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(p.PackageStatus, "MERGED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var now = DateTime.UtcNow;
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var orderItems = allItems.Where(i => i.OrderId == orderId).ToList();

            var quotationFees = new List<QuotationFee>();

            decimal estimatedFreightCharge;
            decimal netWeight;

            if (parcels.Any())
            {
                var parcelResult = CalculateParcelFees(parcels, divisor, freightRate, now);
                totalActualWeight = parcelResult.TotalWeight;
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
                totalActualWeight = request.WeightKg ?? order.TotalWeight ?? 0m;

                if (request.VolumeM3.HasValue)
                {
                    // m³ → cm³. Giá trị > 1000 coi như client đã gửi sẵn cm³.
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

                var totalVolumetricWeight = totalVolumeCm3 / divisor;
                domesticShippingFee = GetDomesticShippingFeeEstimate(activeRules, matchedPricing);

                var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);
                netWeight = Math.Max(totalActualWeight, Math.Max(totalVolumetricWeight, minimumWeight));

                estimatedFreightCharge = Math.Round(netWeight * freightRate, 0);

                if (estimatedFreightCharge > 0)
                {
                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = "Cước vận chuyển quốc tế",
                        FeeType = "MAIN_SERVICE",
                        CalculationType = "PER_KG",
                        Value = freightRate,
                        Amount = estimatedFreightCharge,
                        Note = "Cước vận chuyển quốc tế chính thức",
                        CreatedAt = now
                    });
                }
            }

            bool isConsignment = string.IsNullOrEmpty(order.OrderType) ||
                                 string.Equals(order.OrderType, "CONSIGNMENT", StringComparison.OrdinalIgnoreCase);

            var quotationDetails = request.Quotation;
            var overrideLines = new List<string>();

            decimal baseServiceFee;
            if (isConsignment)
            {
                baseServiceFee = domesticShippingFee;
            }
            else
            {
                // Đơn kho sinh từ mua hộ: Sales được nhập cước/phí dịch vụ, nhưng khác số tính theo
                // bảng giá thì là giá ngoại lệ.
                var computedFreight = estimatedFreightCharge;
                if (quotationDetails?.EstimatedFreightCharge is decimal manualFreight
                    && Math.Abs(manualFreight - computedFreight) > OverrideTolerance)
                {
                    overrideLines.Add($"Cước quốc tế {manualFreight:#,##0}đ (bảng giá: {computedFreight:#,##0}đ)");
                    estimatedFreightCharge = manualFreight;
                }

                baseServiceFee = quotationDetails?.ServiceFee ?? domesticShippingFee;
                if (quotationDetails?.ServiceFee is decimal manualService
                    && Math.Abs(manualService - domesticShippingFee) > OverrideTolerance)
                {
                    overrideLines.Add($"Phí dịch vụ {manualService:#,##0}đ (bảng giá: {domesticShippingFee:#,##0}đ)");
                }
            }

            var totalQuantity = orderItems.Sum(i => i.Quantity ?? 1);
            var totalDeclaredValue = orderItems.Sum(i => i.DeclaredValue ?? 0m);

            decimal totalSurcharges = 0m;
            var surchargeFees = new List<QuotationFee>();

            // Dịch vụ khách chọn cho từng kiện: hệ thống tự tính theo bảng giá. Sale chỉ sửa số tiền của
            // đúng dòng kiện + dịch vụ đó; khác bảng giá thì thành giá ngoại lệ chờ Admin duyệt.
            var itemServiceRules = await ItemServiceRules.LoadRulesAsync(_unitOfWork, orderItems.Select(i => i.Id));
            var itemServiceFees = CalculateItemServiceFees(orderItems, parcels, itemServiceRules, now);
            var itemServiceRuleIds = itemServiceFees.Select(f => f.PricingRuleId!.Value).ToHashSet();
            var itemNames = orderItems.ToDictionary(i => i.Id, i => i.ProductName ?? string.Empty);

            if (quotationDetails?.AdditionalFees != null)
            {
                foreach (var feeDto in quotationDetails.AdditionalFees)
                {
                    PricingRule? rule = null;
                    if (feeDto.FeeId.HasValue)
                    {
                        rule = await pricingRuleRepo.GetByIdAsync(feeDto.FeeId.Value);
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

                        // Phí mua hộ thuộc luồng PurchaseRequest, không phải phụ phí của đơn ký gửi.
                        if (string.Equals(rule.RuleType, "PURCHASE_FEE", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (feeDto.OrderItemId.HasValue)
                    {
                        var line = itemServiceFees.FirstOrDefault(f =>
                            f.OrderItemId == feeDto.OrderItemId && f.PricingRuleId.HasValue && f.PricingRuleId == feeDto.FeeId);
                        if (line == null)
                        {
                            throw new ArgumentException(
                                $"Kiện được chọn không đăng ký dịch vụ \"{rule?.RuleName ?? feeDto.Label}\". Chỉ sửa được phí dịch vụ khách đã chọn cho kiện.");
                        }

                        var manualAmount = feeDto.Enabled ? feeDto.Amount : 0m;
                        if (Math.Abs(manualAmount - line.Amount) > OverrideTolerance)
                        {
                            overrideLines.Add(
                                $"{line.FeeName} kiện {itemNames.GetValueOrDefault(line.OrderItemId!.Value)} {manualAmount:#,##0}đ (bảng giá: {line.Amount:#,##0}đ)");
                            line.OriginalAmount = line.Amount;
                            line.Amount = manualAmount;
                            line.IsOverride = true;
                        }

                        continue;
                    }

                    // Dòng dịch vụ theo kiện FE gửi lại nguyên từ báo giá trước: đã tính ở trên, không cộng lần hai.
                    if (rule != null && itemServiceRuleIds.Contains(rule.Id))
                    {
                        continue;
                    }

                    if (!feeDto.Enabled) continue;

                    var feeAmount = feeDto.Amount;
                    decimal? expected = rule != null
                        ? CalculateRuleAmount(rule, estimatedFreightCharge, netWeight, totalVolumeCm3, totalQuantity, totalDeclaredValue)
                        : null;

                    var isOverride = rule == null
                        ? feeAmount > 0m
                        : Math.Abs(feeAmount - expected!.Value) > OverrideTolerance;

                    if (isOverride)
                    {
                        var label = feeDto.Label ?? rule?.RuleName ?? "Phụ phí bổ sung";
                        overrideLines.Add(rule == null
                            ? $"{label} {feeAmount:#,##0}đ (không có trong bảng giá)"
                            : $"{label} {feeAmount:#,##0}đ (bảng giá: {expected:#,##0}đ)");
                    }

                    totalSurcharges += feeAmount;

                    surchargeFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        PricingRuleId = feeDto.FeeId,
                        FeeName = feeDto.Label ?? rule?.RuleName ?? "Phụ phí bổ sung",
                        FeeType = "SURCHARGE",
                        CalculationType = rule?.CalculationType ?? "FIXED",
                        Value = rule?.Value ?? (feeAmount > 0 ? feeAmount : 0m),
                        Amount = feeAmount,
                        OriginalAmount = isOverride ? expected : null,
                        IsOverride = isOverride,
                        Note = rule?.Description ?? "Phụ phí bổ sung",
                        CreatedAt = now
                    });
                }
            }

            totalSurcharges += itemServiceFees.Sum(f => f.Amount);
            surchargeFees.AddRange(itemServiceFees);

            var packageFees = await CalculatePackageFeesAsync(orderItems, now);
            quotationFees.AddRange(packageFees);
            decimal totalPackageFees = packageFees.Sum(f => f.Amount);

            var serviceFee = baseServiceFee + totalSurcharges + totalPackageFees;

            // Thuế luôn tính theo cấu hình. Sales nhập số khác thì dùng số của Sales nhưng đánh dấu là
            // giá ngoại lệ để Admin duyệt.
            decimal taxableServiceFee = isConsignment ? Math.Max(0m, serviceFee - domesticShippingFee) : serviceFee;
            var taxResult = await CalculateTaxesAsync(orderItems, estimatedFreightCharge, taxableServiceFee, now, activeRules);

            decimal vatAmount = taxResult.VatAmount;
            decimal importTax = taxResult.ImportTax;
            var manualVat = quotationDetails?.Vat;
            var manualImportTax = quotationDetails?.ImportTax;
            var taxOverridden =
                (manualVat is decimal mv && mv > 0m && Math.Abs(mv - taxResult.VatAmount) > OverrideTolerance)
                || (manualImportTax is decimal mi && mi > 0m && Math.Abs(mi - taxResult.ImportTax) > OverrideTolerance);

            if (taxOverridden)
            {
                vatAmount = manualVat is > 0m ? manualVat.Value : taxResult.VatAmount;
                importTax = manualImportTax is > 0m ? manualImportTax.Value : taxResult.ImportTax;
                overrideLines.Add(
                    $"Thuế VAT {vatAmount:#,##0}đ / nhập khẩu {importTax:#,##0}đ (theo cấu hình: {taxResult.VatAmount:#,##0}đ / {taxResult.ImportTax:#,##0}đ)");

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
                        OriginalAmount = taxResult.VatAmount,
                        IsOverride = Math.Abs(vatAmount - taxResult.VatAmount) > OverrideTolerance,
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
                        OriginalAmount = taxResult.ImportTax,
                        IsOverride = Math.Abs(importTax - taxResult.ImportTax) > OverrideTolerance,
                        Note = "Thuế nhập khẩu chốt",
                        CreatedAt = now
                    });
                }
            }
            else
            {
                quotationFees.AddRange(taxResult.TaxFees);
            }

            var taxAndDuty = importTax + vatAmount;

            if (baseServiceFee > 0)
            {
                var domesticRule = activeRules.FirstOrDefault(r =>
                    (r.ServicePricingId == matchedPricing.Id || r.ServicePricingId == null) &&
                    (string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.ConditionType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                     (r.RuleName ?? string.Empty).Contains("nội địa", StringComparison.OrdinalIgnoreCase))
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

            var hasOverride = overrideLines.Count > 0;
            var needsPriceApproval = hasOverride;
            var overrideReason = FirstNonBlank(request.OverrideReason, request.SalesNote, quotationDetails?.SalesNote);

            if (needsPriceApproval && string.IsNullOrWhiteSpace(overrideReason))
            {
                throw new ArgumentException(
                    "Báo giá có giá ngoài bảng giá: " + string.Join("; ", overrideLines)
                    + ". Ghi lý do (overrideReason hoặc ghi chú Sales) để gửi Admin duyệt.");
            }

            // Báo giá mới tới tay khách thì các bản cũ chưa được chấp nhận bị thay thế. Còn chờ duyệt
            // giá thì chỉ thay bản chờ duyệt cũ, khách vẫn xem được báo giá đang có.
            SupersedeOpenQuotations(orderQuotations, quotationRepo, keepCustomerVisible: needsPriceApproval);

            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                QuoteType = "OFFICIAL",
                Status = needsPriceApproval ? QuotationAcceptanceRules.StatusPendingPriceApproval : QuotationAcceptanceRules.StatusPending,
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                Vat = vatAmount,
                ImportTax = importTax,
                TotalVolume = totalVolumeCm3,
                CreatedAt = now,
                ExpiredAt = now.AddDays(QuotationValidDays),
                ServicePricingId = matchedPricing.Id,
                SalesNote = request.SalesNote ?? quotationDetails?.SalesNote,
                WarehouseId = warehouse.Id,
                CreatedById = actorId,
                PriceApprovalStatus = needsPriceApproval
                    ? QuotationAcceptanceRules.PriceApprovalPending
                    : QuotationAcceptanceRules.PriceApprovalNotRequired,
                OverrideReason = hasOverride
                    ? Truncate($"{overrideReason} — {string.Join("; ", overrideLines)}", 500)
                    : null
            };

            await quotationRepo.AddAsync(quotation);

            foreach (var qf in quotationFees)
            {
                qf.QuotationId = quotation.Id;
                await _unitOfWork.Repository<QuotationFee>().AddAsync(qf);
            }

            var statusBefore = order.Status;
            if (!needsPriceApproval)
            {
                order.Status = "QUOTATION_SENT";
            }
            order.TotalWeight = totalActualWeight > 0m ? totalActualWeight : order.TotalWeight;
            order.TotalVolume = totalVolumeCm3 > 0m ? totalVolumeCm3 : order.TotalVolume;
            orderRepo.Update(order);

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, statusBefore, order.Status,
                needsPriceApproval ? "QUOTATION_SUBMITTED_FOR_PRICE_APPROVAL" : "QUOTATION_SENT",
                $"Báo giá {quotation.Id}: tổng {estimatedFreightCharge + serviceFee + taxAndDuty:#,##0}đ, kho {warehouse.WarehouseName}."
                + (hasOverride ? $" Giá ngoại lệ: {string.Join("; ", overrideLines)}." : string.Empty),
                actorId);

            if (!needsPriceApproval)
            {
                await NotifyCustomerQuotationSentAsync(order, customer);
            }

            await _unitOfWork.SaveChangesAsync();

            if (needsPriceApproval)
            {
                await _notificationService.NotifyRolesAsync(
                    new[] { "Admin" },
                    "Báo giá chờ duyệt giá ngoại lệ",
                    $"Đơn {order.ConsignmentCode}: {string.Join("; ", overrideLines)}. Lý do: {overrideReason}",
                    excludeUserId: actorId);
            }

            return new QuotationActionResponse
            {
                Message = needsPriceApproval
                    ? "Báo giá có giá ngoài bảng giá, đã gửi Admin duyệt. Khách chưa nhìn thấy báo giá này."
                    : "Đã gửi báo giá cho khách hàng.",
                Status = needsPriceApproval ? QuotationAcceptanceRules.StatusPendingPriceApproval : "QUOTATION_SENT",
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
                    WarehouseId = warehouse.Id
                }
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> DecidePriceOverrideAsync(
            Guid quotationId, Guid actorId, string actorRole, QuotationPriceDecisionRequest request)
        {
            if (!WarehouseApprovalPolicy.IsAdmin(actorRole))
                throw new UnauthorizedAccessException("Chỉ Admin duyệt giá ngoại lệ của báo giá.");

            var decision = request?.Decision?.Trim().ToUpperInvariant();
            if (decision != QuotationAcceptanceRules.PriceApprovalApproved && decision != QuotationAcceptanceRules.PriceApprovalRejected)
                throw new ArgumentException("Quyết định phải là APPROVED hoặc REJECTED.");

            if (decision == QuotationAcceptanceRules.PriceApprovalRejected && string.IsNullOrWhiteSpace(request!.Note))
                throw new ArgumentException("Từ chối giá ngoại lệ thì bắt buộc ghi lý do.");

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId)
                ?? throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            if (!string.Equals(quotation.Status, QuotationAcceptanceRules.StatusPendingPriceApproval, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Báo giá không ở trạng thái chờ duyệt giá (hiện tại: {StatusNames.Quote(quotation.Status)}).");

            if (quotation.CreatedById.HasValue && quotation.CreatedById.Value == actorId)
                throw new UnauthorizedAccessException("Người lập báo giá không được tự duyệt giá ngoại lệ.");

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(quotation.OrderId)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng của báo giá.");

            var now = DateTime.UtcNow;
            quotation.PriceApprovedById = actorId;
            quotation.PriceApprovedAt = now;
            quotation.PriceApprovalNote = Truncate(request!.Note?.Trim(), 500);

            var statusBefore = order.Status;
            if (decision == QuotationAcceptanceRules.PriceApprovalApproved)
            {
                var otherQuotations = (await quotationRepo.GetAllAsync())
                    .Where(q => q.OrderId == quotation.OrderId && q.Id != quotation.Id)
                    .ToList();

                // Dữ liệu cũ: khách đã chấp nhận bản khác trong lúc bản này chờ duyệt — duyệt thì đơn bị kéo
                // lùi về QUOTATION_SENT. Admin từ chối bản này; phí phát sinh đi qua đợt thanh toán cuối.
                if (otherQuotations.Any(q => string.Equals(q.Status, QuotationAcceptanceRules.StatusAccepted, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Khách đã chấp nhận một báo giá khác của đơn này — không duyệt được giá ngoại lệ, hãy từ chối.");

                SupersedeOpenQuotations(otherQuotations, quotationRepo, keepCustomerVisible: false);

                quotation.PriceApprovalStatus = QuotationAcceptanceRules.PriceApprovalApproved;
                quotation.Status = QuotationAcceptanceRules.StatusPending;
                // Thời hạn báo giá tính từ lúc khách thực sự nhận được.
                quotation.ExpiredAt = now.AddDays(QuotationValidDays);
                order.Status = "QUOTATION_SENT";
                _unitOfWork.Repository<Order>().Update(order);

                var customer = order.CustomerId.HasValue
                    ? await _unitOfWork.Repository<Customer>().GetByIdAsync(order.CustomerId.Value)
                    : null;
                await NotifyCustomerQuotationSentAsync(order, customer);
            }
            else
            {
                quotation.PriceApprovalStatus = QuotationAcceptanceRules.PriceApprovalRejected;
                quotation.Status = QuotationAcceptanceRules.StatusPriceRejected;
            }

            quotationRepo.Update(quotation);

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, statusBefore, order.Status,
                decision == QuotationAcceptanceRules.PriceApprovalApproved ? "QUOTATION_PRICE_APPROVED" : "QUOTATION_PRICE_REJECTED",
                $"Báo giá {quotation.Id}." + (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" {request.Note.Trim()}"),
                actorId);

            await _unitOfWork.SaveChangesAsync();

            if (quotation.CreatedById.HasValue)
            {
                await _notificationService.NotifyUserAsync(quotation.CreatedById.Value,
                    decision == QuotationAcceptanceRules.PriceApprovalApproved ? "Giá ngoại lệ đã được duyệt" : "Giá ngoại lệ bị từ chối",
                    $"Báo giá đơn {order.ConsignmentCode}"
                    + (decision == QuotationAcceptanceRules.PriceApprovalApproved
                        ? " đã được gửi cho khách."
                        : $" bị từ chối: {request.Note}. Lập báo giá mới theo bảng giá hoặc lý do khác."));
            }

            return new QuotationActionResponse
            {
                Message = decision == QuotationAcceptanceRules.PriceApprovalApproved
                    ? "Đã duyệt giá ngoại lệ và gửi báo giá cho khách."
                    : "Đã từ chối giá ngoại lệ.",
                Status = quotation.Status ?? string.Empty,
                RejectionReason = decision == QuotationAcceptanceRules.PriceApprovalRejected ? request.Note : null,
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
                    WarehouseId = quotation.WarehouseId
                }
            };
        }

        private static void SupersedeOpenQuotations(
            IEnumerable<Quotation> quotations, VCL_DLL.Interface.IGenericRepository<Quotation> repo, bool keepCustomerVisible)
        {
            foreach (var old in quotations.Where(q =>
                         string.Equals(q.Status, QuotationAcceptanceRules.StatusPendingPriceApproval, StringComparison.OrdinalIgnoreCase)
                         || (!keepCustomerVisible
                             && (string.Equals(q.Status, QuotationAcceptanceRules.StatusDraft, StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(q.Status, QuotationAcceptanceRules.StatusPending, StringComparison.OrdinalIgnoreCase)))))
            {
                old.Status = QuotationAcceptanceRules.StatusSuperseded;
                repo.Update(old);
            }
        }

        /// <summary>
        /// Kho nhận hàng của báo giá — nơi phiếu tiếp nhận sẽ được lập sau khi khách đặt cọc.
        /// </summary>
        private async Task<Warehouse> ResolveQuotationWarehouseAsync(Guid? warehouseId)
        {
            if (!warehouseId.HasValue || warehouseId.Value == Guid.Empty)
                throw new ArgumentException("Vui lòng chọn kho nhận hàng (warehouseId) cho báo giá.");

            var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId.Value)
                ?? throw new NotFoundException($"Không tìm thấy kho với ID: {warehouseId}");

            if (!warehouse.IsActive)
                throw new ArgumentException($"Kho {warehouse.WarehouseName} đang tạm ngưng hoạt động.");

            if (!string.Equals(warehouse.WarehouseType?.Trim(), "ORIGIN", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Kho {warehouse.WarehouseName} không phải kho nhận hàng quốc tế (ORIGIN).");

            return warehouse;
        }

        /// <summary>
        /// Dòng giá cước của báo giá. Không có dòng giá khớp phương án và tuyến thì báo lỗi —
        /// trước đây rơi về giá cứng 50.000/30.000đ/kg hoặc giá của tuyến khác.
        /// </summary>
        private async Task<ServicePricing> ResolveServicePricingAsync(CreateQuotationRequest request, string serviceType, string? route)
        {
            var allPricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                .Where(p => !p.IsDeleted)
                .ToList();

            var normalizedRoute = string.IsNullOrWhiteSpace(route) ? null : OrderService.NormalizeRoute(route);

            bool MatchesRoute(ServicePricing p) =>
                normalizedRoute == null
                || string.Equals(OrderService.NormalizeRoute($"{p.OriginCountry}-{p.DestinationCountry}"), normalizedRoute, StringComparison.OrdinalIgnoreCase)
                || string.Equals(OrderService.NormalizeRoute(p.OriginCountry), normalizedRoute, StringComparison.OrdinalIgnoreCase);

            var targetPricingId = request.ServicePricingId ?? request.Quotation?.ServicePricingId;
            ServicePricing? matched;

            if (targetPricingId.HasValue && targetPricingId.Value != Guid.Empty)
            {
                matched = allPricings.FirstOrDefault(p => p.Id == targetPricingId.Value)
                    ?? throw new ArgumentException("Dòng giá được chọn không tồn tại hoặc đã ngừng áp dụng.");

                if (!MatchesRoute(matched))
                    throw new ArgumentException($"Dòng giá được chọn không áp dụng cho tuyến {route}.");
            }
            else
            {
                matched = allPricings
                    .Where(p => string.Equals(p.ServiceType, serviceType, StringComparison.OrdinalIgnoreCase))
                    .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                    .OrderByDescending(p => p.EffectiveDate)
                    .FirstOrDefault(MatchesRoute);
            }

            if (matched?.Price is not > 0m)
            {
                throw new ArgumentException(
                    $"Chưa có bảng giá hợp lệ cho phương án {StatusNames.Quote(serviceType)} tuyến '{OrderService.MapRouteToDisplayName(route)}'. Admin cần cấu hình giá trước khi báo giá.");
            }

            if (matched.EffectiveDate.HasValue && matched.EffectiveDate.Value > DateTime.UtcNow)
                throw new ArgumentException("Dòng giá được chọn chưa đến ngày hiệu lực.");

            return matched;
        }

        /// <summary>Số tiền một phụ phí theo đúng công thức của bảng giá, cùng cách báo giá tạm tính đang dùng.</summary>
        private static decimal CalculateRuleAmount(
            PricingRule rule, decimal freightCharge, decimal chargeableWeight, decimal totalVolumeCm3,
            int totalQuantity, decimal totalDeclaredValue)
        {
            decimal amount = rule.CalculationType?.Trim().ToUpperInvariant() switch
            {
                "FIXED" => rule.Value,
                "PERCENTAGE" => (totalDeclaredValue > 0 ? totalDeclaredValue : freightCharge) * (rule.Value / 100m),
                "PER_KG" => chargeableWeight * rule.Value,
                "PER_CBM" => totalVolumeCm3 / 1000000m * rule.Value,
                "PER_PRODUCT" => totalQuantity * rule.Value,
                _ => rule.Value
            };

            if (rule.MinAmount.HasValue && amount < rule.MinAmount.Value) amount = rule.MinAmount.Value;
            if (rule.MaxAmount.HasValue && amount > rule.MaxAmount.Value) amount = rule.MaxAmount.Value;

            return Math.Round(amount, 0);
        }

        private async Task NotifyCustomerQuotationSentAsync(Order order, Customer? customer)
        {
            if (customer == null || customer.UserId == null || customer.UserId == Guid.Empty) return;

            await _unitOfWork.Repository<Notification>().AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = customer.UserId,
                Title = "Đơn hàng đã có báo giá mới",
                Content = $"Đơn hàng ký gửi {order.ConsignmentCode ?? order.Id.ToString()} đã được tạo báo giá. Vui lòng kiểm tra và xác nhận thanh toán.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        private static string? FirstNonBlank(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private static string? Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
    }
}
