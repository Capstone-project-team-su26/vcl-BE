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

        public PurchaseRequestService(IUnitOfWork unitOfWork, IOrderService orderService)
        {
            _unitOfWork = unitOfWork;
            _orderService = orderService;
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

            // 5. Map sang DTO (kèm tên người nhận và danh sách sản phẩm tóm tắt)
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

            // 4.1 Lấy báo giá liên quan nếu có
            PurchaseQuotationResponseDto? quotationDto = null;
            var quotationRepo = _unitOfWork.Repository<PurchaseQuotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var quotation = allQuotations
                .FirstOrDefault(q => q.PurchaseRequestId == requestId);

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
                TotalQuantity = relatedItems.Sum(i => i.Quantity),
                Items = itemDtos,
                Quotation = quotationDto
            };
        }

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
            var quotation = new PurchaseQuotation
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                PurchaseFee = request.PurchaseFee,
                ShippingFee = request.ShippingFee,
                Note = request.Note,
                Status = "PENDING_CUSTOMER_CONFIRMATION",
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
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
                var lineTotal = line.UnitPrice * reqItem.Quantity;
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

                var itemImportTax = Math.Round(lineTotal * category.ImportTaxRate.Value, 2);
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
                    UnitPrice = line.UnitPrice,
                    Quantity = reqItem.Quantity,
                    LineTotal = lineTotal
                };
                await quotationItemRepo.AddAsync(quotationItem);

                itemDtos.Add(new PurchaseQuotationItemResponseDto
                {
                    QuotationItemId = quotationItem.Id,
                    PurchaseRequestItemId = reqItem.Id,
                    ProductName = reqItem.ProductName,
                    UnitPrice = line.UnitPrice,
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

            decimal vatAmount = Math.Round((request.PurchaseFee + request.ShippingFee + totalAdditionalFees) * (vatRatePercent / 100m), 2);
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

            var purchaseFeeRule = activeRules.FirstOrDefault(r =>
                string.Equals(r.RuleType, "PURCHASE_FEE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RuleCode, "PURCHASE_FEE", StringComparison.OrdinalIgnoreCase) ||
                r.RuleName.Contains("mua hộ", StringComparison.OrdinalIgnoreCase) ||
                r.RuleName.Contains("mua hang", StringComparison.OrdinalIgnoreCase));

            decimal feePercent = purchaseFeeRule != null && purchaseFeeRule.Value > 0 ? purchaseFeeRule.Value : 5.0m;
            decimal calculatedPurchaseFee = productsSubtotal * (feePercent / 100m);

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

            decimal totalEstimatedCost = productsSubtotal + calculatedPurchaseFee + estimatedShippingFee;

            return new CalculatePurchaseQuotationFeesResponseDto
            {
                PurchaseRequestId = requestId,
                ProductsSubtotal = Math.Round(productsSubtotal, 0),
                CalculatedPurchaseFee = calculatedPurchaseFee,
                AppliedPurchaseFeePercent = feePercent,
                EstimatedShippingFee = estimatedShippingFee,
                TotalEstimatedCost = Math.Round(totalEstimatedCost, 0),
                Note = $"Phí mua hộ tính theo mức {feePercent}%. Phí vận chuyển ước tính trên {estimatedWeightKg}kg ({totalQuantity} món)."
            };
        }
    }
}

