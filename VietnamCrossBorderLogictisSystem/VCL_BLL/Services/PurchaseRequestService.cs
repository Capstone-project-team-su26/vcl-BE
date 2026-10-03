using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class PurchaseRequestService : IPurchaseRequestService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOrderService _orderService;

        // Các role được phép xem toàn bộ yêu cầu mua hộ cần xử lý (Staff/Sales)
        // Vai trò khai báo tập trung ở StaffRoleHelper — xem ghi chú ở đó về lý do không để mỗi
        // service tự giữ một mảng riêng (kho từng không xem được đơn mua hộ vì lệch mảng).

        // Ký tự phân cách các danh sách lưu gộp trong 1 cột DB (dùng chung quy ước với ký gửi)
        private const char ListSeparator = '|';

        private const int MaxProofImages = 3;

        // Danh sách vai trò duyệt kho nay ở StaffRoleHelper.CanApproveWarehouseStore

        /// <summary>Nhãn tiếng Việt của trạng thái yêu cầu mua hộ, dùng chung cho mọi response.</summary>
        private static readonly Dictionary<string, string> StatusDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NEW"] = "Đơn mới tạo",
            ["PENDING_REVIEW"] = "Chờ xác nhận",
            ["IN_REVIEW"] = "Đang xem xét",
            ["NEED_MORE_INFO"] = "Cần bổ sung thông tin",
            ["REJECTED"] = "Đã từ chối",
            ["QUOTED"] = "Đã báo giá",
            // Khách từ chối BÁO GIÁ — khác REJECTED là Sale từ chối cả YÊU CẦU.
            ["QUOTATION_REJECTED"] = "Đã từ chối báo giá",
            ["WAITING_PAYMENT"] = "Chờ thanh toán",
            ["PAID"] = "Đã trả trước, chờ đặt mua",
            ["PURCHASING"] = "Đang mua hàng",
            ["DEPOSIT_PAID"] = "Đã đặt cọc",
            ["PURCHASED"] = "Đã mua hàng",
            ["SELLER_SHIPPED"] = "NCC đã phát hàng",
            ["ARRIVED_ORIGIN_WAREHOUSE"] = "Đã về kho nước ngoài",
            ["WAITING_STORED"] = "Chờ nhập kho",
            ["STORED"] = "Đã nhập kho",
            ["WAITING_FINAL_PAYMENT"] = "Chờ thanh toán cuối",
            ["COMPLETED"] = "Đơn đã hoàn tất",
            ["CANCELLED"] = "Đã hủy"
        };

        /// <remarks>internal: bảng tổng quan nhân viên dùng lại cùng nhãn.</remarks>
        internal static string GetStatusDisplayName(string? status) =>
            !string.IsNullOrWhiteSpace(status) && StatusDisplayNames.TryGetValue(status.Trim(), out var label)
                ? label
                // Mã lạ: tên chung tiếng Việt (StatusNames), không trả lại mã thô cho màn hình.
                : string.IsNullOrWhiteSpace(status) ? string.Empty : StatusNames.Of(status);

        private readonly IExchangeRateService _exchangeRateService;
        private readonly IConsignmentReceiptService _receiptService;
        private readonly INotificationService _notificationService;

        public PurchaseRequestService(
            IUnitOfWork unitOfWork,
            IOrderService orderService,
            IExchangeRateService exchangeRateService,
            IConsignmentReceiptService receiptService,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _orderService = orderService;
            _exchangeRateService = exchangeRateService;
            _receiptService = receiptService;
            _notificationService = notificationService;
        }

        public async Task<PurchaseRequestResponseDto> CreatePurchaseRequestAsync(Guid userId, string userRole, CreatePurchaseRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu mua hộ không được để trống.");

            // 1. Phân quyền: Chỉ Customer được tạo yêu cầu mua hộ
            if (!string.Equals(userRole, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Chỉ khách hàng mới được tạo yêu cầu mua hộ.");
            }

            // 2. Tìm Customer tương ứng với tài khoản đang đăng nhập
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
            {
                throw new UnauthorizedAccessException("Không tìm thấy thông tin khách hàng tương ứng với tài khoản.");
            }

            return await CreateForCustomerAsync(customer, request, createdByStaff: false);
        }

        /// <summary>
        /// Sale tạo yêu cầu mua hộ thay cho khách hàng (đối xứng với luồng ký gửi
        /// POST /api/staff/consignments). Đơn tạo ra thuộc về khách được chỉ định và
        /// đi theo đúng trạng thái ban đầu như khi khách tự tạo.
        /// </summary>
        public async Task<PurchaseRequestResponseDto> CreatePurchaseRequestByStaffAsync(
            Guid staffUserId, CreatePurchaseRequestByStaffDto request)
        {
            if (request == null)
                throw new ArgumentException("Yêu cầu mua hộ không được để trống.");

            var customer = await ResolveCustomerForStaffAsync(request);
            return await CreateForCustomerAsync(customer, request, createdByStaff: true);
        }

        /// <summary>
        /// Tìm khách hàng mà Sale tạo đơn hộ: theo ID được chọn, hoặc tra theo số điện thoại/email.
        /// </summary>
        private async Task<Customer> ResolveCustomerForStaffAsync(CreatePurchaseRequestByStaffDto request)
        {
            var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            return CustomerLookupHelper.ResolveForStaff(
                allCustomers, request.CustomerId, request.CustomerPhone, request.CustomerEmail);
        }

        /// <summary>
        /// Phần lõi dùng chung cho cả 2 luồng tạo yêu cầu mua hộ (khách tự tạo và Sale tạo hộ).
        /// </summary>
        private async Task<PurchaseRequestResponseDto> CreateForCustomerAsync(
            Customer customer, CreatePurchaseRequestDto request, bool createdByStaff)
        {
            // 3. Validate danh sách sản phẩm
            if (request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("Danh sách sản phẩm cần mua hộ phải có ít nhất một sản phẩm.");
            }

            // Web đã chặn trước; kiểm lại ở đây để gọi thẳng API (hoặc DTO dựng trong code) cũng không lọt
            // và lỗi luôn là 400 tiếng Việt thay vì lỗi SQL (cột ngắn hơn chuỗi gửi lên) 500.
            // Hạn mức số dòng / số lượng do Admin cấu hình (SYSTEM_SETTINGS nhóm ORDER_LIMITS).
            var limits = await OrderLimitSettings.LoadAsync(_unitOfWork);
            if (request.Items.Count > limits.MaxPurchaseItems)
            {
                throw new ArgumentException($"Mỗi yêu cầu mua hộ tối đa {limits.MaxPurchaseItems} dòng sản phẩm.");
            }

            var lineNo = 0;
            foreach (var item in request.Items)
            {
                lineNo++;
                if (item == null)
                {
                    throw new ArgumentException($"Sản phẩm thứ {lineNo} không có dữ liệu.");
                }
                if (string.IsNullOrWhiteSpace(item.ProductLink))
                {
                    throw new ArgumentException("Link sản phẩm không được để trống.");
                }
                if (!InputLimits.IsHttpUrl(item.ProductLink))
                {
                    throw new ArgumentException($"Link sản phẩm thứ {lineNo} không hợp lệ — phải là địa chỉ web bắt đầu bằng http:// hoặc https://.");
                }
                if (string.IsNullOrWhiteSpace(item.ProductType))
                {
                    throw new ArgumentException("Vui lòng chọn loại hàng cho từng sản phẩm.");
                }
                if (item.Quantity <= 0)
                {
                    throw new ArgumentException($"Số lượng sản phẩm thứ {lineNo} phải lớn hơn 0.");
                }
                if (limits.MaxPurchaseItemQuantity.HasValue && item.Quantity > limits.MaxPurchaseItemQuantity.Value)
                {
                    throw new ArgumentException($"Số lượng sản phẩm thứ {lineNo} phải từ 1 đến {limits.MaxPurchaseItemQuantity.Value}.");
                }

                EnsureMaxLength(item.ProductLink, InputLimits.ProductLinkLength, $"Link sản phẩm thứ {lineNo}");
                EnsureMaxLength(item.SourceWebsite, InputLimits.SourceWebsiteLength, $"Website nguồn của sản phẩm thứ {lineNo}");
                EnsureMaxLength(item.ProductType, InputLimits.ProductTypeLength, $"Loại hàng của sản phẩm thứ {lineNo}");
                EnsureMaxLength(item.ProductName, InputLimits.ProductNameLength, $"Tên sản phẩm thứ {lineNo}");
                EnsureMaxLength(item.Attributes, InputLimits.AttributesLength, $"Thuộc tính của sản phẩm thứ {lineNo}");
                EnsureMaxLength(item.Note, InputLimits.ItemNoteLength, $"Ghi chú của sản phẩm thứ {lineNo}");
                if ((item.ImageUrls?.Count(u => !string.IsNullOrWhiteSpace(u)) ?? 0) > InputLimits.MaxImagesPerPurchaseItem)
                {
                    throw new ArgumentException($"Sản phẩm thứ {lineNo} tối đa {InputLimits.MaxImagesPerPurchaseItem} ảnh tham khảo.");
                }
                if (CombineImageUrls(item).Length > InputLimits.ImageUrlsLength)
                {
                    throw new ArgumentException($"Danh sách ảnh của sản phẩm thứ {lineNo} quá dài (tối đa {InputLimits.ImageUrlsLength} ký tự) — bớt ảnh hoặc dùng ảnh tải lên hệ thống.");
                }
            }

            EnsureMaxLength(request.ReceiverName, InputLimits.ReceiverNameLength, "Tên người nhận");
            EnsureMaxLength(request.ReceiverPhone, InputLimits.ReceiverPhoneLength, "Số điện thoại người nhận");
            EnsureMaxLength(request.ReceiverAddress, InputLimits.ReceiverAddressLength, "Địa chỉ nhận hàng");

            // Ghi chú chung được lưu kèm tên dịch vụ đã tick (+ dấu "Sale lên hộ") — kiểm tổng sau khi ghép,
            // không để SQL cắt/báo lỗi 500.
            var generalNote = createdByStaff
                ? AppendStaffMark(BuildGeneralNote(request))
                : BuildGeneralNote(request);
            EnsureGeneralNoteFits(request.GeneralNote, generalNote);

            // 4. Validate tuyến hàng + phương án vận chuyển theo bảng cấu hình giá (giống luồng ký gửi)
            var (route, shippingOption) = await ResolveRouteAndShippingOptionAsync(request.Route, request.ShippingOption);

            // 5. Đối chiếu loại hàng với danh mục PRODUCT_TYPES (chuẩn hóa về đúng tên trong danh mục)
            var canonicalProductTypes = await ResolveProductTypesAsync(request.Items);

            // 6. Đối chiếu hàng cấm / hạn chế (dùng chung logic với ký gửi)
            var warnings = await ValidateAgainstRestrictedItemsAsync(request.Items);

            // 7. Khởi tạo yêu cầu mua hộ
            var purchaseCode = await GenerateUniquePurchaseCodeAsync();
            var purchaseRequest = new PurchaseRequest
            {
                Id = Guid.NewGuid(),
                // Đơn luôn thuộc về tài khoản của khách, kể cả khi Sale là người lên đơn hộ.
                UserId = customer.UserId,
                CustomerId = customer.Id,
                PurchaseCode = purchaseCode,
                Route = route,
                ShippingOption = shippingOption,
                ReceiverName = request.ReceiverName?.Trim(),
                ReceiverPhone = request.ReceiverPhone?.Trim(),
                ReceiverAddress = request.ReceiverAddress?.Trim(),
                RequiresPacking = request.RequiresPacking,
                RequiresWoodenCrate = request.RequiresWoodenCrate,
                RequiresInsurance = request.RequiresInsurance,
                // Chưa có cột CreatedById trên PURCHASE_REQUESTS nên đánh dấu vào ghi chú
                // để biết đơn do nhân viên lên hộ (đã ghép + kiểm độ dài ở trên).
                GeneralNote = generalNote,
                Status = "PENDING_REVIEW",
                CreatedAt = DateTime.UtcNow
            };

            // Quy đổi các cờ dịch vụ phụ sang PricingRule, rồi hợp nhất với danh sách FE gửi thẳng.
            // Mua hộ không có dịch vụ kiểm hàng -> luôn truyền false cho cờ INSPECTION.
            var pricingRuleIds = await _orderService.ResolveServiceFlagRuleIdsAsync(
                false, request.RequiresPacking,
                request.RequiresWoodenCrate, request.RequiresInsurance);

            foreach (var ruleId in await ResolveSelectedPricingRuleIdsAsync(request.PricingRuleIds))
            {
                if (!pricingRuleIds.Contains(ruleId))
                    pricingRuleIds.Add(ruleId);
            }

            purchaseRequest.PricingRuleIds = pricingRuleIds.Any()
                ? string.Join(ListSeparator, pricingRuleIds)
                : null;
            if (purchaseRequest.PricingRuleIds?.Length > InputLimits.PricingRuleIdsLength)
            {
                throw new ArgumentException("Chọn quá nhiều dịch vụ phụ cho một yêu cầu mua hộ.");
            }

            await _unitOfWork.Repository<PurchaseRequest>().AddAsync(purchaseRequest);

            // 8. Tạo các sản phẩm liên kết
            var itemRepo = _unitOfWork.Repository<PurchaseRequestItem>();
            var resultItems = new List<PurchaseRequestItemResponseDto>();
            var createdTypeNames = await ProductTypeNames.LoadAsync(_unitOfWork, canonicalProductTypes.Values);

            foreach (var item in request.Items)
            {
                var requestItem = new PurchaseRequestItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseRequestId = purchaseRequest.Id,
                    ProductLink = item.ProductLink.Trim(),
                    SourceWebsite = item.SourceWebsite?.Trim(),
                    ProductType = canonicalProductTypes[item],
                    ProductName = item.ProductName?.Trim(),
                    Quantity = item.Quantity,
                    Attributes = item.Attributes?.Trim(),
                    Note = item.Note?.Trim(),
                    ImageUrl = CombineImageUrls(item)
                };
                await itemRepo.AddAsync(requestItem);

                var returnedImageUrls = SplitImageUrls(requestItem.ImageUrl);

                resultItems.Add(new PurchaseRequestItemResponseDto
                {
                    ItemId = requestItem.Id,
                    ProductLink = requestItem.ProductLink,
                    SourceWebsite = requestItem.SourceWebsite,
                    ProductType = requestItem.ProductType,
                    ProductTypeName = ProductTypeNames.Resolve(createdTypeNames, requestItem.ProductType),
                    ProductName = requestItem.ProductName,
                    Quantity = requestItem.Quantity,
                    Attributes = requestItem.Attributes,
                    Note = requestItem.Note,
                    ImageUrls = returnedImageUrls
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // Bắn thông báo cho Sale & Admin khi khách tự tạo yêu cầu mua hộ
            if (!createdByStaff)
            {
                var customerName = customer.FullName ?? customer.CustomerCode ?? "Khách hàng";
                await _notificationService.NotifyRolesAsync(
                    new[] { "Sale", "Sales", "Admin", "Staff" },
                    "Có yêu cầu mua hộ mới cần báo giá",
                    $"Khách hàng {customerName} vừa tạo yêu cầu mua hộ mới (Mã: {purchaseRequest.PurchaseCode}). Vui lòng kiểm tra và lập báo giá.");
            }

            // 9. Trả về thông tin yêu cầu mua hộ vừa tạo
            return new PurchaseRequestResponseDto
            {
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode,
                CustomerId = purchaseRequest.CustomerId,
                UserId = customer.UserId,
                Route = OrderService.MapRouteToDisplayName(purchaseRequest.Route),
                ShippingOption = purchaseRequest.ShippingOption,
                ReceiverName = purchaseRequest.ReceiverName,
                ReceiverPhone = purchaseRequest.ReceiverPhone,
                ReceiverAddress = purchaseRequest.ReceiverAddress,
                RequiresPacking = purchaseRequest.RequiresPacking,
                RequiresWoodenCrate = purchaseRequest.RequiresWoodenCrate,
                RequiresInsurance = purchaseRequest.RequiresInsurance,
                GeneralNote = purchaseRequest.GeneralNote,
                Status = purchaseRequest.Status,
                CreatedAt = purchaseRequest.CreatedAt ?? DateTime.UtcNow,
                ItemCount = request.Items.Count,
                TotalQuantity = SafeTotalQuantity(request.Items.Select(i => i.Quantity)),
                PricingRuleIds = pricingRuleIds,
                Items = resultItems,
                Warnings = warnings
            };
        }

        /// <summary>
        /// Chuẩn hóa tuyến hàng và đối chiếu với bảng cấu hình giá đang active (cùng quy tắc với ký gửi).
        /// </summary>
        private async Task<(string Route, string ShippingOption)> ResolveRouteAndShippingOptionAsync(
            string? requestedRoute, string? requestedShippingOption)
        {
            if (string.IsNullOrWhiteSpace(requestedRoute))
                throw new ArgumentException("Vui lòng chọn tuyến hàng (route).");
            if (string.IsNullOrWhiteSpace(requestedShippingOption))
                throw new ArgumentException("Vui lòng chọn phương án vận chuyển (shippingOption).");

            var routeInput = requestedRoute.Trim();
            var optionInput = requestedShippingOption.Trim();
            var canonicalRoute = OrderService.NormalizeRoute(routeInput);

            var servicePricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                .Where(p => !p.IsDeleted
                    && !string.IsNullOrWhiteSpace(p.OriginCountry)
                    && !string.IsNullOrWhiteSpace(p.DestinationCountry))
                .ToList();

            // Chưa có cấu hình giá nào (môi trường dev/seed trống) -> không chặn
            if (!servicePricings.Any())
                return (canonicalRoute, optionInput.ToUpperInvariant());

            // Đối chiếu với đúng các tuyến đang lưu trong DB. Chấp nhận cả 3 dạng FE có thể gửi:
            // mã tuyến ("US-VN"), chỉ nước đi ("US"), hoặc tên hiển thị lấy từ dropdown routes.
            bool MatchesRoute(ServicePricing p)
            {
                var storedRoute = $"{p.OriginCountry}-{p.DestinationCountry}";
                return string.Equals(storedRoute, canonicalRoute, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(storedRoute, routeInput, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.OriginCountry, canonicalRoute, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.OriginCountry, routeInput, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(OrderService.MapRouteToDisplayName(storedRoute), routeInput, StringComparison.OrdinalIgnoreCase);
            }

            var pricingsOnRoute = servicePricings.Where(MatchesRoute).ToList();
            if (!pricingsOnRoute.Any())
            {
                var availableRoutes = string.Join(", ", servicePricings
                    .Select(p => OrderService.MapRouteToDisplayName($"{p.OriginCountry}-{p.DestinationCountry}"))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                throw new ArgumentException(
                    $"Tuyến '{requestedRoute}' không nằm trong các tuyến hệ thống đang khai thác. Các tuyến hiện có: {availableRoutes}.");
            }

            // Phương án vận chuyển phải thuộc đúng tuyến đã chọn (Express của tuyến này ≠ tuyến khác)
            var matchedPricing = pricingsOnRoute.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.ServiceType)
                && p.ServiceType.Equals(optionInput, StringComparison.OrdinalIgnoreCase));

            if (matchedPricing == null)
            {
                var availableOptions = string.Join(", ", pricingsOnRoute
                    .Where(p => !string.IsNullOrWhiteSpace(p.ServiceType))
                    .Select(p => p.ServiceType)
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                throw new ArgumentException(
                    $"Phương án '{requestedShippingOption}' không áp dụng cho tuyến '{requestedRoute}'. Các phương án hiện có: {availableOptions}.");
            }

            // Luôn lưu theo đúng giá trị chuẩn trong DB, không phụ thuộc cách FE gửi lên
            var resolvedRoute = $"{matchedPricing.OriginCountry}-{matchedPricing.DestinationCountry}".ToUpperInvariant();
            return (resolvedRoute, matchedPricing.ServiceType);
        }

        /// <summary>
        /// Kiểm tra danh sách PricingRule khách chọn: phải tồn tại và đang ACTIVE.
        /// Bỏ qua phí bắt buộc / phí nội địa vì hệ thống tự áp, khách không cần chọn.
        /// </summary>
        private async Task<List<Guid>> ResolveSelectedPricingRuleIdsAsync(List<Guid>? requestedRuleIds)
        {
            var resolved = new List<Guid>();
            if (requestedRuleIds == null || !requestedRuleIds.Any())
                return resolved;

            var ruleRepo = _unitOfWork.Repository<PricingRule>();

            foreach (var ruleId in requestedRuleIds.Distinct())
            {
                if (ruleId == Guid.Empty) continue;

                var rule = await ruleRepo.GetByIdAsync(ruleId);
                if (rule == null)
                    throw new ArgumentException($"Không tìm thấy quy định phí với ID: {ruleId}");

                if (!string.Equals(rule.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Quy định phí '{rule.RuleName}' không còn hoạt động.");

                // Phí hệ thống tự áp -> không đưa vào danh sách khách tự chọn
                if (rule.IsRequired || string.Equals(rule.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved.Add(rule.Id);
            }

            return resolved;
        }

        /// <summary>
        /// Đối chiếu loại hàng từng sản phẩm với danh mục PRODUCT_TYPES (nhận cả Id lẫn tên).
        /// Chuẩn hóa về đúng tên trong danh mục để bước báo giá tra được thuế nhập khẩu.
        /// </summary>
        private async Task<Dictionary<CreatePurchaseRequestItemDto, string>> ResolveProductTypesAsync(
            List<CreatePurchaseRequestItemDto> items)
        {
            var productTypes = (await _unitOfWork.Repository<ProductType>().GetAllAsync())
                .Where(t => t.IsActive)
                .ToList();

            // Danh mục chưa được cấu hình -> giữ nguyên giá trị khách gửi, không chặn
            if (!productTypes.Any())
                return items.ToDictionary(i => i, i => i.ProductType.Trim());

            var resolved = new Dictionary<CreatePurchaseRequestItemDto, string>();

            foreach (var item in items)
            {
                var input = item.ProductType.Trim();

                var matched = Guid.TryParse(input, out var typeId)
                    ? productTypes.FirstOrDefault(t => t.Id == typeId)
                    : productTypes.FirstOrDefault(t => string.Equals(t.Name?.Trim(), input, StringComparison.OrdinalIgnoreCase));

                if (matched == null)
                {
                    var available = string.Join(", ", productTypes.Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
                    throw new ArgumentException(
                        $"Loại hàng '{item.ProductType}' không có trong danh mục. Các loại hàng hiện có: {available}.");
                }

                resolved[item] = matched.Name?.Trim() ?? input;
            }

            return resolved;
        }

        /// <summary>
        /// Đối chiếu sản phẩm mua hộ với danh mục hàng cấm/hạn chế.
        /// Hàng cấm (BANNED) chặn tạo; hàng hạn chế/cảnh báo trả về cho khách qua Warnings.
        /// </summary>
        private async Task<List<RestrictedItemWarning>> ValidateAgainstRestrictedItemsAsync(List<CreatePurchaseRequestItemDto> items)
        {
            var validationRequest = new ValidateItemsRequest
            {
                Items = items.Select(i => new ConsignmentItemRequest
                {
                    ProductName = i.ProductName ?? string.Empty,
                    ProductType = i.ProductType ?? string.Empty,
                    Quantity = i.Quantity
                }).ToList()
            };

            var validation = await _orderService.ValidateConsignmentItemsAsync(validationRequest);

            var banned = validation.Results.Where(r => r.Level == "BANNED").ToList();
            if (banned.Count > 0)
            {
                var names = string.Join(", ", banned.Select(b => string.IsNullOrWhiteSpace(b.ProductName) ? b.MatchedItem : b.ProductName).Distinct());
                throw new ArgumentException($"Không thể tạo yêu cầu mua hộ. Các mặt hàng sau thuộc danh mục cấm: {names}");
            }

            return validation.Results
                .Where(r => r.Level != "BANNED")
                .Select(r => new RestrictedItemWarning
                {
                    ProductName = r.ProductName,
                    MatchedItem = r.MatchedItem,
                    RestrictionType = r.Level,
                    Note = r.Reason
                })
                .ToList();
        }

        /// <summary>
        /// Ghép ghi chú chung với các dịch vụ khách đã tick (cùng cách trình bày với ký gửi).
        /// </summary>
        private static string? BuildGeneralNote(CreatePurchaseRequestDto request)
        {
            var noteParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.GeneralNote))
                noteParts.Add(request.GeneralNote.Trim());
            if (request.RequiresPacking)
                noteParts.Add("Yêu cầu đóng gói lại");
            if (request.RequiresWoodenCrate)
                noteParts.Add("Yêu cầu đóng thùng gỗ");
            if (request.RequiresInsurance)
                noteParts.Add("Đăng ký bảo hiểm");

            return noteParts.Any() ? string.Join(". ", noteParts) : null;
        }

        private const string StaffCreatedMark = "Đơn do nhân viên lên hộ khách";

        /// <summary>Chuỗi vượt độ dài cột DB → 400 tiếng Việt (thay vì SQL báo "String or binary data would be truncated").</summary>
        private static void EnsureMaxLength(string? value, int maxLength, string label)
        {
            var length = value?.Trim().Length ?? 0;
            if (length > maxLength)
                throw new ArgumentException($"{label} tối đa {maxLength} ký tự (đang có {length}).");
        }

        /// <summary>
        /// Ghi chú chung lưu = ghi chú khách + tên các dịch vụ đã tick (+ dấu Sale lên hộ). Cột DB 1000 ký tự,
        /// nên báo cho khách còn được gõ tối đa bao nhiêu thay vì để SQL lỗi 500.
        /// </summary>
        private static void EnsureGeneralNoteFits(string? userNote, string? storedNote)
        {
            var storedLength = storedNote?.Length ?? 0;
            if (storedLength <= InputLimits.GeneralNoteLength)
                return;

            var userLength = userNote?.Trim().Length ?? 0;
            var systemPart = storedLength - userLength;
            var allowed = Math.Max(InputLimits.GeneralNoteLength - systemPart, 0);
            throw new ArgumentException(
                $"Ghi chú chung quá dài: tính cả tên dịch vụ đã chọn, ghi chú tối đa {InputLimits.GeneralNoteLength} ký tự. "
                + $"Vui lòng rút gọn ghi chú còn tối đa {allowed} ký tự (đang có {userLength}).");
        }

        /// <summary>Cộng số lượng không tràn int (dữ liệu cũ có thể có dòng số lượng rất lớn).</summary>
        internal static int SafeTotalQuantity(IEnumerable<int> quantities)
        {
            long total = 0;
            foreach (var quantity in quantities)
                total += Math.Max(quantity, 0);
            return (int)Math.Min(total, int.MaxValue);
        }

        private static string AppendStaffMark(string? note) =>
            string.IsNullOrWhiteSpace(note) ? StaffCreatedMark : $"{note}. {StaffCreatedMark}";

        /// <summary>Sinh mã yêu cầu mua hộ không trùng với mã đã có.</summary>
        private async Task<string> GenerateUniquePurchaseCodeAsync()
        {
            var existingCodes = (await _unitOfWork.Repository<PurchaseRequest>().GetAllAsync())
                .Where(r => !string.IsNullOrEmpty(r.PurchaseCode))
                .Select(r => r.PurchaseCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var random = new Random();
            string code;
            do
            {
                code = $"PUR-{DateTime.UtcNow:yyyyMMddHHmmss}-{random.Next(100000, 999999)}";
            } while (existingCodes.Contains(code));

            return code;
        }

        /// <summary>Gộp danh sách ảnh thành 1 chuỗi lưu DB (phân cách "|"), bỏ trùng/rỗng.</summary>
        private static string CombineImageUrls(CreatePurchaseRequestItemDto item)
        {
            if (item.ImageUrls == null) return string.Empty;

            var distinct = item.ImageUrls
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return distinct.Any() ? string.Join(ListSeparator.ToString(), distinct) : string.Empty;
        }

        /// <summary>Tách chuỗi ID quy định phí lưu DB thành danh sách Guid, bỏ qua giá trị hỏng.</summary>
        internal static List<Guid> SplitPricingRuleIds(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return new List<Guid>();

            return stored
                .Split(new[] { ListSeparator, ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Guid.TryParse(part.Trim(), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
        }

        /// <summary>Tách chuỗi ảnh lưu DB thành danh sách URL. Hỗ trợ cả dữ liệu cũ phân cách bằng ",".</summary>
        internal static List<string> SplitImageUrls(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return new List<string>();

            return stored
                .Split(new[] { ListSeparator, ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => u.Length > 0)
                .ToList();
        }

        public async Task<PaginatedResponse<PurchaseRequestListItemDto>> GetPurchaseRequestsAsync(
            Guid? userId, string userRole, int pageNumber, int pageSize, string? status, string? searchKeyword, string? stage = null)
        {
            // Lọc theo CHẶNG THẬT (PurchaseRequestStage) — kiểm mã trước khi đọc dữ liệu.
            var stageFilter = stage?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(stageFilter) && stageFilter != "ALL" && !PurchaseRequestStage.IsKnown(stageFilter))
            {
                throw new ArgumentException(
                    $"Chặng không hợp lệ. Hợp lệ: {string.Join(", ", PurchaseRequestStage.All)}.");
            }
            if (stageFilter == "ALL") stageFilter = null;

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var itemRepo = _unitOfWork.Repository<PurchaseRequestItem>();
            var customerRepo = _unitOfWork.Repository<Customer>();

            var allRequests = await requestRepo.GetAllAsync();
            var allItems = await itemRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();

            var query = allRequests.AsQueryable();

            bool isStaff = StaffRoleHelper.IsInternalStaff(userRole);

            // 1. Phân quyền: Customer chỉ xem yêu cầu của chính mình, Staff/Sales xem tất cả
            if (!isStaff)
            {
                if (userId == null)
                {
                    throw new UnauthorizedAccessException("Người dùng không hợp lệ.");
                }

                var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null)
                {
                    // Không phải Staff và không có hồ sơ khách hàng -> trả về danh sách rỗng
                    return new PaginatedResponse<PurchaseRequestListItemDto>
                    {
                        Items = new List<PurchaseRequestListItemDto>(),
                        TotalCount = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                query = query.Where(r => r.CustomerId == customer.Id);
            }

            // 2. Lọc theo trạng thái
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => string.Equals(r.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            // 3. Tìm kiếm theo mã yêu cầu hoặc tên Customer
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var keyword = searchKeyword.Trim();
                var matchedCustomerIds = allCustomers
                    .Where(c => !string.IsNullOrEmpty(c.FullName)
                        && c.FullName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Id)
                    .ToHashSet();

                query = query.Where(r =>
                    (r.PurchaseCode != null && r.PurchaseCode.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    || (r.CustomerId.HasValue && matchedCustomerIds.Contains(r.CustomerId.Value)));
            }

            var allQuotations = await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync();

            // 3b. Lọc theo chặng thật: phải suy chặng cho mọi yêu cầu còn lại TRƯỚC khi phân trang.
            Dictionary<Guid, PurchaseStageResult>? stageById = null;
            if (stageFilter != null)
            {
                var candidates = query.ToList();
                var candidateProgress = await BuildPurchaseProgressAsync(
                    candidates.Select(r => r.Id).ToHashSet(), allQuotations, allItems);
                stageById = await PurchaseRequestStage.ComputeAsync(_unitOfWork, candidates,
                    candidateProgress.ToDictionary(kv => kv.Key, kv => kv.Value.OpenLineCount));
                var matchedIds = stageById.Where(kv => kv.Value.Stage == stageFilter).Select(kv => kv.Key).ToHashSet();
                query = candidates.Where(r => matchedIds.Contains(r.Id)).AsQueryable();
            }

            var totalCount = query.Count();

            // 4. Ưu tiên PENDING_REVIEW lên đầu, sau đó theo ngày tạo mới nhất; rồi phân trang
            var pagedRequests = query
                .OrderByDescending(r => string.Equals(r.Status, "PENDING_REVIEW", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(r => r.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // 5. Mốc báo giá mới nhất của từng yêu cầu (chỉ tra cho trang hiện tại)
            var pagedRequestIds = pagedRequests.Select(r => r.Id).ToHashSet();
            var quotationCreatedAtByRequest = allQuotations
                .Where(q => pagedRequestIds.Contains(q.PurchaseRequestId) && q.CreatedAt.HasValue)
                .GroupBy(q => q.PurchaseRequestId)
                .ToDictionary(g => g.Key, g => g.Max(q => q.CreatedAt!.Value));

            // Tên kho cho các dòng có chọn kho — tra một lần rồi map, không query trong vòng lặp
            var warehouseIds = pagedRequests
                .Where(r => r.ExpectedWarehouseId.HasValue)
                .Select(r => r.ExpectedWarehouseId!.Value)
                .ToHashSet();
            var warehouseNames = warehouseIds.Count == 0
                ? new Dictionary<Guid, string?>()
                : (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                    .Where(w => warehouseIds.Contains(w.Id))
                    .ToDictionary(w => w.Id, w => (string?)w.WarehouseName);

            // Khách + tình trạng lập đơn mua + tiền trả trước đã thu — tra một lần cho cả trang (web Sale trước đây
            // phải gọi danh bạ khách + đơn mua của từng yêu cầu).
            var customersById = allCustomers
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First());
            // Hồ sơ khách tạo từ đăng ký nhanh có thể trống tên/SĐT — lấy từ tài khoản đăng nhập của khách.
            var pageUserIds = pagedRequests
                .Select(r => r.CustomerId.HasValue && customersById.TryGetValue(r.CustomerId.Value, out var c) ? c.UserId : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();
            var usersById = pageUserIds.Count == 0
                ? new Dictionary<Guid, User>()
                : (await _unitOfWork.Repository<User>().GetAllAsync())
                    .Where(u => pageUserIds.Contains(u.Id))
                    .ToDictionary(u => u.Id);
            var purchaseProgress = await BuildPurchaseProgressAsync(pagedRequestIds, allQuotations, allItems);
            stageById ??= await PurchaseRequestStage.ComputeAsync(_unitOfWork, pagedRequests,
                purchaseProgress.ToDictionary(kv => kv.Key, kv => kv.Value.OpenLineCount));

            // 6. Map sang DTO (kèm tên người nhận và danh sách sản phẩm tóm tắt)
            var resultItems = pagedRequests.Select(r =>
            {
                var products = allItems
                    .Where(i => i.PurchaseRequestId == r.Id)
                    .Select(i => new PurchaseRequestListProductDto
                    {
                        ProductName = i.ProductName,
                        Quantity = i.Quantity
                    })
                    .ToList();

                var quotationCreatedAt = quotationCreatedAtByRequest.TryGetValue(r.Id, out var quotedAt)
                    ? quotedAt
                    : (DateTime?)null;

                var customer = r.CustomerId.HasValue && customersById.TryGetValue(r.CustomerId.Value, out var found)
                    ? found
                    : null;
                var progress = purchaseProgress.TryGetValue(r.Id, out var p) ? p : default;
                var account = customer?.UserId != null && usersById.TryGetValue(customer.UserId.Value, out var u) ? u : null;

                return new PurchaseRequestListItemDto
                {
                    PurchaseRequestId = r.Id,
                    PurchaseCode = r.PurchaseCode ?? string.Empty,
                    CustomerId = r.CustomerId,
                    Route = OrderService.MapRouteToDisplayName(r.Route),
                    ShippingOption = r.ShippingOption,
                    ReceiverName = r.ReceiverName ?? string.Empty,
                    ItemCount = products.Count,
                    TotalQuantity = SafeTotalQuantity(products.Select(p => p.Quantity)),
                    Status = r.Status ?? string.Empty,
                    GeneralNote = r.GeneralNote,
                    CreatedAt = r.CreatedAt ?? DateTime.MinValue,
                    QuotationCreatedAt = quotationCreatedAt,
                    StatusUpdatedAt = quotationCreatedAt ?? r.CreatedAt,
                    StatusDisplayName = GetStatusDisplayName(r.Status),
                    OverallStage = stageById[r.Id].Stage,
                    OverallStageText = stageById[r.Id].Text,
                    IsFullyCompleted = stageById[r.Id].IsFullyCompleted,
                    WarehouseId = r.ExpectedWarehouseId,
                    WarehouseName = r.ExpectedWarehouseId.HasValue
                        && warehouseNames.TryGetValue(r.ExpectedWarehouseId.Value, out var whName)
                        ? whName
                        : null,
                    CustomerName = FirstFilled(customer?.FullName, account?.FullName),
                    CustomerCode = FirstFilled(customer?.CustomerCode),
                    CustomerPhone = FirstFilled(customer?.Phone, account?.Phone),
                    ActivePurchaseOrderCount = progress.ActiveOrderCount,
                    OpenLineCount = progress.OpenLineCount,
                    OpenQuantity = progress.OpenQuantity,
                    ClosedQuantity = progress.ClosedQuantity,
                    PrepaidAmount = progress.PrepaidAmount,
                    Items = products
                };
            }).ToList();

            return new PaginatedResponse<PurchaseRequestListItemDto>
            {
                Items = resultItems,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        private static string? FirstFilled(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        private readonly record struct PurchaseProgress(
            int ActiveOrderCount, int OpenLineCount, int OpenQuantity, int ClosedQuantity, decimal PrepaidAmount);

        /// <summary>
        /// Tình trạng lập đơn mua NCC + tiền trả trước đã thu cho các yêu cầu của trang hiện tại. Luật "còn mua
        /// được" giống hệt PurchaseOrderService.ApplyContentAsync: mỗi sản phẩm chỉ nằm trong MỘT đơn mua chưa huỷ,
        /// phải có trong báo giá khách đã chấp nhận, SL ≤ SL khách đặt − SL đã đóng "không mua được".
        /// </summary>
        private async Task<Dictionary<Guid, PurchaseProgress>> BuildPurchaseProgressAsync(
            HashSet<Guid> requestIds, IEnumerable<PurchaseQuotation> allQuotations, IEnumerable<PurchaseRequestItem> allItems)
        {
            var result = new Dictionary<Guid, PurchaseProgress>();
            if (requestIds.Count == 0)
                return result;

            var acceptedQuotationByRequest = allQuotations
                .Where(q => requestIds.Contains(q.PurchaseRequestId)
                    && string.Equals(q.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                .GroupBy(q => q.PurchaseRequestId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.CreatedAt ?? DateTime.MinValue).First().Id);
            var acceptedQuotationIds = acceptedQuotationByRequest.Values.ToHashSet();
            var quotedItemIds = acceptedQuotationIds.Count == 0
                ? new HashSet<(Guid, Guid)>()
                : (await _unitOfWork.Repository<PurchaseQuotationItem>().GetAllAsync())
                    .Where(qi => acceptedQuotationIds.Contains(qi.PurchaseQuotationId))
                    .Select(qi => (qi.PurchaseQuotationId, qi.PurchaseRequestItemId))
                    .ToHashSet();

            var activeOrders = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(o => requestIds.Contains(o.PurchaseRequestId) && PurchaseOrderStatuses.IsActive(o))
                .ToList();
            var activeOrderIds = activeOrders.Select(o => o.Id).ToHashSet();
            var takenItemIds = activeOrderIds.Count == 0
                ? new HashSet<Guid>()
                : (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                    .Where(i => activeOrderIds.Contains(i.PurchaseOrderId))
                    .Select(i => i.PurchaseRequestItemId)
                    .ToHashSet();

            var payments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => requestIds.Contains(p.PurchaseRequestId))
                .ToList();

            // Phần đã đóng "không mua được": dòng hoàn tiền UNFULFILLED của khoản hoàn còn hiệu lực.
            var liveRefundIds = payments
                .Where(p => PurchasePaymentTypes.IsRefund(p)
                    && !string.Equals(p.Status?.Trim(), PurchaseRefundStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet();
            var closedQtyByItem = liveRefundIds.Count == 0
                ? new Dictionary<Guid, int>()
                : (await _unitOfWork.Repository<PurchaseRefundLine>().GetAllAsync())
                    .Where(l => liveRefundIds.Contains(l.PurchasePaymentId) && l.PurchaseRequestItemId.HasValue
                        && string.Equals(l.ReasonCode?.Trim(), PurchaseRefundReasons.Unfulfilled, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(l => l.PurchaseRequestItemId!.Value)
                    .ToDictionary(g => g.Key, g => SafeTotalQuantity(g.Select(l => l.Quantity)));

            var itemsByRequest = allItems
                .Where(i => requestIds.Contains(i.PurchaseRequestId))
                .GroupBy(i => i.PurchaseRequestId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var requestId in requestIds)
            {
                var items = itemsByRequest.TryGetValue(requestId, out var list) ? list : new List<PurchaseRequestItem>();
                var hasAccepted = acceptedQuotationByRequest.TryGetValue(requestId, out var quotationId);

                int openLines = 0;
                long openQuantity = 0, closedQuantity = 0;
                foreach (var item in items)
                {
                    var closed = closedQtyByItem.GetValueOrDefault(item.Id);
                    closedQuantity += closed;
                    if (!hasAccepted || !quotedItemIds.Contains((quotationId, item.Id)) || takenItemIds.Contains(item.Id))
                        continue;
                    var remaining = Math.Max(item.Quantity - closed, 0);
                    if (remaining <= 0)
                        continue;
                    openLines++;
                    openQuantity += remaining;
                }

                var prepaid = payments
                    .Where(p => p.PurchaseRequestId == requestId
                        && PurchasePaymentTypes.IsFirstInstalment(p) && PurchasePaymentTypes.IsPaid(p))
                    .Sum(p => p.Amount);

                result[requestId] = new PurchaseProgress(
                    activeOrders.Count(o => o.PurchaseRequestId == requestId),
                    openLines,
                    (int)Math.Min(openQuantity, int.MaxValue),
                    (int)Math.Min(closedQuantity, int.MaxValue),
                    prepaid);
            }

            return result;
        }

        public async Task<PurchaseRequestDetailResponseDto> GetPurchaseRequestDetailAsync(Guid requestId, Guid? userId, string userRole)
        {
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();

            // 1. Tìm yêu cầu mua hộ (404 nếu không có)
            var request = await requestRepo.GetByIdAsync(requestId);
            if (request == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            // 2. Phân quyền: Customer chỉ xem yêu cầu của chính mình; Staff/Sales xem tất cả
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();

            bool isStaff = StaffRoleHelper.IsInternalStaff(userRole);

            if (!isStaff)
            {
                if (userId == null)
                {
                    throw new UnauthorizedAccessException("Người dùng không hợp lệ.");
                }

                var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null || request.CustomerId != customer.Id)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền xem yêu cầu mua hộ này.");
                }
            }

            // 3. Lấy tên Customer sở hữu + người tạo
            var owner = request.CustomerId.HasValue
                ? allCustomers.FirstOrDefault(c => c.Id == request.CustomerId.Value)
                : null;

            string createdByName = owner?.FullName ?? string.Empty;
            if (request.UserId.HasValue)
            {
                var creator = await _unitOfWork.Repository<User>().GetByIdAsync(request.UserId.Value);
                if (creator != null) createdByName = creator.FullName ?? createdByName;
            }

            // 4. Lấy danh sách sản phẩm của yêu cầu
            var allItems = await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync();
            var relatedItems = allItems.Where(i => i.PurchaseRequestId == requestId).ToList();
            var itemTypeNames = await ProductTypeNames.LoadAsync(_unitOfWork, relatedItems.Select(i => i.ProductType));

            var itemDtos = relatedItems.Select(i => {
                var urls = SplitImageUrls(i.ImageUrl);
                return new PurchaseRequestItemResponseDto
                {
                    ItemId = i.Id,
                    ProductLink = i.ProductLink ?? string.Empty,
                    SourceWebsite = i.SourceWebsite,
                    ProductType = i.ProductType,
                    ProductTypeName = ProductTypeNames.Resolve(itemTypeNames, i.ProductType),
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    Attributes = i.Attributes,
                    Note = i.Note,
                    ImageUrls = urls
                };
            }).ToList();

            var expectedWarehouse = request.ExpectedWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(request.ExpectedWarehouseId.Value)
                : null;

            // Đơn kho chỉ tồn tại sau khi duyệt nhập kho; lấy để trả kèm mã đơn và phiếu PDF,
            // nhờ đó khách và Sale tra lại phiếu bất cứ lúc nào thay vì chỉ thấy đúng một lần
            // ở response của bước duyệt.
            var warehouseOrder = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .FirstOrDefault(o => o.PurchaseRequestId == request.Id);

            // Hành trình lô của đơn — dựng lại bằng đúng hàm luồng ký gửi đang dùng, để Sale
            // nhìn hai loại đơn thấy cùng một khối thông tin.
            // Chặng thật đầu–cuối (đơn mua NCC + đơn kho + khoản hoàn), không chỉ status của yêu cầu.
            var detailProgress = await BuildPurchaseProgressAsync(
                new HashSet<Guid> { request.Id },
                await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync(),
                relatedItems);
            var overallStage = (await PurchaseRequestStage.ComputeAsync(_unitOfWork, new[] { request },
                detailProgress.ToDictionary(kv => kv.Key, kv => kv.Value.OpenLineCount)))[request.Id];

            var shipmentGroups = warehouseOrder != null
                ? await _orderService.GetOrderShipmentJourneyAsync(warehouseOrder.Id)
                : new List<ConsignmentShipmentGroup>();

            // 4.1 Lấy báo giá liên quan nếu có
            PurchaseQuotationResponseDto? quotationDto = null;
            var quotationRepo = _unitOfWork.Repository<PurchaseQuotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            // Lấy báo giá mới nhất (giống luồng ký gửi) — trước đây FirstOrDefault trả về
            // bản ngẫu nhiên khi một yêu cầu có nhiều lần báo giá.
            var quotation = allQuotations
                .Where(q => q.PurchaseRequestId == requestId)
                .OrderByDescending(q => q.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();

            if (quotation != null)
            {
                var quotationItemRepo = _unitOfWork.Repository<PurchaseQuotationItem>();
                var allQuoItems = await quotationItemRepo.GetAllAsync();
                var quoItems = allQuoItems.Where(qi => qi.PurchaseQuotationId == quotation.Id).ToList();

                var itemMap = relatedItems.ToDictionary(i => i.Id);
                var quotationItemDtos = quoItems.Select(qi => new PurchaseQuotationItemResponseDto
                {
                    QuotationItemId = qi.Id,
                    PurchaseRequestItemId = qi.PurchaseRequestItemId,
                    ProductName = itemMap.ContainsKey(qi.PurchaseRequestItemId) ? itemMap[qi.PurchaseRequestItemId].ProductName : string.Empty,
                    UnitPrice = qi.UnitPrice,
                    UnitPriceOriginal = qi.UnitPriceOriginal,
                    Quantity = qi.Quantity,
                    LineTotal = qi.LineTotal
                }).ToList();

                var feeRepo = _unitOfWork.Repository<QuotationFee>();
                var allFees = await feeRepo.GetAllAsync();
                var fees = allFees.Where(f => f.PurchaseQuotationId == quotation.Id).ToList();

                var feeDtos = fees.Select(f => new PurchaseQuotationAdditionalFeeResponseDto
                {
                    Id = f.Id,
                    PricingRuleId = f.PricingRuleId,
                    FeeName = f.FeeName,
                    FeeType = f.FeeType,
                    CalculationType = f.CalculationType,
                    Value = f.Value,
                    Amount = f.Amount,
                    Note = f.Note,
                    CreatedAt = f.CreatedAt
                }).ToList();

                quotationDto = new PurchaseQuotationResponseDto
                {
                    QuotationId = quotation.Id,
                    PurchaseRequestId = requestId,
                    PurchaseCode = request.PurchaseCode ?? string.Empty,
                    ProductsSubtotal = quotation.ProductsSubtotal,
                    PurchaseFee = quotation.PurchaseFee,
                    ShippingFee = quotation.ShippingFee,
                    DomesticShippingFee = quotation.DomesticShippingFee,
                    FreightRatePerKg = quotation.FreightRatePerKg,
                    EstimatedWeight = quotation.EstimatedWeight,
                    VatRate = quotation.VatRate,
                    Vat = quotation.Vat ?? 0m,
                    ImportTax = quotation.ImportTax ?? 0m,
                    TotalAmount = quotation.TotalAmount,
                    Currency = quotation.Currency,
                    ExchangeRate = quotation.ExchangeRate,
                    ExchangeRateAt = quotation.ExchangeRateAt,
                    Note = quotation.Note,
                    Status = quotation.Status,
                    CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                    Items = quotationItemDtos,
                    AdditionalFees = feeDtos
                };
                PurchasePrepayRule.Fill(quotationDto, quotation);
            }

            return new PurchaseRequestDetailResponseDto
            {
                PurchaseRequestId = request.Id,
                PurchaseCode = request.PurchaseCode ?? string.Empty,
                CustomerId = request.CustomerId,
                CustomerName = owner?.FullName ?? string.Empty,
                CreatedByName = createdByName,
                Route = OrderService.MapRouteToDisplayName(request.Route),
                ShippingOption = request.ShippingOption,
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                ReceiverAddress = request.ReceiverAddress,
                RequiresPacking = request.RequiresPacking,
                RequiresWoodenCrate = request.RequiresWoodenCrate,
                RequiresInsurance = request.RequiresInsurance,
                PricingRuleIds = SplitPricingRuleIds(request.PricingRuleIds),
                GeneralNote = request.GeneralNote,
                Status = request.Status ?? string.Empty,
                Reason = request.Reason,
                CreatedAt = request.CreatedAt ?? DateTime.MinValue,
                QuotationCreatedAt = quotation?.CreatedAt,
                StatusUpdatedAt = quotation?.CreatedAt ?? request.CreatedAt,
                StatusDisplayName = GetStatusDisplayName(request.Status),
                OverallStage = overallStage.Stage,
                OverallStageText = overallStage.Text,
                IsFullyCompleted = overallStage.IsFullyCompleted,
                WarehouseId = request.ExpectedWarehouseId,
                WarehouseName = expectedWarehouse?.WarehouseName,
                ProofImages = SplitList(request.ProofImageUrls),
                OrderCode = warehouseOrder?.ConsignmentCode,
                ReceiptPdfUrl = warehouseOrder?.ReceiptPdfUrl,
                ReceiptGeneratedAt = warehouseOrder?.ReceiptGeneratedAt,
                TotalQuantity = SafeTotalQuantity(relatedItems.Select(i => i.Quantity)),
                Items = itemDtos,
                Quotation = quotationDto,
                Shipments = shipmentGroups,
                DiscrepancyParcelCount = shipmentGroups
                    .SelectMany(g => g.Parcels)
                    .Count(p => p.Inspection?.HasDiscrepancy == true)
            };
        }

        /// <summary>
        /// Khách từ chối báo giá mua hộ.
        /// <para>
        /// Luồng ký gửi có sẵn <c>PUT /api/quotations/{id}/reject</c>, nhưng nó thao tác trên bảng
        /// QUOTATIONS của ký gửi nên gọi bằng id báo giá mua hộ chỉ ra 404. Khách vì thế không có
        /// đường nào từ chối báo giá mua hộ — chỉ có đường đồng ý và trả tiền.
        /// </para>
        /// Chỉ từ chối được khi đơn đang ở <c>QUOTED</c>/<c>WAITING_PAYMENT</c> và chưa phát sinh
        /// thanh toán nào; đã trả tiền thì phải đi đường huỷ đơn/hoàn tiền, không phải từ chối báo giá.
        /// </summary>
        public async Task<UpdatePurchaseRequestStatusResponseDto> RejectPurchaseQuotationAsync(
            Guid requestId, Guid? userId, string userRole, string? rejectionReason)
        {
            var reason = rejectionReason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Vui lòng nhập lý do từ chối báo giá.");

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");

            // Khách chỉ từ chối báo giá của chính mình; nhân viên từ chối hộ khách thì chỉ Sale/Admin
            // (cùng nhóm với lập báo giá) — kho, giao vận, OM không được đụng vào báo giá.
            if (StaffRoleHelper.IsInternalStaff(userRole))
            {
                if (!StaffRoleHelper.CanQuotePurchase(userRole))
                    throw new UnauthorizedAccessException("Chỉ Sale hoặc Admin được từ chối báo giá yêu cầu mua hộ thay khách.");
            }
            else
            {
                var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .FirstOrDefault(c => c.UserId == userId);
                if (customer == null || purchaseRequest.CustomerId != customer.Id)
                    throw new UnauthorizedAccessException("Bạn không có quyền từ chối báo giá của đơn này.");
            }

            var currentStatus = purchaseRequest.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (currentStatus is not ("QUOTED" or "WAITING_PAYMENT"))
            {
                throw new ArgumentException(
                    $"Chỉ từ chối được báo giá khi đơn đang ở {StatusNames.QuoteAny("QUOTED", "WAITING_PAYMENT")} (hiện tại: {StatusNames.Quote(purchaseRequest.Status)}).");
            }

            // Đã có tiền vào thì không cho từ chối âm thầm — dữ liệu thanh toán sẽ mồ côi.
            var hasPayment = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Any(p => p.PurchaseRequestId == requestId
                          && !string.Equals(p.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(p.Status, "FAILED", StringComparison.OrdinalIgnoreCase));
            if (hasPayment)
                throw new ArgumentException("Đơn đã phát sinh thanh toán, không thể từ chối báo giá. Vui lòng liên hệ CSKH để huỷ đơn.");

            var quotation = (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                .Where(q => q.PurchaseRequestId == requestId)
                .OrderByDescending(q => q.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();

            if (quotation != null)
            {
                quotation.Status = "REJECTED";
                quotation.Note = string.IsNullOrWhiteSpace(quotation.Note)
                    ? $"Khách từ chối: {reason}"
                    : $"{quotation.Note} | Khách từ chối: {reason}";
                _unitOfWork.Repository<PurchaseQuotation>().Update(quotation);
            }

            var actor = (await _unitOfWork.Repository<User>().GetAllAsync())
                .FirstOrDefault(u => u.Id == userId);

            await _unitOfWork.Repository<PurchaseRequestHistory>().AddAsync(new PurchaseRequestHistory
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                FromStatus = purchaseRequest.Status,
                ToStatus = "QUOTATION_REJECTED",
                Note = reason,
                ChangedBy = userId,
                ChangedByName = actor?.FullName,
                CreatedAt = DateTime.UtcNow
            });

            purchaseRequest.Status = "QUOTATION_REJECTED";
            purchaseRequest.Reason = reason;
            requestRepo.Update(purchaseRequest);
            await _unitOfWork.SaveChangesAsync();

            return new UpdatePurchaseRequestStatusResponseDto
            {
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                Status = purchaseRequest.Status,
                Reason = purchaseRequest.Reason
            };
        }

        /// <summary>
        /// Mốc khách hàng cần được báo, kèm nội dung thông báo. Cố ý KHÔNG báo mọi bước:
        /// các trạng thái nội bộ (PENDING_REVIEW, PAID, ARRIVED_ORIGIN_WAREHOUSE, WAITING_STORED)
        /// là việc vận hành giữa Sale và kho, báo hết chỉ làm phiền khách.
        /// </summary>
        private static readonly Dictionary<string, (string Title, string Content)> CustomerNotifications =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PURCHASED"] = ("Đơn mua hộ đã được đặt hàng",
                    "Yêu cầu mua hộ (Mã: {0}) đã được đặt hàng thành công với người bán."),
                ["SELLER_SHIPPED"] = ("Người bán đã giao hàng",
                    "Người bán đã giao hàng cho yêu cầu mua hộ (Mã: {0}). Hàng đang trên đường về kho."),
                ["STORED"] = ("Hàng mua hộ đã nhập kho",
                    "Hàng của yêu cầu mua hộ (Mã: {0}) đã nhập kho. Bạn có thể xem phiếu nhập kho trong chi tiết đơn."),
                ["NEED_MORE_INFO"] = ("Yêu cầu mua hộ cần bổ sung thông tin",
                    "Yêu cầu mua hộ (Mã: {0}) cần bạn bổ sung thông tin."),
                ["REJECTED"] = ("Yêu cầu mua hộ bị từ chối",
                    "Yêu cầu mua hộ (Mã: {0}) đã bị từ chối."),
                ["QUOTED"] = ("Yêu cầu mua hộ đã có báo giá",
                    "Yêu cầu mua hộ (Mã: {0}) đã được tạo báo giá. Vui lòng kiểm tra và thực hiện thanh toán."),
            };

        /// <summary>
        /// Bắn thông báo cho khách khi đơn sang mốc đáng quan tâm, theo đúng cách luồng ký gửi đang làm
        /// (xem <c>OrderService.Workflow.cs</c>). Chỉ ghi vào UnitOfWork; hàm gọi chịu trách nhiệm lưu.
        /// <para>
        /// Không bắn khi trạng thái không đổi, để bấm lại nút không sinh thông báo trùng.
        /// Lỗi tra khách không được làm hỏng việc cập nhật đơn — thiếu thông báo còn hơn kẹt nghiệp vụ.
        /// </para>
        /// </summary>
        private async Task NotifyCustomerAsync(
            PurchaseRequest purchaseRequest, string targetStatus, string? previousStatus, string? reason = null)
        {
            if (string.Equals(targetStatus, previousStatus, StringComparison.OrdinalIgnoreCase))
                return;

            if (!CustomerNotifications.TryGetValue(targetStatus, out var template))
                return;

            try
            {
                var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .FirstOrDefault(c => c.Id == purchaseRequest.CustomerId);
                if (customer == null)
                    return;

                var content = string.Format(template.Content, purchaseRequest.PurchaseCode ?? purchaseRequest.Id.ToString());
                if (!string.IsNullOrWhiteSpace(reason))
                    content += $" Lý do: {reason}";

                await _unitOfWork.Repository<Notification>().AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = customer.UserId,
                    Title = template.Title,
                    Content = content,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });

                // Bắn thông báo nội bộ cho các bộ phận liên quan
                var code = purchaseRequest.PurchaseCode ?? purchaseRequest.Id.ToString();
                if (string.Equals(targetStatus, "ARRIVED_ORIGIN_WAREHOUSE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(targetStatus, "WAITING_STORED", StringComparison.OrdinalIgnoreCase))
                {
                    await _notificationService.NotifyRolesAsync(
                        new[] { "WarehouseStaff", "OperationsManager", "Operations", "Warehouse" },
                        "Hàng mua hộ đã về kho",
                        $"Hàng của yêu cầu mua hộ {code} đã về kho nước ngoài, sẵn sàng kiểm kê nhập kho.");
                }
                else if (string.Equals(targetStatus, "STORED", StringComparison.OrdinalIgnoreCase))
                {
                    await _notificationService.NotifyRolesAsync(
                        new[] { "Sale", "Sales", "Admin" },
                        "Đơn mua hộ đã nhập kho",
                        $"Hàng của yêu cầu mua hộ {code} đã được kiểm kê và nhập kho thành công.");
                }
                else if (string.Equals(targetStatus, "REJECTED", StringComparison.OrdinalIgnoreCase))
                {
                    await _notificationService.NotifyRolesAsync(
                        new[] { "Sale", "Sales", "Admin" },
                        "Yêu cầu mua hộ bị từ chối",
                        $"Yêu cầu mua hộ {code} đã bị từ chối.{(string.IsNullOrWhiteSpace(reason) ? "" : $" Lý do: {reason}")}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CREATE PURCHASE NOTIFICATION ERROR ({purchaseRequest.Id}): {ex.Message}");
            }
        }

        public async Task<List<PurchaseRequestHistoryItemDto>> GetPurchaseRequestHistoryAsync(
            Guid requestId, Guid? userId, string userRole)
        {
            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");

            // Customer chỉ xem lịch sử đơn của chính mình
            bool isStaff = StaffRoleHelper.IsInternalStaff(userRole);
            if (!isStaff)
            {
                var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                    .FirstOrDefault(c => c.UserId == userId);
                if (customer == null || purchaseRequest.CustomerId != customer.Id)
                    throw new UnauthorizedAccessException("Bạn không có quyền xem lịch sử của đơn mua hộ này.");
            }

            var histories = (await _unitOfWork.Repository<PurchaseRequestHistory>().GetAllAsync())
                .Where(h => h.PurchaseRequestId == requestId)
                .OrderByDescending(h => h.CreatedAt)
                .ToList();

            return histories.Select(h => new PurchaseRequestHistoryItemDto
            {
                HistoryId = h.Id,
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                ToStatusDisplayName = GetStatusDisplayName(h.ToStatus),
                Note = h.Note,
                ProofImages = SplitList(h.ProofImageUrls),
                ChangedBy = h.ChangedBy,
                ChangedByName = h.ChangedByName,
                CreatedAt = h.CreatedAt
            }).ToList();
        }

        /// <summary>
        /// Kho nhận hàng phải nằm ở nước xuất phát của tuyến. Đơn tuyến Trung Quốc mà chọn kho Hàn Quốc
        /// là sai nghiệp vụ — không chặn ở server thì hàng bị gán nhầm kho và lô gom sai chặng.
        /// FE đã lọc sẵn, nhưng lọc ở client không phải là kiểm soát.
        /// </summary>
        private static void EnsureWarehouseMatchesRoute(Warehouse warehouse, string? route)
        {
            var canonicalRoute = OrderService.NormalizeRoute(route);
            if (string.IsNullOrEmpty(canonicalRoute)) return;

            // Tuyến có dạng "CN-VN", nước xuất phát là phần đứng trước dấu gạch.
            var originCode = canonicalRoute.Split('-').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(originCode)) return;

            var warehouseRegion = warehouse.Region?.ToString();
            if (string.IsNullOrWhiteSpace(warehouseRegion)) return;

            if (!string.Equals(warehouseRegion, originCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Kho '{warehouse.WarehouseName}' thuộc khu vực {warehouseRegion}, không khớp tuyến {canonicalRoute}. " +
                    $"Vui lòng chọn kho ở {originCode}.");
            }
        }

        private static List<string> SplitList(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : value.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

        public async Task<UpdatePurchaseRequestStatusResponseDto> UpdatePurchaseRequestStatusAsync(
            Guid requestId, Guid userId, string userRole, UpdatePurchaseRequestStatusRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu cập nhật không được để trống.");

            // 1. Phân quyền: chỉ Sale/Admin được xem xét yêu cầu (kho, giao vận, OM chỉ xem).
            if (!StaffRoleHelper.CanQuotePurchase(userRole))
            {
                throw new UnauthorizedAccessException("Chỉ Sale hoặc Admin được cập nhật trạng thái xem xét yêu cầu mua hộ.");
            }

            // 2. Tìm yêu cầu (404 nếu không có)
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            // 3. Không cho cập nhật nếu đã sang bước báo giá hoặc đã hủy
            var currentStatus = purchaseRequest.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            // Chỉ đổi tay ở giai đoạn xem xét; đã báo giá / trả tiền thì đi đường huỷ – hoàn tiền.
            if (currentStatus is not ("PENDING_REVIEW" or "IN_REVIEW" or "NEED_MORE_INFO"))
            {
                throw new ArgumentException($"Không thể cập nhật trạng thái do yêu cầu đã ở trạng thái {StatusNames.Quote(purchaseRequest.Status)}.");
            }

            // 4. Validate trạng thái mới
            var targetStatus = request.Status.Trim().ToUpperInvariant();
            var validStatuses = new[] { "PENDING_REVIEW", "IN_REVIEW", "NEED_MORE_INFO", "REJECTED" };
            if (!validStatuses.Contains(targetStatus))
            {
                throw new ArgumentException($"Trạng thái không hợp lệ. Chỉ chuyển được sang {StatusNames.QuoteAny("PENDING_REVIEW", "IN_REVIEW", "NEED_MORE_INFO", "REJECTED")}.");
            }

            // 5. Lý do bắt buộc khi NEED_MORE_INFO hoặc REJECTED
            bool needsReason = targetStatus == "NEED_MORE_INFO" || targetStatus == "REJECTED";
            if (needsReason && string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new ArgumentException("Vui lòng cung cấp lý do khi yêu cầu bổ sung thông tin hoặc từ chối.");
            }

            // 6. Cập nhật
            purchaseRequest.Status = targetStatus;
            purchaseRequest.Reason = needsReason ? request.Reason : null;
            requestRepo.Update(purchaseRequest);
            await NotifyCustomerAsync(purchaseRequest, targetStatus, currentStatus, purchaseRequest.Reason);
            await _unitOfWork.SaveChangesAsync();

            return new UpdatePurchaseRequestStatusResponseDto
            {
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                Status = purchaseRequest.Status,
                Reason = purchaseRequest.Reason
            };
        }

        public async Task<PurchaseQuotationResponseDto> CreatePurchaseQuotationAsync(
            Guid requestId, Guid userId, string userRole, CreatePurchaseQuotationDto request)
        {
            if (request == null)
                throw new ArgumentException("Dữ liệu báo giá không được để trống.");

            // 1. Phân quyền: chỉ Sale/Admin được lập báo giá (kho, giao vận, OM chỉ xem).
            if (!StaffRoleHelper.CanQuotePurchase(userRole))
            {
                throw new UnauthorizedAccessException("Chỉ Sale hoặc Admin được lập báo giá yêu cầu mua hộ.");
            }

            // 2. Tìm yêu cầu (404 nếu không có)
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            // 3. Chỉ tạo báo giá cho yêu cầu đang ở trạng thái hợp lệ
            var currentStatus = purchaseRequest.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var quotableStatuses = new[] { "PENDING_REVIEW", "IN_REVIEW", "NEED_MORE_INFO" };
            if (!quotableStatuses.Contains(currentStatus))
            {
                throw new ArgumentException($"Không thể tạo báo giá do yêu cầu đang ở trạng thái {StatusNames.Quote(purchaseRequest.Status)}.");
            }

            // 4. Không cho tạo trùng báo giá đang hiệu lực
            var quotationRepo = _unitOfWork.Repository<PurchaseQuotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            bool hasActiveQuotation = allQuotations.Any(q =>
                q.PurchaseRequestId == requestId
                && string.Equals(q.Status, "PENDING_CUSTOMER_CONFIRMATION", StringComparison.OrdinalIgnoreCase));
            if (hasActiveQuotation)
            {
                throw new ArgumentException("Yêu cầu này đã có báo giá đang chờ khách xác nhận.");
            }

            // 5. Validate danh sách sản phẩm báo giá (phải thuộc yêu cầu này)
            if (request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("Báo giá phải có ít nhất một sản phẩm.");
            }

            var allItems = await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync();
            var requestItems = allItems.Where(i => i.PurchaseRequestId == requestId).ToList();
            var requestItemMap = requestItems.ToDictionary(i => i.Id);

            foreach (var line in request.Items)
            {
                if (!requestItemMap.ContainsKey(line.PurchaseRequestItemId))
                {
                    throw new ArgumentException($"Sản phẩm {line.PurchaseRequestItemId} không thuộc yêu cầu mua hộ này.");
                }
                if (line.UnitPrice < 0)
                {
                    throw new ArgumentException("Đơn giá sản phẩm không được âm.");
                }
            }

            // 6. Tạo báo giá + chi tiết chi phí theo từng sản phẩm
            // Tỷ giá lấy MỘT LẦN cho cả báo giá rồi đóng băng vào bản ghi: nếu tra lại lúc thanh toán
            // thì Admin sửa bảng tỷ giá là báo giá cũ tự đổi số tiền, gây tranh chấp với khách.
            var quotationCurrency = string.IsNullOrWhiteSpace(request.Currency)
                ? "VND"
                : request.Currency.Trim().ToUpperInvariant();
            var appliedRate = await _exchangeRateService.GetRateToVndAsync(quotationCurrency);
            var isForeignCurrency = quotationCurrency != "VND";

            var quotation = new PurchaseQuotation
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                PurchaseFee = request.PurchaseFee,
                ShippingFee = request.ShippingFee,
                Note = request.Note,
                Status = "PENDING_CUSTOMER_CONFIRMATION",
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                Currency = quotationCurrency,
                ExchangeRate = appliedRate,
                ExchangeRateAt = DateTime.UtcNow
            };

            var categories = await _unitOfWork.Repository<ProductType>().GetAllAsync();
            var quotationItemRepo = _unitOfWork.Repository<PurchaseQuotationItem>();
            var itemDtos = new List<PurchaseQuotationItemResponseDto>();
            decimal productsSubtotal = 0m;
            decimal importTax = 0m;
            var itemTaxFees = new List<QuotationFee>();

            foreach (var line in request.Items)
            {
                var reqItem = requestItemMap[line.PurchaseRequestItemId];

                // Sale nhập giá theo ngoại tệ; mọi tính toán phía sau (thuế, phí, tổng bill) đều theo VND.
                var unitPriceVnd = isForeignCurrency
                    ? Math.Round(line.UnitPrice * appliedRate, 0, MidpointRounding.AwayFromZero)
                    : line.UnitPrice;

                var lineTotal = unitPriceVnd * reqItem.Quantity;
                productsSubtotal += lineTotal;

                if (string.IsNullOrWhiteSpace(reqItem.ProductType))
                {
                    throw new ArgumentException($"Sản phẩm '{reqItem.ProductName}' phải có thông tin phân loại sản phẩm (ProductType).");
                }

                ProductType category = null;
                if (Guid.TryParse(reqItem.ProductType.Trim(), out var guidId))
                {
                    category = categories.FirstOrDefault(c => c.Id == guidId);
                }
                else
                {
                    category = categories.FirstOrDefault(c => 
                        string.Equals(c.Name?.Trim(), reqItem.ProductType?.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (category == null || !category.ImportTaxRate.HasValue)
                {
                    throw new ArgumentException($"Loại hàng '{reqItem.ProductType}' chưa được cấu hình thuế nhập khẩu hợp lệ.");
                }

                var itemImportTax = Math.Round(lineTotal * category.ImportTaxRate.Value, 0);
                importTax += itemImportTax;

                if (itemImportTax > 0)
                {
                    itemTaxFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        FeeName = $"Thuế nhập khẩu - {category.Name}",
                        FeeType = "TAX",
                        CalculationType = "PERCENTAGE",
                        Value = category.ImportTaxRate.Value * 100m,
                        Amount = itemImportTax,
                        Note = $"Thuế nhập khẩu {category.ImportTaxRate.Value * 100m}% cho mặt hàng {reqItem.ProductName ?? "không tên"}",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                var quotationItem = new PurchaseQuotationItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseQuotationId = quotation.Id,
                    PurchaseRequestItemId = reqItem.Id,
                    UnitPrice = unitPriceVnd,
                    UnitPriceOriginal = isForeignCurrency ? line.UnitPrice : null,
                    Quantity = reqItem.Quantity,
                    LineTotal = lineTotal
                };
                await quotationItemRepo.AddAsync(quotationItem);

                itemDtos.Add(new PurchaseQuotationItemResponseDto
                {
                    QuotationItemId = quotationItem.Id,
                    PurchaseRequestItemId = reqItem.Id,
                    ProductName = reqItem.ProductName,
                    UnitPrice = unitPriceVnd,
                    UnitPriceOriginal = isForeignCurrency ? line.UnitPrice : null,
                    Quantity = reqItem.Quantity,
                    LineTotal = lineTotal
                });
            }

            var quotationFees = new List<QuotationFee>();
            decimal totalAdditionalFees = 0m;

            // Ship nội địa (NCC → kho nguồn) chỉ tính MỘT lần: ô "Ship nội địa" Sale nhập là nguồn. Web cũ còn
            // gửi kèm rule DOMESTIC_FEE như phụ phí → trước đây bị cộng hai lần vào phần trả trước.
            // Ô trống (0) mà có phụ phí DOMESTIC_FEE thì lấy số đó làm ship nội địa (vẫn một dòng DOMESTIC_SHIPPING).
            decimal domesticFeeFromRule = 0m;

            if (request.AdditionalFees != null && request.AdditionalFees.Any())
            {
                foreach (var feeDto in request.AdditionalFees)
                {
                    if (feeDto.Amount < 0)
                    {
                        throw new ArgumentException("Số tiền phụ phí không được âm.");
                    }

                    PricingRule rule = null;
                    if (feeDto.PricingRuleId.HasValue)
                    {
                        rule = await _unitOfWork.Repository<PricingRule>().GetByIdAsync(feeDto.PricingRuleId.Value);
                        if (rule == null)
                        {
                            throw new ArgumentException($"Không tìm thấy quy định phí với ID: {feeDto.PricingRuleId.Value}");
                        }
                        if (!string.Equals(rule.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ArgumentException($"Quy định phí '{rule.RuleName}' không còn hoạt động.");
                        }
                    }

                    // Thuế NK (theo loại hàng, thu ở chặng VN) và VAT (rule VAT) backend tự tính ở dưới.
                    // Web nhân viên gửi kèm hai rule này như phụ phí thì bị cộng HAI lần vào bill và thuế NK
                    // bị thu ngay ở phần trả trước — bỏ qua.
                    if (PurchasePrepayRule.IsAutoComputedTax(rule, feeDto.FeeType))
                    {
                        continue;
                    }

                    if (PurchasePrepayRule.IsDomesticShippingFee(rule, feeDto.FeeType))
                    {
                        domesticFeeFromRule = Math.Max(domesticFeeFromRule, feeDto.Amount);
                        continue;
                    }

                    totalAdditionalFees += feeDto.Amount;

                    quotationFees.Add(new QuotationFee
                    {
                        Id = Guid.NewGuid(),
                        PricingRuleId = feeDto.PricingRuleId,
                        FeeName = feeDto.FeeName ?? rule?.RuleName ?? "Phụ phí",
                        FeeType = feeDto.FeeType ?? rule?.RuleType ?? "SURCHARGE",
                        CalculationType = rule?.CalculationType ?? "FIXED",
                        Value = rule?.Value ?? feeDto.Amount,
                        Amount = feeDto.Amount,
                        Note = feeDto.Note ?? rule?.Description ?? "Phụ phí mua hộ",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            if (request.PurchaseFee > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Phí mua hộ",
                    FeeType = "SERVICE_FEE",
                    CalculationType = "FIXED",
                    Value = request.PurchaseFee,
                    Amount = request.PurchaseFee,
                    Note = "Phí dịch vụ mua hộ — trả trước",
                    CreatedAt = DateTime.UtcNow
                });
            }

            var domesticFee = request.DomesticShippingFee > 0m ? request.DomesticShippingFee : domesticFeeFromRule;
            if (domesticFee > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Ship nội địa từ NCC tới kho",
                    FeeType = DomesticShippingFeeType,
                    CalculationType = "FIXED",
                    Value = domesticFee,
                    Amount = domesticFee,
                    Note = "Phí vận chuyển nội địa nước ngoài — trả trước",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Cước quốc tế chỉ là TẠM TÍNH theo cân ước lượng: số thật tính lại ở kho VN theo đúng
            // đơn giá /kg này (VnSettlementService đọc dòng MAIN_SERVICE PER_KG của báo giá kho).
            var (freightRate, pricingId) = await ResolveFreightRateAsync(purchaseRequest, request.FreightRatePerKg, request.ServicePricingId);
            var totalQuantity = SafeTotalQuantity(requestItems.Where(i => request.Items.Any(l => l.PurchaseRequestItemId == i.Id)).Select(i => i.Quantity));
            var estimatedWeight = request.EstimatedWeight is > 0m
                ? request.EstimatedWeight.Value
                : Math.Max(1.0m, totalQuantity * 0.2m);
            var freightEstimate = Math.Round(estimatedWeight * freightRate, 0);
            quotationFees.Add(new QuotationFee
            {
                Id = Guid.NewGuid(),
                FeeName = "Cước vận chuyển quốc tế (tạm tính)",
                FeeType = "MAIN_SERVICE",
                CalculationType = "PER_KG",
                Value = freightRate,
                Amount = freightEstimate,
                ChargeableWeight = estimatedWeight,
                Note = $"Tạm tính {estimatedWeight:0.###} kg × {freightRate:#,##0}đ — thu ở VN theo cân đo thật",
                CreatedAt = DateTime.UtcNow
            });

            // 7. Tính tổng tiền báo giá
            var vatRatePercent = await ResolveVatRatePercentAsync();

            // VAT phần phí dịch vụ thu trước; VAT cước thu ở VN cùng cước.
            var vatOnFees = Math.Round((request.PurchaseFee + domesticFee + totalAdditionalFees) * (vatRatePercent / 100m), 0);
            var vatOnFreight = Math.Round(freightEstimate * (vatRatePercent / 100m), 0);
            if (vatOnFees > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = $"VAT {vatRatePercent:G29}% phí dịch vụ",
                    FeeType = "TAX",
                    CalculationType = "PERCENTAGE",
                    Value = vatRatePercent,
                    Amount = vatOnFees,
                    Note = $"{vatRatePercent:G29}% phí mua hộ, ship nội địa và phụ phí — trả trước",
                    CreatedAt = DateTime.UtcNow
                });
            }
            if (vatOnFreight > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = $"VAT {vatRatePercent:G29}% cước quốc tế (tạm tính)",
                    FeeType = "TAX",
                    CalculationType = "PERCENTAGE",
                    Value = vatRatePercent,
                    Amount = vatOnFreight,
                    Note = $"{vatRatePercent:G29}% cước quốc tế — thu ở VN",
                    CreatedAt = DateTime.UtcNow
                });
            }

            quotationFees.AddRange(itemTaxFees);

            var prepayAmount = productsSubtotal + request.PurchaseFee + domesticFee + totalAdditionalFees + vatOnFees;
            var laterAmount = freightEstimate + vatOnFreight + importTax;

            quotation.ProductsSubtotal = productsSubtotal;
            quotation.ShippingFee = freightEstimate;
            quotation.DomesticShippingFee = domesticFee;
            quotation.FreightRatePerKg = freightRate;
            quotation.EstimatedWeight = estimatedWeight;
            quotation.ServicePricingId = pricingId;
            quotation.VatRate = vatRatePercent;
            quotation.Vat = vatOnFees + vatOnFreight;
            quotation.ImportTax = importTax;
            quotation.PrepayAmount = prepayAmount;
            quotation.EstimatedLaterAmount = laterAmount;
            quotation.TotalAmount = prepayAmount + laterAmount;
            await quotationRepo.AddAsync(quotation);

            // Save Quotation Fees to DB
            foreach (var qf in quotationFees)
            {
                qf.PurchaseQuotationId = quotation.Id;
                await _unitOfWork.Repository<QuotationFee>().AddAsync(qf);
            }

            // 8. Cập nhật trạng thái yêu cầu mua hộ sang QUOTED
            var previousStatus = purchaseRequest.Status;
            purchaseRequest.Status = "QUOTED";
            requestRepo.Update(purchaseRequest);
            await NotifyCustomerAsync(purchaseRequest, "QUOTED", previousStatus);

            await _unitOfWork.SaveChangesAsync();

            var response = MapQuotation(quotation, purchaseRequest, quotationFees);
            response.Items = itemDtos;
            return response;
        }

        /// <summary>Loại phí ship nội địa nước ngoài (NCC → kho nguồn) trên báo giá mua hộ.</summary>
        private const string DomesticShippingFeeType = "DOMESTIC_SHIPPING";

        /// <summary>Phí nằm trong các cột riêng của báo giá, không liệt kê lại ở "phụ phí".</summary>
        private static readonly HashSet<string> HeadlineFeeTypes = new(StringComparer.OrdinalIgnoreCase)
        { "SERVICE_FEE", "MAIN_SERVICE", DomesticShippingFeeType };

        private static PurchaseQuotationResponseDto MapQuotation(
            PurchaseQuotation quotation, PurchaseRequest purchaseRequest, IEnumerable<QuotationFee> fees)
        {
            var dto = new PurchaseQuotationResponseDto
            {
                QuotationId = quotation.Id,
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                ProductsSubtotal = quotation.ProductsSubtotal,
                PurchaseFee = quotation.PurchaseFee,
                ShippingFee = quotation.ShippingFee,
                DomesticShippingFee = quotation.DomesticShippingFee,
                FreightRatePerKg = quotation.FreightRatePerKg,
                EstimatedWeight = quotation.EstimatedWeight,
                VatRate = quotation.VatRate,
                Vat = quotation.Vat ?? 0m,
                ImportTax = quotation.ImportTax ?? 0m,
                TotalAmount = quotation.TotalAmount,
                Currency = quotation.Currency,
                ExchangeRate = quotation.ExchangeRate,
                ExchangeRateAt = quotation.ExchangeRateAt,
                Note = quotation.Note,
                Status = quotation.Status,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                AdditionalFees = fees
                    .Where(f => f.PricingRuleId.HasValue || !HeadlineFeeTypes.Contains(f.FeeType ?? string.Empty))
                    .Select(f => new PurchaseQuotationAdditionalFeeResponseDto
                    {
                        Id = f.Id,
                        PricingRuleId = f.PricingRuleId,
                        FeeName = f.FeeName,
                        FeeType = f.FeeType,
                        CalculationType = f.CalculationType,
                        Value = f.Value,
                        Amount = f.Amount,
                        Note = f.Note,
                        CreatedAt = f.CreatedAt
                    }).ToList()
            };
            PurchasePrepayRule.Fill(dto, quotation);
            return dto;
        }

        /// <summary>% VAT hiện hành từ PricingRule (mặc định 8%).</summary>
        private async Task<decimal> ResolveVatRatePercentAsync()
        {
            var activeRules = (await _unitOfWork.Repository<PricingRule>().GetAllAsync())
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var vatRule = activeRules.FirstOrDefault(r =>
                string.Equals(r.RuleCode, "VAT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RuleType, "VAT", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(r.RuleType, "TAX", StringComparison.OrdinalIgnoreCase) && (r.RuleName ?? "").Contains("VAT", StringComparison.OrdinalIgnoreCase)));

            if (vatRule != null && vatRule.Value > 0)
                return vatRule.Value <= 1.0m ? vatRule.Value * 100m : vatRule.Value;
            return 8m;
        }

        /// <summary>
        /// Đơn giá cước quốc tế /kg cho báo giá: Sale nhập tay, hoặc chọn bảng giá, hoặc lấy bảng giá
        /// khớp tuyến + loại dịch vụ của yêu cầu.
        /// </summary>
        private async Task<(decimal Rate, Guid? PricingId)> ResolveFreightRateAsync(
            PurchaseRequest purchaseRequest, decimal? manualRate, Guid? servicePricingId)
        {
            var allPricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync()).Where(p => !p.IsDeleted).ToList();

            if (servicePricingId.HasValue)
            {
                var chosen = allPricings.FirstOrDefault(p => p.Id == servicePricingId.Value)
                    ?? throw new ArgumentException("Không tìm thấy bảng giá cước đã chọn.");
                return (manualRate is > 0m ? manualRate.Value : chosen.Price ?? 0m, chosen.Id);
            }

            var matched = MatchServicePricing(purchaseRequest, allPricings);
            if (manualRate is > 0m) return (manualRate.Value, matched?.Id);
            if (matched?.Price is not > 0m)
                throw new ArgumentException("Chưa có bảng giá cước cho tuyến / loại dịch vụ của yêu cầu này. Vui lòng nhập đơn giá cước /kg.");
            return (matched!.Price!.Value, matched.Id);
        }

        private static ServicePricing? MatchServicePricing(PurchaseRequest purchaseRequest, List<ServicePricing> allPricings)
        {
            var shippingOption = purchaseRequest.ShippingOption ?? "Standard";
            var routeStr = purchaseRequest.Route ?? string.Empty;

            return allPricings
                .Where(p => string.Equals(p.ServiceType, shippingOption, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault(p =>
                    string.IsNullOrWhiteSpace(routeStr) ||
                    string.Equals(OrderService.NormalizeRoute($"{p.OriginCountry}-{p.DestinationCountry}"), OrderService.NormalizeRoute(routeStr), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(OrderService.NormalizeRoute(p.OriginCountry), OrderService.NormalizeRoute(routeStr), StringComparison.OrdinalIgnoreCase)
                ) ?? allPricings.FirstOrDefault(p => string.Equals(p.ServiceType, shippingOption, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<PurchaseQuotationResponseDto> GetPurchaseQuotationByRequestIdAsync(Guid requestId)
        {
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            var quotationRepo = _unitOfWork.Repository<PurchaseQuotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var quotation = allQuotations.Where(q => q.PurchaseRequestId == requestId)
                .OrderByDescending(q => q.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();
            if (quotation == null)
            {
                throw new NotFoundException("Yêu cầu mua hộ chưa được tạo báo giá.");
            }

            var quotationItemRepo = _unitOfWork.Repository<PurchaseQuotationItem>();
            var allQuoItems = await quotationItemRepo.GetAllAsync();
            var quoItems = allQuoItems.Where(qi => qi.PurchaseQuotationId == quotation.Id).ToList();

            var reqItemRepo = _unitOfWork.Repository<PurchaseRequestItem>();
            var allReqItems = await reqItemRepo.GetAllAsync();
            var reqItems = allReqItems.Where(ri => ri.PurchaseRequestId == requestId).ToList();
            var itemMap = reqItems.ToDictionary(i => i.Id);

            var itemDtos = quoItems.Select(qi => new PurchaseQuotationItemResponseDto
            {
                QuotationItemId = qi.Id,
                PurchaseRequestItemId = qi.PurchaseRequestItemId,
                ProductName = itemMap.ContainsKey(qi.PurchaseRequestItemId) ? itemMap[qi.PurchaseRequestItemId].ProductName : string.Empty,
                UnitPrice = qi.UnitPrice,
                UnitPriceOriginal = qi.UnitPriceOriginal,
                Quantity = qi.Quantity,
                LineTotal = qi.LineTotal
            }).ToList();

            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            var allFees = await feeRepo.GetAllAsync();
            var fees = allFees.Where(f => f.PurchaseQuotationId == quotation.Id).ToList();

            var response = MapQuotation(quotation, purchaseRequest, fees);
            response.Items = itemDtos;
            return response;
        }

        public async Task<CalculatePurchaseQuotationFeesResponseDto> CalculatePurchaseQuotationFeesAsync(
            Guid requestId, CalculatePurchaseQuotationFeesRequestDto request)
        {
            if (request == null || request.Items == null || !request.Items.Any())
            {
                throw new ArgumentException("Danh sách sản phẩm không được để trống.");
            }

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            // 1. Tính tổng tiền hàng
            decimal productsSubtotal = request.Items.Sum(i => (i.UnitPrice < 0 ? 0 : i.UnitPrice) * (i.Quantity < 0 ? 0 : i.Quantity));

            // 2. Tra cứu quy tắc Phí Mua Hộ từ PricingRule
            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var activeRules = (await pricingRuleRepo.GetAllAsync())
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selectedRuleIds = SplitPricingRuleIds(purchaseRequest.PricingRuleIds);
            PricingRule? purchaseFeeRule = null;
            if (selectedRuleIds.Any())
            {
                purchaseFeeRule = activeRules.FirstOrDefault(r => selectedRuleIds.Contains(r.Id) &&
                    (string.Equals(r.RuleType, "PURCHASE_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.RuleCode, "PURCHASE_FEE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.RuleCode, "PURCHASE_FEE_PERCENT", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.RuleCode, "PURCHASE_FEE_FIXED", StringComparison.OrdinalIgnoreCase)));
            }

            if (purchaseFeeRule == null)
            {
                purchaseFeeRule = activeRules
                    .Where(r => string.Equals(r.RuleType, "PURCHASE_FEE", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(r.RuleCode, "PURCHASE_FEE", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(r.RuleCode, "PURCHASE_FEE_PERCENT", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(r.RuleCode, "PURCHASE_FEE_FIXED", StringComparison.OrdinalIgnoreCase) ||
                                (r.RuleName != null && r.RuleName.Contains("mua hộ", StringComparison.OrdinalIgnoreCase)) ||
                                (r.RuleName != null && r.RuleName.Contains("mua hang", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(r => string.Equals(r.RuleCode, "PURCHASE_FEE_PERCENT", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(r => string.Equals(r.CalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(r => r.RuleCode)
                    .FirstOrDefault();
            }

            decimal calculatedPurchaseFee = 0m;
            decimal feePercent = 5.0m;
            bool isFixedFee = purchaseFeeRule != null && string.Equals(purchaseFeeRule.CalculationType, "FIXED", StringComparison.OrdinalIgnoreCase);

            if (isFixedFee)
            {
                calculatedPurchaseFee = purchaseFeeRule!.Value;
                feePercent = productsSubtotal > 0 ? Math.Round(calculatedPurchaseFee / productsSubtotal * 100m, 2) : 0m;
            }
            else
            {
                feePercent = purchaseFeeRule != null && purchaseFeeRule.Value > 0 ? purchaseFeeRule.Value : 5.0m;
                calculatedPurchaseFee = productsSubtotal * (feePercent / 100m);
            }

            if (purchaseFeeRule?.MinAmount.HasValue == true && calculatedPurchaseFee < purchaseFeeRule.MinAmount.Value)
            {
                calculatedPurchaseFee = purchaseFeeRule.MinAmount.Value;
            }

            calculatedPurchaseFee = Math.Round(calculatedPurchaseFee, 0);

            // 3. Cước quốc tế tạm tính theo bảng giá khớp tuyến (0,2 kg / món, tối thiểu 1 kg)
            int totalQuantity = SafeTotalQuantity(request.Items.Select(i => i.Quantity));
            decimal estimatedWeightKg = Math.Max(1.0m, totalQuantity * 0.2m);

            var allPricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync()).Where(p => !p.IsDeleted).ToList();
            var matchedPricing = MatchServicePricing(purchaseRequest, allPricings);
            decimal freightUnitPrice = matchedPricing?.Price ?? 0m;
            decimal estimatedShippingFee = Math.Round(estimatedWeightKg * freightUnitPrice, 0);

            // 4. Tra cứu Phí Vận Chuyển Nội Địa (DOMESTIC_FEE - Mặc định 5,000đ)
            var domesticFeeRule = activeRules.FirstOrDefault(r =>
                string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase));

            decimal domesticFee = domesticFeeRule != null && domesticFeeRule.Value > 0 ? domesticFeeRule.Value : 5000m;

            var vatRate = await ResolveVatRatePercentAsync();
            decimal prepay = productsSubtotal + calculatedPurchaseFee + domesticFee
                + Math.Round((calculatedPurchaseFee + domesticFee) * vatRate / 100m, 0);
            decimal totalEstimatedCost = prepay + estimatedShippingFee + Math.Round(estimatedShippingFee * vatRate / 100m, 0);
            string feeDescription = isFixedFee ? $"{calculatedPurchaseFee:N0}đ (cố định)" : $"{feePercent:G29}%";

            return new CalculatePurchaseQuotationFeesResponseDto
            {
                PurchaseRequestId = requestId,
                ProductsSubtotal = Math.Round(productsSubtotal, 0),
                CalculatedPurchaseFee = calculatedPurchaseFee,
                AppliedPurchaseFeePercent = feePercent,
                EstimatedShippingFee = estimatedShippingFee,
                DomesticShippingFee = domesticFee,
                FreightRatePerKg = freightUnitPrice,
                EstimatedWeight = estimatedWeightKg,
                ServicePricingId = matchedPricing?.Id,
                VatRate = vatRate,
                EstimatedPrepayAmount = Math.Round(prepay, 0),
                TotalEstimatedCost = Math.Round(totalEstimatedCost, 0),
                Note = $"Phí mua hộ: {feeDescription}. Ship nội địa: {domesticFee:N0}đ (trả trước). Cước quốc tế tạm tính: {estimatedShippingFee:N0}đ ({estimatedWeightKg}kg × {freightUnitPrice:N0}đ) — thu ở VN theo cân đo thật."
            };
        }
    }
}


