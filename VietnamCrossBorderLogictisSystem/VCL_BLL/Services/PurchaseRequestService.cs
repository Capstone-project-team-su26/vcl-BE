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
        private static readonly string[] StaffRoles = { "ADMIN", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS" };

        // Ký tự phân cách các danh sách lưu gộp trong 1 cột DB (dùng chung quy ước với ký gửi)
        private const char ListSeparator = '|';

        private const int MaxProofImages = 3;

        /// <summary>
        /// Các trạng thái popup "Xác nhận mua hộ" được phép đặt, theo đúng 4 nấc bấm của FE.
        /// <para>
        /// <c>PENDING_REVIEW</c> và <c>PAID</c> bình thường do hệ thống tự đặt (PAID đến từ webhook
        /// thanh toán). Mở cho bấm tay theo yêu cầu FE — không ảnh hưởng dòng tiền vì mọi điều kiện
        /// thanh toán đều đọc bảng PURCHASE_PAYMENTS chứ không đọc trạng thái đơn. Mọi lần bấm đều
        /// ghi vào PURCHASE_REQUEST_HISTORY nên truy được ai đặt tay.
        /// </para>
        /// Vẫn chặn <c>COMPLETED</c>: đó là trạng thái kết thúc, chỉ đặt khi đã thu đủ tiền.
        /// </summary>
        /// <summary>
        /// 5 bước tiến độ mua hộ. Cố tình KHÔNG có <c>COMPLETED</c>: trạng thái đó thuộc luồng
        /// thanh toán (webhook đặt khi khách trả đủ tiền). Chốt với FE 08/08/2026 — bước nhập kho
        /// dừng ở <c>STORED</c> để một cột không mang hai nghĩa "đã thu đủ tiền" và "đã nhập kho".
        /// </summary>
        private static readonly string[] PurchaseProgressStatuses =
        {
            "PENDING_REVIEW", "DEPOSIT_PAID", "PAID", "PURCHASED", "SELLER_SHIPPED",
            "ARRIVED_ORIGIN_WAREHOUSE", "WAITING_STORED", "STORED"
        };

        /// <summary>
        /// Bước duyệt kiểm kê nhập kho — chỉ Manager/Ops được đặt, Sale không.
        /// Khớp thiết kế FE: sang bước 4 thì nút bấm phía Sale ẩn đi.
        /// </summary>
        private static readonly string[] WarehouseApprovalStatuses = { "STORED" };

        private static readonly string[] WarehouseApprovalRoles =
            { "ADMIN", "OPERATIONSMANAGER", "OPERATIONS", "MANAGER" };

        /// <summary>Nhãn tiếng Việt của trạng thái yêu cầu mua hộ, dùng chung cho mọi response.</summary>
        private static readonly Dictionary<string, string> StatusDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NEW"] = "Tạo đơn hàng",
            ["PENDING_REVIEW"] = "Chờ xác nhận",
            ["IN_REVIEW"] = "Đang xem xét",
            ["NEED_MORE_INFO"] = "Cần bổ sung thông tin",
            ["REJECTED"] = "Đã từ chối",
            ["QUOTED"] = "Đã báo giá",
            ["WAITING_PAYMENT"] = "Chờ thanh toán",
            ["PAID"] = "Đã thanh toán",
            ["DEPOSIT_PAID"] = "Đã đặt cọc",
            ["PURCHASED"] = "Xác nhận mua hàng",
            ["SELLER_SHIPPED"] = "NCC đã phát hàng",
            ["ARRIVED_ORIGIN_WAREHOUSE"] = "Đã về kho nước ngoài",
            ["WAITING_STORED"] = "Chờ nhập kho",
            ["STORED"] = "Đã nhập kho",
            ["WAITING_FINAL_PAYMENT"] = "Chờ thanh toán cuối",
            ["COMPLETED"] = "Hoàn tất nghiệp vụ",
            ["CANCELLED"] = "Đã hủy"
        };

        private static string GetStatusDisplayName(string? status) =>
            !string.IsNullOrWhiteSpace(status) && StatusDisplayNames.TryGetValue(status.Trim(), out var label)
                ? label
                : status ?? string.Empty;

        private readonly IExchangeRateService _exchangeRateService;
        private readonly IConsignmentReceiptService _receiptService;

        public PurchaseRequestService(
            IUnitOfWork unitOfWork,
            IOrderService orderService,
            IExchangeRateService exchangeRateService,
            IConsignmentReceiptService receiptService)
        {
            _unitOfWork = unitOfWork;
            _orderService = orderService;
            _exchangeRateService = exchangeRateService;
            _receiptService = receiptService;
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

            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ProductLink))
                {
                    throw new ArgumentException("Link sản phẩm không được để trống.");
                }
                if (string.IsNullOrWhiteSpace(item.ProductType))
                {
                    throw new ArgumentException("Vui lòng chọn loại hàng cho từng sản phẩm.");
                }
                if (item.Quantity <= 0)
                {
                    throw new ArgumentException("Số lượng sản phẩm phải lớn hơn 0.");
                }
            }

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
                // để biết đơn do nhân viên lên hộ.
                GeneralNote = createdByStaff
                    ? AppendStaffMark(BuildGeneralNote(request))
                    : BuildGeneralNote(request),
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

            await _unitOfWork.Repository<PurchaseRequest>().AddAsync(purchaseRequest);

            // 8. Tạo các sản phẩm liên kết
            var itemRepo = _unitOfWork.Repository<PurchaseRequestItem>();
            var resultItems = new List<PurchaseRequestItemResponseDto>();

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
                    ProductName = requestItem.ProductName,
                    Quantity = requestItem.Quantity,
                    Attributes = requestItem.Attributes,
                    Note = requestItem.Note,
                    ImageUrls = returnedImageUrls
                });
            }

            await _unitOfWork.SaveChangesAsync();

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
                TotalQuantity = request.Items.Sum(i => i.Quantity),
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
            Guid? userId, string userRole, int pageNumber, int pageSize, string? status, string? searchKeyword)
        {
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var itemRepo = _unitOfWork.Repository<PurchaseRequestItem>();
            var customerRepo = _unitOfWork.Repository<Customer>();

            var allRequests = await requestRepo.GetAllAsync();
            var allItems = await itemRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();

            var query = allRequests.AsQueryable();

            bool isStaff = !string.IsNullOrWhiteSpace(userRole)
                && StaffRoles.Contains(userRole.Trim().ToUpperInvariant());

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
            var allQuotations = await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync();
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

                return new PurchaseRequestListItemDto
                {
                    PurchaseRequestId = r.Id,
                    PurchaseCode = r.PurchaseCode ?? string.Empty,
                    CustomerId = r.CustomerId,
                    Route = OrderService.MapRouteToDisplayName(r.Route),
                    ShippingOption = r.ShippingOption,
                    ReceiverName = r.ReceiverName ?? string.Empty,
                    ItemCount = products.Count,
                    TotalQuantity = products.Sum(p => p.Quantity),
                    Status = r.Status ?? string.Empty,
                    GeneralNote = r.GeneralNote,
                    CreatedAt = r.CreatedAt ?? DateTime.MinValue,
                    QuotationCreatedAt = quotationCreatedAt,
                    StatusUpdatedAt = quotationCreatedAt ?? r.CreatedAt,
                    StatusDisplayName = GetStatusDisplayName(r.Status),
                    WarehouseId = r.ExpectedWarehouseId,
                    WarehouseName = r.ExpectedWarehouseId.HasValue
                        && warehouseNames.TryGetValue(r.ExpectedWarehouseId.Value, out var whName)
                        ? whName
                        : null,
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

            bool isStaff = !string.IsNullOrWhiteSpace(userRole)
                && StaffRoles.Contains(userRole.Trim().ToUpperInvariant());

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

            var itemDtos = relatedItems.Select(i => {
                var urls = SplitImageUrls(i.ImageUrl);
                return new PurchaseRequestItemResponseDto
                {
                    ItemId = i.Id,
                    ProductLink = i.ProductLink ?? string.Empty,
                    SourceWebsite = i.SourceWebsite,
                    ProductType = i.ProductType,
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
                WarehouseId = request.ExpectedWarehouseId,
                WarehouseName = expectedWarehouse?.WarehouseName,
                ProofImages = SplitList(request.ProofImageUrls),
                TotalQuantity = relatedItems.Sum(i => i.Quantity),
                Items = itemDtos,
                Quotation = quotationDto
            };
        }

        public async Task<ConfirmPurchaseResponseDto> ConfirmPurchaseAsync(
            Guid requestId, Guid userId, string userRole, ConfirmPurchaseRequestDto request)
        {
            if (request == null)
                throw new ArgumentException("Nội dung xác nhận mua hộ không được để trống.");

            // 1. Phân quyền: chỉ Staff/Sales/Buying được cập nhật tiến độ mua hàng
            if (string.IsNullOrWhiteSpace(userRole) || !StaffRoles.Contains(userRole.Trim().ToUpperInvariant()))
                throw new UnauthorizedAccessException("Chỉ Staff hoặc Sales được xác nhận mua hộ.");

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");

            var currentStatus = purchaseRequest.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (currentStatus is "CANCELLED" or "REJECTED")
                throw new ArgumentException($"Không thể cập nhật tiến độ do đơn đang ở trạng thái {purchaseRequest.Status}.");

            // 2. Chỉ nhận nhóm trạng thái tiến độ mua hàng
            var targetStatus = request.Status.Trim().ToUpperInvariant();
            if (!PurchaseProgressStatuses.Contains(targetStatus))
            {
                throw new ArgumentException(
                    $"Trạng thái '{request.Status}' không thuộc nhóm tiến độ mua hàng. " +
                    $"Hợp lệ: {string.Join(", ", PurchaseProgressStatuses)}.");
            }

            // 2b. Bước duyệt nhập kho là việc của Manager/Ops, Sale không được tự đóng đơn
            var normalizedRole = userRole.Trim().ToUpperInvariant();
            if (WarehouseApprovalStatuses.Contains(targetStatus)
                && !WarehouseApprovalRoles.Contains(normalizedRole))
            {
                throw new UnauthorizedAccessException(
                    $"Chỉ Manager/Ops được duyệt kiểm kê nhập kho (trạng thái {targetStatus}).");
            }

            // 3. Kho dự kiến nhận hàng — FE gửi warehouseId, kèm destinationWarehouseId cùng giá trị
            var warehouseId = request.WarehouseId ?? request.DestinationWarehouseId;
            Warehouse? warehouse = null;
            if (warehouseId.HasValue)
            {
                warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId.Value)
                    ?? throw new NotFoundException($"Không tìm thấy kho với ID {warehouseId.Value}");

                EnsureWarehouseMatchesRoute(warehouse, purchaseRequest.Route);
            }

            // 3. Validate ảnh bằng chứng ở phía server, không tin số client gửi
            var proofImages = (request.ProofImages ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct()
                .ToList();

            if (proofImages.Count > MaxProofImages)
                throw new ArgumentException($"Chỉ được tải tối đa {MaxProofImages} ảnh bằng chứng.");

            if (proofImages.Any(u => u.Contains(ListSeparator)))
                throw new ArgumentException($"URL ảnh không được chứa ký tự '{ListSeparator}'.");

            // 4. Ghi nhật ký TRƯỚC khi đổi, để giữ được trạng thái cũ
            var actor = (await _unitOfWork.Repository<User>().GetAllAsync())
                .FirstOrDefault(u => u.Id == userId);

            await _unitOfWork.Repository<PurchaseRequestHistory>().AddAsync(new PurchaseRequestHistory
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                FromStatus = purchaseRequest.Status,
                ToStatus = targetStatus,
                Note = request.GeneralNote?.Trim(),
                ProofImageUrls = proofImages.Any() ? string.Join(ListSeparator, proofImages) : null,
                ChangedBy = userId,
                ChangedByName = actor?.FullName,
                CreatedAt = DateTime.UtcNow
            });

            // 5. Cập nhật đơn. Ảnh chỉ ghi đè khi lần này có gửi, để không xoá mất ảnh cũ.
            purchaseRequest.Status = targetStatus;
            if (proofImages.Any())
                purchaseRequest.ProofImageUrls = string.Join(ListSeparator, proofImages);
            if (!string.IsNullOrWhiteSpace(request.GeneralNote))
                purchaseRequest.GeneralNote = request.GeneralNote.Trim();
            if (warehouse != null)
                purchaseRequest.ExpectedWarehouseId = warehouse.Id;

            requestRepo.Update(purchaseRequest);
            await _unitOfWork.SaveChangesAsync();

            return await BuildConfirmResponseAsync(purchaseRequest);
        }

        /// <summary>
        /// Manager/Ops duyệt kiểm kê và nhập kho chính thức (bước 5).
        /// Tách khỏi <see cref="ConfirmPurchaseAsync"/> để phân quyền rõ ràng: Sale đẩy đơn tới
        /// WAITING_STORED rồi dừng, kho duyệt xong mới sang STORED.
        /// </summary>
        public async Task<ConfirmPurchaseResponseDto> ApproveStoreAsync(
            Guid requestId, Guid userId, string userRole, ApproveStoreRequestDto request)
        {
            request ??= new ApproveStoreRequestDto();

            var normalizedRole = (userRole ?? string.Empty).Trim().ToUpperInvariant();
            if (!WarehouseApprovalRoles.Contains(normalizedRole))
                throw new UnauthorizedAccessException("Chỉ Manager/Ops được duyệt kiểm kê nhập kho.");

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");

            var currentStatus = purchaseRequest.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (currentStatus is "CANCELLED" or "REJECTED")
                throw new ArgumentException($"Không thể duyệt nhập kho do đơn đang ở trạng thái {purchaseRequest.Status}.");

            if (currentStatus is "STORED" or "COMPLETED")
                throw new ArgumentException("Đơn đã được nhập kho trước đó.");

            // Phải qua bước kho báo hàng tới thì mới duyệt được
            if (currentStatus != "WAITING_STORED" && currentStatus != "ARRIVED_ORIGIN_WAREHOUSE")
            {
                throw new ArgumentException(
                    $"Chỉ duyệt nhập kho khi đơn đang ở WAITING_STORED hoặc ARRIVED_ORIGIN_WAREHOUSE (hiện tại: {purchaseRequest.Status}).");
            }

            var warehouseId = request.WarehouseId ?? purchaseRequest.ExpectedWarehouseId;
            Warehouse? warehouse = null;
            if (warehouseId.HasValue)
            {
                warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId.Value)
                    ?? throw new NotFoundException($"Không tìm thấy kho với ID {warehouseId.Value}");
            }

            var actor = (await _unitOfWork.Repository<User>().GetAllAsync())
                .FirstOrDefault(u => u.Id == userId);

            await _unitOfWork.Repository<PurchaseRequestHistory>().AddAsync(new PurchaseRequestHistory
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                FromStatus = purchaseRequest.Status,
                ToStatus = "STORED",
                Note = request.Note?.Trim(),
                ChangedBy = userId,
                ChangedByName = actor?.FullName,
                CreatedAt = DateTime.UtcNow
            });

            purchaseRequest.Status = "STORED";
            if (warehouse != null)
                purchaseRequest.ExpectedWarehouseId = warehouse.Id;
            if (!string.IsNullOrWhiteSpace(request.Note))
                purchaseRequest.GeneralNote = request.Note.Trim();

            requestRepo.Update(purchaseRequest);

            var (consignmentCode, orderId) = await MaterializeWarehouseOrderAsync(purchaseRequest, userId);

            await _unitOfWork.SaveChangesAsync();

            // Phiếu là chứng từ để kho đối chiếu khi nhận hàng, nên phát hành ngay khi OM duyệt.
            // Lỗi sinh phiếu không được làm hỏng việc duyệt — Sale/OM tải lại sau vẫn được.
            string? receiptPdfUrl = null;
            if (orderId.HasValue)
            {
                try
                {
                    receiptPdfUrl = await _receiptService.GenerateAndStoreReceiptAsync(orderId.Value, userId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"GENERATE PURCHASE RECEIPT ERROR ({purchaseRequest.Id}): {ex.Message}");
                }
            }

            var response = await BuildConfirmResponseAsync(purchaseRequest);
            response.ConsignmentCode = consignmentCode;
            response.ReceiptPdfUrl = receiptPdfUrl;
            return response;
        }

        /// <summary>
        /// Biến yêu cầu mua hộ thành một <see cref="Order"/> loại "PURCHASE" khi hàng được duyệt nhập kho.
        /// <para>
        /// Từ mốc này hàng nằm trong kho VCL, nên nó dùng chung nguyên vẹn luồng kho đã có của ký gửi:
        /// nhân viên kho check-in cân đo → sinh <c>Parcel</c> → put-away → <c>Inventory</c> → gom vào
        /// WRO → lô quốc tế về VN. Không phải viết lại bất cứ bước kho nào.
        /// </para>
        /// Trả về mã đơn để kho tra cứu; null nếu đã vật chất hoá trước đó.
        /// </summary>
        private async Task<(string? Code, Guid? OrderId)> MaterializeWarehouseOrderAsync(PurchaseRequest purchaseRequest, Guid actorUserId)
        {
            var orderRepo = _unitOfWork.Repository<Order>();

            var existing = (await orderRepo.GetAllAsync())
                .FirstOrDefault(o => o.PurchaseRequestId == purchaseRequest.Id);
            if (existing != null)
                return (existing.ConsignmentCode, existing.Id);

            var now = DateTime.UtcNow;
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = purchaseRequest.CustomerId,
                PurchaseRequestId = purchaseRequest.Id,
                ConsignmentCode = await GenerateWarehouseOrderCodeAsync(),
                ConsignmentType = purchaseRequest.ShippingOption,
                Route = purchaseRequest.Route,
                ReceiverName = purchaseRequest.ReceiverName,
                ReceiverPhone = purchaseRequest.ReceiverPhone,
                ReceiverAddress = purchaseRequest.ReceiverAddress,
                RequiresInspection = purchaseRequest.RequiresInspection,
                OrderType = "PURCHASE",
                // Kho chỉ check-in được khi đơn ở trạng thái này (xem ParcelService.CheckInParcelAsync)
                Status = "APPROVED",
                Note = $"Sinh tự động từ yêu cầu mua hộ {purchaseRequest.PurchaseCode}.",
                CreatedById = actorUserId,
                CreatedAt = now
            };
            await orderRepo.AddAsync(order);

            // Mỗi sản phẩm mua hộ thành một dòng hàng. Cân nặng/kích thước để trống —
            // nhân viên kho điền lúc check-in, đó mới là số đo thật để tính cước.
            var items = (await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync())
                .Where(i => i.PurchaseRequestId == purchaseRequest.Id)
                .ToList();

            // Giá trị khai báo của hàng mua hộ chính là giá sản phẩm trên báo giá đã chốt.
            var acceptedQuotation = (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                .Where(q => q.PurchaseRequestId == purchaseRequest.Id)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            var lineTotalByItem = acceptedQuotation == null
                ? new Dictionary<Guid, decimal>()
                : (await _unitOfWork.Repository<PurchaseQuotationItem>().GetAllAsync())
                    .Where(qi => qi.PurchaseQuotationId == acceptedQuotation.Id)
                    .GroupBy(qi => qi.PurchaseRequestItemId)
                    .ToDictionary(g => g.Key, g => g.Sum(qi => qi.LineTotal));

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            foreach (var item in items)
            {
                await orderItemRepo.AddAsync(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductName = item.ProductName,
                    ProductType = item.ProductType,
                    Quantity = item.Quantity,
                    DeclaredValue = lineTotalByItem.TryGetValue(item.Id, out var lineTotal) ? lineTotal : null,
                    // ReferenceUrl là nơi phiếu PDF đọc ẢNH sản phẩm, không phải link trang bán hàng —
                    // nhét ProductLink vào đây thì phiếu tải về HTML rồi vỡ lúc vẽ ảnh.
                    ReferenceUrl = item.ImageUrl,
                    Description = string.IsNullOrWhiteSpace(item.ProductLink)
                        ? item.Note
                        : $"{item.ProductLink}{(string.IsNullOrWhiteSpace(item.Note) ? "" : " — " + item.Note)}",
                    CreatedAt = now
                });
            }

            return (order.ConsignmentCode, order.Id);
        }

        private async Task<string> GenerateWarehouseOrderCodeAsync()
        {
            var existingCodes = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => !string.IsNullOrEmpty(o.ConsignmentCode))
                .Select(o => o.ConsignmentCode!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var random = new Random();
            string code;
            do
            {
                code = $"VCL-{DateTime.UtcNow:yyyyMMddHHmmss}-{random.Next(100000, 999999)}";
            } while (existingCodes.Contains(code));

            return code;
        }

        private async Task<ConfirmPurchaseResponseDto> BuildConfirmResponseAsync(PurchaseRequest purchaseRequest)
        {
            Warehouse? warehouse = purchaseRequest.ExpectedWarehouseId.HasValue
                ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(purchaseRequest.ExpectedWarehouseId.Value)
                : null;

            return new ConfirmPurchaseResponseDto
            {
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                Status = purchaseRequest.Status,
                StatusDisplayName = GetStatusDisplayName(purchaseRequest.Status),
                ProofImages = SplitList(purchaseRequest.ProofImageUrls),
                GeneralNote = purchaseRequest.GeneralNote,
                WarehouseId = purchaseRequest.ExpectedWarehouseId,
                WarehouseName = warehouse?.WarehouseName,
                UpdatedAt = DateTime.UtcNow
            };
        }

        public async Task<List<PurchaseRequestHistoryItemDto>> GetPurchaseRequestHistoryAsync(
            Guid requestId, Guid? userId, string userRole)
        {
            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");

            // Customer chỉ xem lịch sử đơn của chính mình
            bool isStaff = !string.IsNullOrWhiteSpace(userRole)
                && StaffRoles.Contains(userRole.Trim().ToUpperInvariant());
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

            // 1. Phân quyền: chỉ Staff/Sales được cập nhật trạng thái
            bool isStaff = !string.IsNullOrWhiteSpace(userRole)
                && StaffRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!isStaff)
            {
                throw new UnauthorizedAccessException("Chỉ Staff hoặc Sales được cập nhật trạng thái yêu cầu mua hộ.");
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
            if (currentStatus == "QUOTED" || currentStatus == "CANCELLED")
            {
                throw new ArgumentException($"Không thể cập nhật trạng thái do yêu cầu đã ở trạng thái {purchaseRequest.Status}.");
            }

            // 4. Validate trạng thái mới
            var targetStatus = request.Status.Trim().ToUpperInvariant();
            var validStatuses = new[] { "PENDING_REVIEW", "IN_REVIEW", "NEED_MORE_INFO", "REJECTED" };
            if (!validStatuses.Contains(targetStatus))
            {
                throw new ArgumentException("Trạng thái không hợp lệ. Hợp lệ: PENDING_REVIEW, IN_REVIEW, NEED_MORE_INFO, REJECTED.");
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

            // 1. Phân quyền: chỉ Staff/Sales được tạo báo giá
            bool isStaff = !string.IsNullOrWhiteSpace(userRole)
                && StaffRoles.Contains(userRole.Trim().ToUpperInvariant());
            if (!isStaff)
            {
                throw new UnauthorizedAccessException("Chỉ Staff hoặc Sales được tạo báo giá mua hộ.");
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
                throw new ArgumentException($"Không thể tạo báo giá do yêu cầu đang ở trạng thái {purchaseRequest.Status}.");
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
                    Note = "Phí dịch vụ mua hộ",
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (request.ShippingFee > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Phí vận chuyển",
                    FeeType = "MAIN_SERVICE",
                    CalculationType = "FIXED",
                    Value = request.ShippingFee,
                    Amount = request.ShippingFee,
                    Note = "Phí vận chuyển quốc tế / nội địa cho mua hộ",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 7. Tính tổng tiền báo giá
            var pricingRuleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await pricingRuleRepo.GetAllAsync();
            var activeRules = allRules.Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)).ToList();

            decimal vatRatePercent = 8m;
            var vatRule = activeRules.FirstOrDefault(r =>
                string.Equals(r.RuleCode, "VAT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RuleType, "VAT", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(r.RuleType, "TAX", StringComparison.OrdinalIgnoreCase) && (r.RuleName ?? "").Contains("VAT", StringComparison.OrdinalIgnoreCase)));

            if (vatRule != null && vatRule.Value > 0)
            {
                vatRatePercent = vatRule.Value <= 1.0m ? vatRule.Value * 100m : vatRule.Value;
            }

            decimal vatAmount = Math.Round((request.PurchaseFee + request.ShippingFee + totalAdditionalFees) * (vatRatePercent / 100m), 0);
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
                    Note = $"{vatRatePercent:G29}% phí dịch vụ mua hộ",
                    CreatedAt = DateTime.UtcNow
                });
            }

            quotationFees.AddRange(itemTaxFees);

            quotation.ProductsSubtotal = productsSubtotal;
            quotation.Vat = vatAmount;
            quotation.ImportTax = importTax;
            quotation.TotalAmount = productsSubtotal + request.PurchaseFee + request.ShippingFee + totalAdditionalFees + vatAmount + importTax;
            await quotationRepo.AddAsync(quotation);

            // Save Quotation Fees to DB
            foreach (var qf in quotationFees)
            {
                qf.PurchaseQuotationId = quotation.Id;
                await _unitOfWork.Repository<QuotationFee>().AddAsync(qf);
            }

            // 8. Cập nhật trạng thái yêu cầu mua hộ sang QUOTED
            purchaseRequest.Status = "QUOTED";
            requestRepo.Update(purchaseRequest);

            await _unitOfWork.SaveChangesAsync();

            return new PurchaseQuotationResponseDto
            {
                QuotationId = quotation.Id,
                PurchaseRequestId = requestId,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                ProductsSubtotal = quotation.ProductsSubtotal,
                PurchaseFee = quotation.PurchaseFee,
                ShippingFee = quotation.ShippingFee,
                Vat = vatAmount,
                ImportTax = importTax,
                TotalAmount = quotation.TotalAmount,
                Currency = quotation.Currency,
                ExchangeRate = quotation.ExchangeRate,
                ExchangeRateAt = quotation.ExchangeRateAt,
                Note = quotation.Note,
                Status = quotation.Status,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                Items = itemDtos,
                AdditionalFees = quotationFees.Where(f => f.PricingRuleId.HasValue || (f.FeeType != "SERVICE_FEE" && f.FeeType != "MAIN_SERVICE")).Select(f => new PurchaseQuotationAdditionalFeeResponseDto
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
            var quotation = allQuotations.FirstOrDefault(q => q.PurchaseRequestId == requestId);
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

            return new PurchaseQuotationResponseDto
            {
                QuotationId = quotation.Id,
                PurchaseRequestId = requestId,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                ProductsSubtotal = quotation.ProductsSubtotal,
                PurchaseFee = quotation.PurchaseFee,
                ShippingFee = quotation.ShippingFee,
                Vat = quotation.Vat ?? 0m,
                ImportTax = quotation.ImportTax ?? 0m,
                TotalAmount = quotation.TotalAmount,
                Currency = quotation.Currency,
                ExchangeRate = quotation.ExchangeRate,
                ExchangeRateAt = quotation.ExchangeRateAt,
                Note = quotation.Note,
                Status = quotation.Status,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                Items = itemDtos,
                AdditionalFees = feeDtos.Where(f => f.PricingRuleId.HasValue || (f.FeeType != "SERVICE_FEE" && f.FeeType != "MAIN_SERVICE")).ToList()
            };
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

            // 3. Tra cứu Bảng giá Vận chuyển (ServicePricing) để ước tính Phí Vận Chuyển
            int totalQuantity = request.Items.Sum(i => i.Quantity < 0 ? 0 : i.Quantity);
            decimal estimatedWeightKg = Math.Max(1.0m, totalQuantity * 0.2m); // Ước tính 0.2kg / món

            var servicePricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allPricings = (await servicePricingRepo.GetAllAsync()).Where(p => !p.IsDeleted).ToList();

            var shippingOption = purchaseRequest.ShippingOption ?? "Standard";
            var routeStr = purchaseRequest.Route ?? string.Empty;

            var matchedPricing = allPricings
                .Where(p => string.Equals(p.ServiceType, shippingOption, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault(p =>
                    string.IsNullOrWhiteSpace(routeStr) ||
                    string.Equals(OrderService.NormalizeRoute($"{p.OriginCountry}-{p.DestinationCountry}"), OrderService.NormalizeRoute(routeStr), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(OrderService.NormalizeRoute(p.OriginCountry), OrderService.NormalizeRoute(routeStr), StringComparison.OrdinalIgnoreCase)
                ) ?? allPricings.FirstOrDefault(p => string.Equals(p.ServiceType, shippingOption, StringComparison.OrdinalIgnoreCase));

            decimal freightUnitPrice = matchedPricing?.Price ?? 200000m; // Default 200k/kg nếu ko có cước
            decimal estimatedShippingFee = Math.Round(estimatedWeightKg * freightUnitPrice, 0);

            // 4. Tra cứu Phí Vận Chuyển Nội Địa (DOMESTIC_FEE - Mặc định 5,000đ)
            var domesticFeeRule = activeRules.FirstOrDefault(r =>
                string.Equals(r.RuleType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase));

            decimal domesticFee = domesticFeeRule != null && domesticFeeRule.Value > 0 ? domesticFeeRule.Value : 5000m;

            decimal totalEstimatedCost = productsSubtotal + calculatedPurchaseFee + estimatedShippingFee + domesticFee;
            string feeDescription = isFixedFee ? $"{calculatedPurchaseFee:N0}đ (cố định)" : $"{feePercent:G29}%";

            return new CalculatePurchaseQuotationFeesResponseDto
            {
                PurchaseRequestId = requestId,
                ProductsSubtotal = Math.Round(productsSubtotal, 0),
                CalculatedPurchaseFee = calculatedPurchaseFee,
                AppliedPurchaseFeePercent = feePercent,
                EstimatedShippingFee = estimatedShippingFee,
                DomesticShippingFee = domesticFee,
                TotalEstimatedCost = Math.Round(totalEstimatedCost, 0),
                Note = $"Phí mua hộ: {feeDescription}. Ship quốc tế ước tính: {estimatedShippingFee:N0}đ ({estimatedWeightKg}kg). Ship nội địa: {domesticFee:N0}đ."
            };
        }
    }
}


