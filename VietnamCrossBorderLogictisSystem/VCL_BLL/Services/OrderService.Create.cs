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
            var (route, shippingOption, shippingPricingId) = await ResolveRouteAndShippingOptionAsync(request.Route, request.ShippingOption);
            EnsureHasItems(request.Items);
            await EnsureItemsWithinLimitsAsync(request.Items!);
            EnsureWoodenCrateSelected(request.Items!);

            // Map user → customer (cùng pattern với các service khác)
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new UnauthorizedAccessException("Không tìm thấy thông tin khách hàng tương ứng với tài khoản.");

            var draft = await BuildConsignmentDraftAsync(request.Items, request.Note, shippingPricingId);
            draft.DefaultDestinationHandling = NormalizeDestinationHandling(request.DefaultDestinationHandling);

            return await PersistConsignmentAsync(
                customer, route, shippingOption, "PENDING_REVIEW", createdById: null,
                request.ReceiverName, request.ReceiverPhone, request.ReceiverAddress,
                request.Items, draft);
        }

        public async Task<CreateConsignmentResponse> CreateConsignmentByStaffAsync(CreateConsignmentByStaffRequest request, Guid staffUserId)
        {
            var (route, shippingOption, shippingPricingId) = await ResolveRouteAndShippingOptionAsync(request.Route, request.ShippingOption);
            EnsureHasItems(request.Items);
            await EnsureItemsWithinLimitsAsync(request.Items!);

            var customer = await ResolveCustomerForStaffAsync(request);

            // Sale tự tạo đơn cho khách là đã tiếp nhận; bước kế tiếp là Sale báo giá.
            const string initialStatus = "APPROVED";

            var draft = await BuildConsignmentDraftAsync(request.Items, request.Note, shippingPricingId);
            draft.DefaultDestinationHandling = NormalizeDestinationHandling(request.DefaultDestinationHandling);

            return await PersistConsignmentAsync(
                customer, route, shippingOption, initialStatus, staffUserId,
                request.ReceiverName, request.ReceiverPhone, request.ReceiverAddress,
                request.Items, draft);
        }

        /// <summary>
        /// ƯỚC TÍNH ĐƠN KÝ GỬI TRƯỚC KHI TẠO.
        ///
        /// Đi qua ĐÚNG các bước của lúc tạo đơn thật — chuẩn hoá tuyến + phương án, kiểm giới
        /// hạn kiện, kiểm hàng cấm, dựng draft dịch vụ theo kiện, gợi ý cỡ thùng khi khách
        /// chưa chọn — rồi gọi chung một hàm tính tiền. Khác duy nhất: không ghi gì xuống DB.
        ///
        /// Nhờ vậy số trên màn xác nhận và số trên hoá đơn phát hành ngay sau đó là CÙNG MỘT
        /// phép tính, không phải hai đoạn mã chạy song song rồi lệch nhau.
        /// </summary>
        public async Task<EstimateQuotationResponse> PreviewConsignmentAsync(
            CreateConsignmentRequest request, Guid userId)
        {
            var (route, shippingOption, shippingPricingId) =
                await ResolveRouteAndShippingOptionAsync(request.Route, request.ShippingOption);
            EnsureHasItems(request.Items);
            await EnsureItemsWithinLimitsAsync(request.Items!);
            EnsureWoodenCrateSelected(request.Items!);

            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new UnauthorizedAccessException("Không tìm thấy thông tin khách hàng tương ứng với tài khoản.");

            var draft = await BuildConsignmentDraftAsync(request.Items, request.Note, shippingPricingId);
            draft.DefaultDestinationHandling = NormalizeDestinationHandling(request.DefaultDestinationHandling);

            var (order, orderItems, itemServices) = await BuildInMemoryConsignmentAsync(
                customer, route, shippingOption, request.ReceiverName, request.ReceiverPhone,
                request.ReceiverAddress, request.Items, draft);

            return await _quotationService.PreviewEstimateAsync(order, orderItems, itemServices);
        }

        /// <summary>
        /// ƯỚC TÍNH ĐƠN SALE TẠO HỘ KHÁCH — bản song sinh của PreviewConsignmentAsync.
        ///
        /// Đi qua đúng các bước của CreateConsignmentByStaffAsync, kể cả cách tra ra khách
        /// (theo customerId / số điện thoại / email). Lưu ý: luồng Sale KHÔNG gọi
        /// EnsureWoodenCrateSelected — ước tính cũng không gọi, vì hai bên phải kiểm giống
        /// hệt nhau thì số mới giống nhau.
        /// </summary>
        public async Task<EstimateQuotationResponse> PreviewConsignmentByStaffAsync(
            CreateConsignmentByStaffRequest request, Guid staffUserId)
        {
            var (route, shippingOption, shippingPricingId) =
                await ResolveRouteAndShippingOptionAsync(request.Route, request.ShippingOption);
            EnsureHasItems(request.Items);
            await EnsureItemsWithinLimitsAsync(request.Items!);

            var customer = await ResolveCustomerForStaffAsync(request);

            var draft = await BuildConsignmentDraftAsync(request.Items, request.Note, shippingPricingId);
            draft.DefaultDestinationHandling = NormalizeDestinationHandling(request.DefaultDestinationHandling);

            var (order, orderItems, itemServices) = await BuildInMemoryConsignmentAsync(
                customer, route, shippingOption, request.ReceiverName, request.ReceiverPhone,
                request.ReceiverAddress, request.Items, draft);

            return await _quotationService.PreviewEstimateAsync(order, orderItems, itemServices);
        }

        /// <summary>
        /// Dựng Order + OrderItems trong BỘ NHỚ theo đúng cách PersistConsignmentAsync dựng
        /// chúng trước khi ghi — kể cả bước gợi ý cỡ thùng khi khách không tự chọn, vì cỡ thùng
        /// quyết định phí đóng gói.
        ///
        /// Không sinh mã đơn: mã chỉ cấp cho đơn thật, cấp ở đây sẽ đốt mất một số thứ tự.
        /// </summary>
        private async Task<(Order Order, List<OrderItem> Items, IReadOnlyDictionary<Guid, List<PricingRule>> ItemServices)>
            BuildInMemoryConsignmentAsync(
                Customer customer,
                string route,
                string shippingOption,
                string? receiverName,
                string? receiverPhone,
                string? receiverAddress,
                List<ConsignmentItemRequest> items,
                ConsignmentDraft draft)
        {
            var now = DateTime.UtcNow;

            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                CompanyId = customer.CompanyId,
                ConsignmentCode = string.Empty,
                ConsignmentType = shippingOption,
                Route = route,
                ReceiverName = receiverName,
                ReceiverPhone = receiverPhone,
                ReceiverAddress = receiverAddress,
                RequiresInspection = draft.RequiresInspection,
                DefaultDestinationHandling = draft.DefaultDestinationHandling,
                Status = "PREVIEW",
                OrderType = "CONSIGNMENT",
                Note = draft.Note,
                CreatedAt = now
            };

            var orderItems = new List<OrderItem>();
            var itemServices = new Dictionary<Guid, List<PricingRule>>();

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];

                Guid? packageConfigId = item.PackageConfigurationId;
                if (!packageConfigId.HasValue || packageConfigId.Value == Guid.Empty)
                {
                    packageConfigId = await SuggestPackageConfigurationIdAsync(
                        item.Length, item.Width, item.Height, item.Weight);
                }

                var orderItemId = Guid.NewGuid();
                orderItems.Add(new OrderItem
                {
                    Id = orderItemId,
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

                itemServices[orderItemId] = draft.ItemServices[index];
            }

            return (order, orderItems, itemServices);
        }

        public async Task<List<ItemServiceDto>> GetItemServicesAsync(string? route, string? shippingOption)
        {
            var rules = (await _unitOfWork.Repository<PricingRule>().GetAllAsync())
                .Where(ItemServiceRules.IsSelectable)
                .ToList();

            if (!string.IsNullOrWhiteSpace(route) && !string.IsNullOrWhiteSpace(shippingOption))
            {
                var (_, _, shippingPricingId) = await ResolveRouteAndShippingOptionAsync(route, shippingOption);
                rules = rules.Where(r => !r.ServicePricingId.HasValue || r.ServicePricingId == shippingPricingId).ToList();
            }

            return rules
                .OrderBy(r => r.RuleName)
                .Select(ItemServiceRules.ToDto)
                .ToList();
        }

        /// <summary>
        /// Thông tin đã tổng hợp từ request, dùng chung cho cả 2 luồng tạo ký gửi
        /// (khách tự tạo và Sale tạo hộ).
        /// </summary>
        /// <summary>
        /// Chuẩn hoá ý muốn của khách về đúng hai giá trị hệ thống hiểu.
        /// <para>
        /// Gõ sai thì trả null — coi như khách chưa chọn, kho sẽ hỏi lại. Không đoán bừa, vì
        /// đoán nhầm là hàng đi sai hướng: giao thẳng khi khách muốn gửi kho, hoặc ngược lại.
        /// </para>
        /// </summary>
        private static string? NormalizeDestinationHandling(string? value)
        {
            var normalized = value?.Trim().ToUpperInvariant();

            return normalized switch
            {
                "DIRECT_DELIVERY" => "DIRECT_DELIVERY",
                "STORE_AT_VN" => "STORE_AT_VN",
                _ => null,
            };
        }

        private sealed class ConsignmentDraft
        {
            /// <summary>Dịch vụ của từng kiện, cùng thứ tự với danh sách mặt hàng trong request.</summary>
            public List<List<PricingRule>> ItemServices { get; set; } = new();
            public bool RequiresInspection { get; set; }
            public string? DefaultDestinationHandling { get; set; }
            public string? Note { get; set; }
            public List<RestrictedItemWarning> Warnings { get; set; } = new();
        }

        /// <summary>
        /// Validate tuyến hàng + phương án vận chuyển theo bảng cấu hình giá,
        /// trả về giá trị đã chuẩn hoá theo cấu hình kèm dòng giá khớp (nếu có).
        /// </summary>
        private async Task<(string Route, string ShippingOption, Guid? ShippingPricingId)> ResolveRouteAndShippingOptionAsync(string? requestedRoute, string? requestedShippingOption)
        {
            if (string.IsNullOrWhiteSpace(requestedRoute))
                throw new ArgumentException("Vui lòng chọn tuyến hàng (route).");
            if (string.IsNullOrWhiteSpace(requestedShippingOption))
                throw new ArgumentException("Vui lòng chọn phương án vận chuyển (shippingOption).");

            var canonicalRoute = NormalizeRoute(requestedRoute);

            // Chỉ các dòng giá là phương án vận chuyển mới được khách chọn; phí lưu kho cũng
            // nằm trong bảng này nhưng không phải một cách gửi hàng.
            var servicePricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                .Where(p => !p.IsDeleted && ServicePricingHelper.IsShippingOption(p.ServiceType))
                .ToList();
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
            return (canonicalRoute, matchedPricing != null ? matchedPricing.ServiceType : requestedShippingOption, matchedPricing?.Id);
        }

        private static void EnsureHasItems(List<ConsignmentItemRequest>? items)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("Yêu cầu ký gửi phải có ít nhất một mặt hàng.");
        }

        /// <summary>
        /// Chặn kiện/đơn vượt hạn mức. Hạn mức do Admin cấu hình (SYSTEM_SETTINGS nhóm ORDER_LIMITS,
        /// xem <see cref="OrderLimitSettings"/>); giới hạn để trống = không giới hạn. Kích thước và cân
        /// nặng chỉ kiểm khi client có gửi, vì luồng Sale tạo hộ và mobile không bắt buộc nhập đủ.
        /// </summary>
        private async Task EnsureItemsWithinLimitsAsync(List<ConsignmentItemRequest> items)
        {
            var limits = await OrderLimitSettings.LoadAsync(_unitOfWork);

            if (items.Count > limits.MaxPackages)
                throw new ArgumentException($"Mỗi đơn ký gửi tối đa {limits.MaxPackages} kiện hàng.");

            foreach (var item in items)
            {
                var label = string.IsNullOrWhiteSpace(item.ProductName) ? "kiện hàng" : $"kiện \"{item.ProductName}\"";

                if (limits.MaxParcelQuantity.HasValue && item.Quantity > limits.MaxParcelQuantity.Value)
                    throw new ArgumentException($"Số lượng tối đa {limits.MaxParcelQuantity.Value} sản phẩm trên 1 kiện hàng ({label}).");

                if (limits.MaxParcelWeightKg.HasValue && item.Weight.HasValue && item.Weight.Value > limits.MaxParcelWeightKg.Value)
                    throw new ArgumentException($"Cân nặng tối đa {OrderLimitSettings.Kg(limits.MaxParcelWeightKg.Value)} kg cho mỗi kiện hàng ({label}).");

                if (limits.MaxParcelLengthCm.HasValue && item.Length.HasValue && item.Length.Value > limits.MaxParcelLengthCm.Value)
                    throw new ArgumentException($"Chiều dài tối đa {OrderLimitSettings.Cm(limits.MaxParcelLengthCm.Value)} cm ({label}).");

                if (limits.MaxParcelWidthCm.HasValue && item.Width.HasValue && item.Width.Value > limits.MaxParcelWidthCm.Value)
                    throw new ArgumentException($"Chiều rộng tối đa {OrderLimitSettings.Cm(limits.MaxParcelWidthCm.Value)} cm ({label}).");

                if (limits.MaxParcelHeightCm.HasValue && item.Height.HasValue && item.Height.Value > limits.MaxParcelHeightCm.Value)
                    throw new ArgumentException($"Chiều cao tối đa {OrderLimitSettings.Cm(limits.MaxParcelHeightCm.Value)} cm ({label}).");

                if (limits.MaxItemDeclaredValue.HasValue && item.DeclaredValue.HasValue && item.DeclaredValue.Value > limits.MaxItemDeclaredValue.Value)
                    throw new ArgumentException($"Giá trị 1 kiện hàng không được vượt quá {OrderLimitSettings.Money(limits.MaxItemDeclaredValue.Value)} đ ({label}).");
            }

            var totalWeight = items.Sum(i => i.Weight ?? 0m);
            if (limits.MaxTotalWeightKg.HasValue && totalWeight > limits.MaxTotalWeightKg.Value)
                throw new ArgumentException($"Tổng cân nặng toàn bộ đơn hàng ({OrderLimitSettings.Kg(totalWeight)} kg) vượt quá giới hạn tối đa {OrderLimitSettings.Kg(limits.MaxTotalWeightKg.Value)} kg.");

            var totalDeclaredValue = items.Sum(i => i.DeclaredValue ?? 0m);
            if (limits.MaxTotalDeclaredValue.HasValue && totalDeclaredValue > limits.MaxTotalDeclaredValue.Value)
                throw new ArgumentException($"Tổng giá trị hàng hóa của đơn hàng ({OrderLimitSettings.Money(totalDeclaredValue)} đ) vượt quá giới hạn tối đa {OrderLimitSettings.Money(limits.MaxTotalDeclaredValue.Value)} đ.");
        }

        /// <summary>
        /// Khách tạo đơn bắt buộc chọn cấu hình thùng gỗ cho từng kiện, nên chặn luôn ở server thay
        /// vì để <c>SuggestPackageConfigurationIdAsync</c> đoán hộ. Luồng Sale tạo hộ không áp ràng buộc này.
        /// </summary>
        private static void EnsureWoodenCrateSelected(List<ConsignmentItemRequest> items)
        {
            var missingCount = items.Count(i => !i.PackageConfigurationId.HasValue);
            if (missingCount > 0)
                throw new ArgumentException($"Còn {missingCount} kiện chưa có cấu hình thùng gỗ. Vui lòng chọn cấu hình cho từng kiện.");
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
        /// Đối chiếu hàng cấm/hạn chế, kiểm cấu hình thùng và dịch vụ khách chọn cho từng kiện.
        /// </summary>
        private async Task<ConsignmentDraft> BuildConsignmentDraftAsync(
            List<ConsignmentItemRequest> items, string? requestNote, Guid? shippingPricingId)
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

            // Cấu hình thùng gửi lên phải có thật và còn dùng; trước đây ID sai rơi xuống lỗi khoá ngoại.
            var configs = await ItemServiceRules.LoadPackageConfigurationsAsync(_unitOfWork);
            foreach (var item in items.Where(i => i.PackageConfigurationId.HasValue && i.PackageConfigurationId.Value != Guid.Empty))
            {
                if (!configs.TryGetValue(item.PackageConfigurationId!.Value, out var config)
                    || !string.Equals(config.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Cấu hình thùng của kiện \"{item.ProductName}\" không tồn tại hoặc đã ngừng dùng.");
                }
            }

            var itemServices = await ItemServiceRules.ResolveForItemsAsync(_unitOfWork, items, shippingPricingId);

            return new ConsignmentDraft
            {
                ItemServices = itemServices,
                RequiresInspection = itemServices.Any(rules => rules.Any(ItemServiceRules.IsInspection)),
                Note = string.IsNullOrWhiteSpace(requestNote) ? null : requestNote.Trim(),
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
                DefaultDestinationHandling = draft.DefaultDestinationHandling,
                Status = status,
                OrderType = "CONSIGNMENT",
                Note = draft.Note,
                CreatedById = createdById,
                CreatedAt = now
            };
            await _unitOfWork.Repository<Order>().AddAsync(order);

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var itemServiceRepo = _unitOfWork.Repository<OrderItemService>();
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                Guid? packageConfigId = item.PackageConfigurationId;
                if (!packageConfigId.HasValue || packageConfigId.Value == Guid.Empty)
                {
                    packageConfigId = await SuggestPackageConfigurationIdAsync(item.Length, item.Width, item.Height, item.Weight);
                }

                var orderItemId = Guid.NewGuid();
                await orderItemRepo.AddAsync(new OrderItem
                {
                    Id = orderItemId,
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

                foreach (var rule in draft.ItemServices[index])
                {
                    await itemServiceRepo.AddAsync(new OrderItemService
                    {
                        Id = Guid.NewGuid(),
                        OrderItemId = orderItemId,
                        PricingRuleId = rule.Id,
                        CreatedAt = now
                    });
                }
            }

            await _unitOfWork.SaveChangesAsync();

            // Bắn thông báo cho Sale & Admin khi khách tự tạo đơn ký gửi
            if (!createdById.HasValue)
            {
                var customerName = customer.FullName ?? customer.CustomerCode ?? "Khách hàng";
                await _notificationService.NotifyRolesAsync(
                    new[] { "Sale", "Sales", "Admin", "Staff" },
                    "Có đơn ký gửi mới cần xử lý",
                    $"Khách hàng {customerName} vừa tạo đơn ký gửi mới (Mã: {order.ConsignmentCode}). Vui lòng tiếp nhận và lập báo giá.");
            }

            // Sinh báo giá tạm tính (tái sử dụng QuotationService); phí dịch vụ tính theo từng kiện.
            var ruleIds = draft.ItemServices.SelectMany(rules => rules).Select(r => r.Id).Distinct().ToList();
            var quotation = await _quotationService.CreateEstimateQuotationAsync(order.Id);

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
