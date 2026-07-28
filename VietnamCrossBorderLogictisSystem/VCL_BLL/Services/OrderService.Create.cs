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
    public partial class OrderService
    {
        public async Task<CreateConsignmentResponse> CreateConsignmentAsync(CreateConsignmentRequest request, Guid userId)
        {
            var (route, shippingOption) = await ResolveRouteAndShippingOptionAsync(request.Route, request.ShippingOption);
            EnsureHasItems(request.Items);

            // Map user → customer (cùng pattern với các service khác)
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new UnauthorizedAccessException("Không tìm thấy thông tin khách hàng tương ứng với tài khoản.");

            var draft = await BuildConsignmentDraftAsync(
                request.Items, request.PricingRuleIds, request.Note,
                request.RequiresInspection, request.RequiresPacking,
                request.RequiresWoodenCrate, request.RequiresInsurance);

            return await PersistConsignmentAsync(
                customer, route, shippingOption, "PENDING_REVIEW", createdById: null,
                request.ReceiverName, request.ReceiverPhone, request.ReceiverAddress,
                request.Items, draft);
        }

        public async Task<CreateConsignmentResponse> CreateConsignmentByStaffAsync(CreateConsignmentByStaffRequest request, Guid staffUserId)
        {
            var (route, shippingOption) = await ResolveRouteAndShippingOptionAsync(request.Route, request.ShippingOption);
            EnsureHasItems(request.Items);

            var customer = await ResolveCustomerForStaffAsync(request);

            // Trạng thái ban đầu của đơn do nhân viên tạo, mặc định APPROVED
            var initialStatus = _configuration["ConsignmentSettings:InitialStatusByStaff"] ?? "APPROVED";
            initialStatus = initialStatus.Trim().ToUpperInvariant();
            if (initialStatus != "PENDING_REVIEW" && initialStatus != "APPROVED")
            {
                initialStatus = "APPROVED";
            }

            var draft = await BuildConsignmentDraftAsync(
                request.Items, request.PricingRuleIds, request.Note,
                request.RequiresInspection, request.RequiresPacking,
                request.RequiresWoodenCrate, request.RequiresInsurance);

            return await PersistConsignmentAsync(
                customer, route, shippingOption, initialStatus, staffUserId,
                request.ReceiverName, request.ReceiverPhone, request.ReceiverAddress,
                request.Items, draft);
        }

        /// <summary>
        /// Thông tin đã tổng hợp từ request, dùng chung cho cả 2 luồng tạo ký gửi
        /// (khách tự tạo và Sale tạo hộ).
        /// </summary>
        private sealed class ConsignmentDraft
        {
            public List<PricingRule> SelectedRules { get; set; } = new();
            public bool RequiresInspection { get; set; }
            public string? Note { get; set; }
            public List<RestrictedItemWarning> Warnings { get; set; } = new();
        }

        /// <summary>
        /// Validate tuyến hàng + phương án vận chuyển theo bảng cấu hình giá,
        /// trả về giá trị đã chuẩn hoá theo cấu hình.
        /// </summary>
        private async Task<(string Route, string ShippingOption)> ResolveRouteAndShippingOptionAsync(string? requestedRoute, string? requestedShippingOption)
        {
            if (string.IsNullOrWhiteSpace(requestedRoute))
                throw new ArgumentException("Vui lòng chọn tuyến hàng (route).");
            if (string.IsNullOrWhiteSpace(requestedShippingOption))
                throw new ArgumentException("Vui lòng chọn phương án vận chuyển (shippingOption).");

            var canonicalRoute = NormalizeRoute(requestedRoute);

            var servicePricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync()).Where(p => !p.IsDeleted).ToList();
            var matchedPricing = servicePricings.FirstOrDefault(p =>
                p.ServiceType != null && p.ServiceType.Equals(requestedShippingOption, StringComparison.OrdinalIgnoreCase)
                && (
                    string.Equals($"{p.OriginCountry}-{p.DestinationCountry}", canonicalRoute, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.OriginCountry, canonicalRoute, StringComparison.OrdinalIgnoreCase)
                ));

            if (matchedPricing == null)
            {
                var serviceTypeExists = servicePricings.Any(p => p.ServiceType != null && p.ServiceType.Equals(requestedShippingOption, StringComparison.OrdinalIgnoreCase));
                if (!serviceTypeExists)
                {
                    throw new ArgumentException($"Không có cấu hình giá hợp lệ cho phương án '{requestedShippingOption}'. Vui lòng chọn lại.");
                }
            }

            // Dùng đúng giá trị chuẩn hóa từ cấu hình
            return (canonicalRoute, matchedPricing != null ? matchedPricing.ServiceType : requestedShippingOption);
        }

        private static void EnsureHasItems(List<ConsignmentItemRequest>? items)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("Yêu cầu ký gửi phải có ít nhất một mặt hàng.");
        }

        /// <summary>
        /// Tìm khách hàng mà Sale tạo đơn hộ: theo ID được chọn, hoặc tra theo số điện thoại/email.
        /// </summary>
        private async Task<Customer> ResolveCustomerForStaffAsync(CreateConsignmentByStaffRequest request)
        {
            var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            return CustomerLookupHelper.ResolveForStaff(
                allCustomers, request.CustomerId, request.CustomerPhone, request.CustomerEmail);
        }

        /// <summary>
        /// Đối chiếu hàng cấm/hạn chế, resolve các dịch vụ phụ được chọn (theo ID và theo cờ boolean)
        /// và dựng ghi chú xử lý cho đơn.
        /// </summary>
        private async Task<ConsignmentDraft> BuildConsignmentDraftAsync(
            List<ConsignmentItemRequest> items,
            List<Guid>? pricingRuleIds,
            string? requestNote,
            bool requiresInspection, bool requiresPacking, bool requiresWoodenCrate, bool requiresInsurance)
        {
            // Đối chiếu hàng cấm / hạn chế (dùng chung logic với endpoint validate-items)
            var validationResults = await ValidateItemsAgainstRestrictedAsync(items);
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

            // Resolve các quy định giá/dịch vụ phụ được chọn theo ID
            var selectedOrderRules = new List<PricingRule>();
            if (pricingRuleIds != null && pricingRuleIds.Any())
            {
                foreach (var ruleId in pricingRuleIds)
                {
                    var rule = await _unitOfWork.Repository<PricingRule>().GetByIdAsync(ruleId);
                    if (rule != null && !string.Equals(rule.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) && !rule.IsRequired)
                    {
                        selectedOrderRules.Add(rule);
                    }
                }
            }

            // FE có thể gửi các cờ boolean thay vì PricingRuleIds -> map cờ sang PricingRule active
            // theo RuleType tương ứng và union vào danh sách đã chọn (không trùng lặp).
            await AddRulesFromServiceFlagsAsync(selectedOrderRules,
                requiresInspection, requiresPacking, requiresWoodenCrate, requiresInsurance);

            // Dịch vụ kiểm hàng: theo cờ FE gửi hoặc theo rule đã chọn
            bool inspection = requiresInspection || selectedOrderRules.Any(r =>
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
            if (!string.IsNullOrWhiteSpace(requestNote))
            {
                noteParts.Add(requestNote);
            }
            if (boxRuleCodes.Any())
            {
                noteParts.Add("Yêu cầu đóng gói: " + string.Join(", ", boxRuleCodes));
            }
            if (inspection)
            {
                noteParts.Add("Yêu cầu kiểm hàng");
            }
            if (otherRuleCodes.Any())
            {
                noteParts.Add("Dịch vụ khác: " + string.Join(", ", otherRuleCodes));
            }

            return new ConsignmentDraft
            {
                SelectedRules = selectedOrderRules,
                RequiresInspection = inspection,
                Note = noteParts.Any() ? string.Join(". ", noteParts) : null,
                Warnings = warnings
            };
        }

        /// <summary>
        /// Tạo Order + OrderItems, sinh báo giá tạm tính và dựng response trả về.
        /// </summary>
        private async Task<CreateConsignmentResponse> PersistConsignmentAsync(
            Customer customer,
            string route,
            string shippingOption,
            string status,
            Guid? createdById,
            string? receiverName,
            string? receiverPhone,
            string? receiverAddress,
            List<ConsignmentItemRequest> items,
            ConsignmentDraft draft)
        {
            var now = DateTime.UtcNow;

            // BE luôn tự sinh ConsignmentCode ngay khi tạo (dù trạng thái ban đầu là gì)
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                CompanyId = customer.CompanyId,
                ConsignmentCode = await GenerateUniqueConsignmentCodeAsync(),
                ConsignmentType = shippingOption,
                Route = route,
                ReceiverName = receiverName,
                ReceiverPhone = receiverPhone,
                ReceiverAddress = receiverAddress,
                RequiresInspection = draft.RequiresInspection,
                Status = status,
                OrderType = "CONSIGNMENT",
                Note = draft.Note,
                CreatedById = createdById,
                CreatedAt = now
            };
            await _unitOfWork.Repository<Order>().AddAsync(order);

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            foreach (var item in items)
            {
                Guid? packageConfigId = item.PackageConfigurationId;
                if (!packageConfigId.HasValue || packageConfigId.Value == Guid.Empty)
                {
                    packageConfigId = await SuggestPackageConfigurationIdAsync(item.Length, item.Width, item.Height, item.Weight);
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
                    Width = item.Width,
                    Height = item.Height,
                    Length = item.Length,
                    DeclaredValue = item.DeclaredValue,
                    ReferenceUrl = item.CombinedReferenceUrls(),
                    DomesticTrackingCode = item.DomesticTrackingCode,
                    PackageConfigurationId = packageConfigId,
                    CreatedAt = now
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // Sinh báo giá tạm tính (tái sử dụng QuotationService).
            // Truyền danh sách phụ phí đã tick để báo giá chỉ áp các phí được chọn.
            var ruleIds = draft.SelectedRules.Select(r => r.Id).ToList();
            var quotation = await _quotationService.CreateEstimateQuotationAsync(order.Id, ruleIds);

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
                ItemCount = items.Sum(i => i.Quantity),
                PricingRuleIds = ruleIds,
                Quotation = quotation,
                Warnings = draft.Warnings
            };
        }
    }
}
