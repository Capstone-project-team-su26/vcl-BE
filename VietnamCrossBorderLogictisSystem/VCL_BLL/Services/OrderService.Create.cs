using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class OrderService
    {
        public async Task<CreateConsignmentResponse> CreateConsignmentAsync(CreateConsignmentRequest request, Guid userId)
        {
            // 1. Validate tuyến hàng + phương án vận chuyển theo bảng cấu hình giá (PricingRule)
            if (string.IsNullOrWhiteSpace(request.Route))
                throw new ArgumentException("Vui lòng chọn tuyến hàng (route).");
            if (string.IsNullOrWhiteSpace(request.ShippingOption))
                throw new ArgumentException("Vui lòng chọn phương án vận chuyển (shippingOption).");

            var canonicalRoute = NormalizeRoute(request.Route);

            var servicePricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync()).Where(p => !p.IsDeleted).ToList();
            var matchedPricing = servicePricings.FirstOrDefault(p =>
                p.ServiceType != null && p.ServiceType.Equals(request.ShippingOption, StringComparison.OrdinalIgnoreCase)
                && (
                    string.Equals($"{p.OriginCountry}-{p.DestinationCountry}", canonicalRoute, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.OriginCountry, canonicalRoute, StringComparison.OrdinalIgnoreCase)
                ));

            if (matchedPricing == null)
            {
                var serviceTypeExists = servicePricings.Any(p => p.ServiceType != null && p.ServiceType.Equals(request.ShippingOption, StringComparison.OrdinalIgnoreCase));
                if (!serviceTypeExists)
                {
                    throw new ArgumentException($"Không có cấu hình giá hợp lệ cho phương án '{request.ShippingOption}'. Vui lòng chọn lại.");
                }
            }

            // Dùng đúng giá trị chuẩn hóa từ cấu hình
            var route = canonicalRoute;
            var shippingOption = matchedPricing != null ? matchedPricing.ServiceType : request.ShippingOption;

            if (request.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Yêu cầu ký gửi phải có ít nhất một mặt hàng.");

            // 2. Map user → customer (cùng pattern với các service khác)
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new UnauthorizedAccessException("Không tìm thấy thông tin khách hàng tương ứng với tài khoản.");

            // 3. Đối chiếu hàng cấm / hạn chế (dùng chung logic với endpoint validate-items)
            var validationResults = await ValidateItemsAgainstRestrictedAsync(request.Items);
            var bannedItems = validationResults.Where(r => r.Level == "BANNED").ToList();
            if (bannedItems.Count > 0)
            {
                var names = string.Join(", ", bannedItems.Select(b => b.ProductName).Distinct());
                throw new ArgumentException($"Không thể tạo ký gửi. Các mặt hàng sau thuộc danh mục cấm: {names}");
            }

            // Các mặt hàng hạn chế/cảnh báo → đưa vào Warnings để trả về cho khách
            var warnings = validationResults
                .Where(r => r.Level != "BANNED")
                .Select(r => new RestrictedItemWarning
                {
                    ProductName = r.ProductName,
                    MatchedItem = r.MatchedItem,
                    RestrictionType = r.Level,
                    Note = r.Reason
                })
                .ToList();

            // 4. Resolve các quy định giá/dịch vụ phụ được chọn
            var selectedOrderRules = new List<PricingRule>();
            if (request.PricingRuleIds != null && request.PricingRuleIds.Any())
            {
                foreach (var ruleId in request.PricingRuleIds)
                {
                    var rule = await _unitOfWork.Repository<PricingRule>().GetByIdAsync(ruleId);
                    if (rule != null && !string.Equals(rule.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) && !rule.IsRequired)
                    {
                        selectedOrderRules.Add(rule);
                    }
                }
            }

            // FE hiện gửi các cờ boolean thay vì PricingRuleIds -> map cờ sang PricingRule active
            // theo RuleType tương ứng và union vào danh sách đã chọn (không trùng lặp).
            await AddRulesFromServiceFlagsAsync(selectedOrderRules,
                request.RequiresInspection, request.RequiresPacking,
                request.RequiresWoodenCrate, request.RequiresInsurance);

            var selectedItemRulesMap = new Dictionary<ConsignmentItemRequest, List<PricingRule>>();
            foreach (var item in request.Items)
            {
                selectedItemRulesMap[item] = new List<PricingRule>();
            }

            // Kiểm tra dịch vụ kiểm hàng (RequiresInspection): theo cờ FE gửi hoặc theo rule đã chọn
            bool requiresInspection = request.RequiresInspection || selectedOrderRules.Any(r =>
                string.Equals(r.RuleType, "INSPECTION", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase)
            );

            // Gom tất cả mã đóng gói (RuleCode)
            var boxRuleCodes = new List<string>();
            foreach (var rule in selectedOrderRules)
            {
                if (IsBoxRule(rule))
                {
                    if (!string.IsNullOrWhiteSpace(rule.RuleCode) && !boxRuleCodes.Contains(rule.RuleCode))
                    {
                        boxRuleCodes.Add(rule.RuleCode);
                    }
                }
            }

            // Gom tất cả mã dịch vụ phụ khác (non-box, non-inspection)
            var otherRuleCodes = selectedOrderRules
                .Where(r => !IsBoxRule(r) && 
                            !string.Equals(r.RuleType, "INSPECTION", StringComparison.OrdinalIgnoreCase) && 
                            !string.Equals(r.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.RuleCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();

            var noteParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Note))
            {
                noteParts.Add(request.Note);
            }
            if (boxRuleCodes.Any())
            {
                noteParts.Add("Yêu cầu đóng gói: " + string.Join(", ", boxRuleCodes));
            }
            if (requiresInspection)
            {
                noteParts.Add("Yêu cầu kiểm hàng");
            }
            if (otherRuleCodes.Any())
            {
                noteParts.Add("Dịch vụ khác: " + string.Join(", ", otherRuleCodes));
            }
            var note = noteParts.Any() ? string.Join(". ", noteParts) : null;

            // Tạo Order
            var orderRepo = _unitOfWork.Repository<Order>();
            var now = DateTime.UtcNow;
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                CompanyId = customer.CompanyId,
                ConsignmentCode = await GenerateUniqueConsignmentCodeAsync(),
                ConsignmentType = shippingOption,
                Route = route,
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                ReceiverAddress = request.ReceiverAddress,
                RequiresInspection = requiresInspection,
                Status = "PENDING_REVIEW",
                OrderType = "CONSIGNMENT",
                Note = note,
                CreatedAt = now
            };
            await orderRepo.AddAsync(order);

            // 5. Tạo OrderItems
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            foreach (var item in request.Items)
            {
                decimal? length = item.Length;
                decimal? width = item.Width;
                decimal? height = item.Height;

                Guid? packageConfigId = item.PackageConfigurationId;
                if (!packageConfigId.HasValue || packageConfigId.Value == Guid.Empty)
                {
                    packageConfigId = await SuggestPackageConfigurationIdAsync(length, width, height, item.Weight);
                }

                await orderItemRepo.AddAsync(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    CompanyId = customer.CompanyId,
                    ProductName = item.ProductName,
                    ProductType = item.ProductType,
                    Quantity = item.Quantity,
                    Weight = item.Weight,
                    Width = width,
                    Height = height,
                    Length = length,
                    DeclaredValue = item.DeclaredValue,
                    ReferenceUrl = item.CombinedReferenceUrls(),
                    DomesticTrackingCode = item.DomesticTrackingCode,
                    PackageConfigurationId = packageConfigId,
                    CreatedAt = now
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 6. Sinh báo giá tạm tính (tái sử dụng QuotationService)
            // Truyền danh sách phụ phí khách tự tick để báo giá chỉ áp các phí đã chọn
            var quotation = await _quotationService.CreateEstimateQuotationAsync(
                order.Id, selectedOrderRules.Select(r => r.Id).ToList());

            // 7. Trả về kết quả gộp
            return new CreateConsignmentResponse
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                CustomerId = customer.Id,
                UserId = customer.UserId,
                Status = order.Status,
                ConsignmentType = order.ConsignmentType,
                Route = MapRouteToDisplayName(order.Route),
                RequiresInspection = order.RequiresInspection,
                ItemCount = request.Items.Sum(i => i.Quantity),
                PricingRuleIds = selectedOrderRules.Select(r => r.Id).ToList(),
                Quotation = quotation,
                Warnings = warnings
            };
        }

        public async Task<CreateConsignmentResponse> CreateConsignmentByStaffAsync(CreateConsignmentByStaffRequest request, Guid staffUserId)
        {
            // 1. Validate tuyến hàng + phương án vận chuyển theo bảng cấu hình giá (PricingRule)
            if (string.IsNullOrWhiteSpace(request.Route))
                throw new ArgumentException("Vui lòng chọn tuyến hàng (route).");
            if (string.IsNullOrWhiteSpace(request.ShippingOption))
                throw new ArgumentException("Vui lòng chọn phương án vận chuyển (shippingOption).");

            var canonicalRoute = NormalizeRoute(request.Route);

            var servicePricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync()).Where(p => !p.IsDeleted).ToList();
            var matchedPricing = servicePricings.FirstOrDefault(p =>
                p.ServiceType != null && p.ServiceType.Equals(request.ShippingOption, StringComparison.OrdinalIgnoreCase)
                && (
                    string.Equals($"{p.OriginCountry}-{p.DestinationCountry}", canonicalRoute, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.OriginCountry, canonicalRoute, StringComparison.OrdinalIgnoreCase)
                ));

            if (matchedPricing == null)
            {
                var serviceTypeExists = servicePricings.Any(p => p.ServiceType != null && p.ServiceType.Equals(request.ShippingOption, StringComparison.OrdinalIgnoreCase));
                if (!serviceTypeExists)
                {
                    throw new ArgumentException($"Không có cấu hình giá hợp lệ cho phương án '{request.ShippingOption}'. Vui lòng chọn lại.");
                }
            }

            var route = canonicalRoute;
            var shippingOption = matchedPricing != null ? matchedPricing.ServiceType : request.ShippingOption;

            if (request.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Yêu cầu ký gửi phải có ít nhất một mặt hàng.");

            // 2. Resolve Customer được chọn hoặc tìm theo thông tin nhập vào
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            Customer? customer = null;

            if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
            {
                customer = allCustomers.FirstOrDefault(c => c.Id == request.CustomerId.Value);
                if (customer == null)
                    throw new NotFoundException($"Không tìm thấy khách hàng với ID {request.CustomerId.Value}");
            }
            else if (!string.IsNullOrWhiteSpace(request.CustomerPhone))
            {
                customer = allCustomers.FirstOrDefault(c => string.Equals(c.Phone, request.CustomerPhone.Trim(), StringComparison.OrdinalIgnoreCase));
                if (customer == null)
                    throw new NotFoundException($"Không tìm thấy khách hàng với số điện thoại {request.CustomerPhone}");
            }
            else if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
            {
                customer = allCustomers.FirstOrDefault(c => string.Equals(c.Email, request.CustomerEmail.Trim(), StringComparison.OrdinalIgnoreCase));
                if (customer == null)
                    throw new NotFoundException($"Không tìm thấy khách hàng với email {request.CustomerEmail}");
            }
            else
            {
                throw new ArgumentException("Vui lòng chọn khách hàng có sẵn hoặc nhập email/số điện thoại để tìm kiếm.");
            }

            // 3. Đối chiếu hàng cấm / hạn chế
            var validationResults = await ValidateItemsAgainstRestrictedAsync(request.Items);
            var bannedItems = validationResults.Where(r => r.Level == "BANNED").ToList();
            if (bannedItems.Count > 0)
            {
                var names = string.Join(", ", bannedItems.Select(b => b.ProductName).Distinct());
                throw new ArgumentException($"Không thể tạo ký gửi. Các mặt hàng sau thuộc danh mục cấm: {names}");
            }

            var warnings = validationResults
                .Where(r => r.Level != "BANNED")
                .Select(r => new RestrictedItemWarning
                {
                    ProductName = r.ProductName,
                    MatchedItem = r.MatchedItem,
                    RestrictionType = r.Level,
                    Note = r.Reason
                })
                .ToList();

            // 4. Lấy cấu hình trạng thái ban đầu của nhân viên tạo, mặc định APPROVED
            var initialStatus = _configuration["ConsignmentSettings:InitialStatusByStaff"] ?? "APPROVED";
            initialStatus = initialStatus.Trim().ToUpperInvariant();
            if (initialStatus != "PENDING_REVIEW" && initialStatus != "APPROVED")
            {
                initialStatus = "APPROVED";
            }

            // BE luôn tự sinh ConsignmentCode ngay khi tạo (dù trạng thái ban đầu là gì)
            var consignmentCode = await GenerateUniqueConsignmentCodeAsync();

            // 5. Resolve các quy định giá/dịch vụ phụ được chọn
            var selectedOrderRules = new List<PricingRule>();
            if (request.PricingRuleIds != null && request.PricingRuleIds.Any())
            {
                foreach (var ruleId in request.PricingRuleIds)
                {
                    var rule = await _unitOfWork.Repository<PricingRule>().GetByIdAsync(ruleId);
                    if (rule != null && !string.Equals(rule.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) && !rule.IsRequired)
                    {
                        selectedOrderRules.Add(rule);
                    }
                }
            }

            var selectedItemRulesMap = new Dictionary<ConsignmentItemRequest, List<PricingRule>>();
            foreach (var item in request.Items)
            {
                selectedItemRulesMap[item] = new List<PricingRule>();
            }

            // Kiểm tra dịch vụ kiểm hàng (RequiresInspection)
            bool requiresInspection = selectedOrderRules.Any(r =>
                string.Equals(r.RuleType, "INSPECTION", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase)
            );

            // Gom tất cả mã đóng gói (RuleCode)
            var boxRuleCodes = new List<string>();
            foreach (var rule in selectedOrderRules)
            {
                if (IsBoxRule(rule))
                {
                    if (!string.IsNullOrWhiteSpace(rule.RuleCode) && !boxRuleCodes.Contains(rule.RuleCode))
                    {
                        boxRuleCodes.Add(rule.RuleCode);
                    }
                }
            }

            // Gom tất cả mã dịch vụ phụ khác (non-box, non-inspection)
            var otherRuleCodes = selectedOrderRules
                .Where(r => !IsBoxRule(r) && 
                            !string.Equals(r.RuleType, "INSPECTION", StringComparison.OrdinalIgnoreCase) && 
                            !string.Equals(r.ConditionType, "REQUIRES_INSPECTION", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.RuleCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();

            var noteParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Note))
            {
                noteParts.Add(request.Note);
            }
            if (boxRuleCodes.Any())
            {
                noteParts.Add("Yêu cầu đóng gói: " + string.Join(", ", boxRuleCodes));
            }
            if (requiresInspection)
            {
                noteParts.Add("Yêu cầu kiểm hàng");
            }
            if (otherRuleCodes.Any())
            {
                noteParts.Add("Dịch vụ khác: " + string.Join(", ", otherRuleCodes));
            }
            var note = noteParts.Any() ? string.Join(". ", noteParts) : null;

            // Tạo Order
            var orderRepo = _unitOfWork.Repository<Order>();
            var nowTime = DateTime.UtcNow;
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                CompanyId = customer.CompanyId,
                ConsignmentCode = consignmentCode,
                ConsignmentType = shippingOption,
                Route = route,
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                ReceiverAddress = request.ReceiverAddress,
                RequiresInspection = requiresInspection,
                Status = initialStatus,
                OrderType = "CONSIGNMENT",
                Note = note,
                CreatedById = staffUserId,
                CreatedAt = nowTime
            };
            await orderRepo.AddAsync(order);

            // 6. Tạo OrderItems
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            foreach (var item in request.Items)
            {
                decimal? length = item.Length;
                decimal? width = item.Width;
                decimal? height = item.Height;

                Guid? packageConfigId = item.PackageConfigurationId;
                if (!packageConfigId.HasValue || packageConfigId.Value == Guid.Empty)
                {
                    packageConfigId = await SuggestPackageConfigurationIdAsync(length, width, height, item.Weight);
                }

                await orderItemRepo.AddAsync(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    CompanyId = customer.CompanyId,
                    ProductName = item.ProductName,
                    ProductType = item.ProductType,
                    Quantity = item.Quantity,
                    Weight = item.Weight,
                    Width = width,
                    Height = height,
                    Length = length,
                    DeclaredValue = item.DeclaredValue,
                    ReferenceUrl = item.CombinedReferenceUrls(),
                    DomesticTrackingCode = item.DomesticTrackingCode,
                    PackageConfigurationId = packageConfigId,
                    CreatedAt = nowTime
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 7. Sinh báo giá tạm tính
            // Truyền danh sách phụ phí khách/sale đã tick để báo giá chỉ áp các phí đã chọn
            var quotation = await _quotationService.CreateEstimateQuotationAsync(
                order.Id, selectedOrderRules.Select(r => r.Id).ToList());

            // 8. Trả kết quả
            return new CreateConsignmentResponse
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                CustomerId = customer.Id,
                UserId = customer.UserId,
                Status = order.Status,
                ConsignmentType = order.ConsignmentType,
                Route = MapRouteToDisplayName(order.Route),
                RequiresInspection = order.RequiresInspection,
                ItemCount = request.Items.Sum(i => i.Quantity),
                PricingRuleIds = selectedOrderRules.Select(r => r.Id).ToList(),
                Quotation = quotation,
                Warnings = warnings
            };
        }
    }
}
