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
    public class PurchaseRequestService : IPurchaseRequestService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Các role được phép xem toàn bộ yêu cầu mua hộ cần xử lý (Staff/Sales)
        private static readonly string[] StaffRoles = { "ADMIN", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS" };

        public PurchaseRequestService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
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
                if (item.Quantity <= 0)
                {
                    throw new ArgumentException("Số lượng sản phẩm phải lớn hơn 0.");
                }
            }

            // 4. Khởi tạo yêu cầu mua hộ
            var purchaseCode = $"PUR-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}";
            var purchaseRequest = new PurchaseRequest
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CustomerId = customer.Id,
                PurchaseCode = purchaseCode,
                Route = OrderService.NormalizeRoute(request.Route),
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                ReceiverAddress = request.ReceiverAddress,
                RequiresInspection = request.RequiresInspection,
                RequiresQuantityCheck = request.RequiresQuantityCheck,
                GeneralNote = request.GeneralNote,
                Status = "PENDING_REVIEW",
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Repository<PurchaseRequest>().AddAsync(purchaseRequest);

            // 5. Tạo các sản phẩm liên kết
            var itemRepo = _unitOfWork.Repository<PurchaseRequestItem>();
            var resultItems = new List<PurchaseRequestItemResponseDto>();

            foreach (var item in request.Items)
            {
                var requestItem = new PurchaseRequestItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseRequestId = purchaseRequest.Id,
                    ProductLink = item.ProductLink,
                    SourceWebsite = item.SourceWebsite,
                    ProductType = item.ProductType,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    Attributes = item.Attributes,
                    Note = item.Note,
                    ImageUrl = (item.ImageUrls != null && item.ImageUrls.Any())
                        ? string.Join(",", item.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
                        : (item.ImageUrl ?? string.Empty)
                };
                await itemRepo.AddAsync(requestItem);

                var returnedImageUrls = new List<string>();
                if (!string.IsNullOrWhiteSpace(requestItem.ImageUrl))
                {
                    returnedImageUrls = requestItem.ImageUrl
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(u => u.Trim())
                        .ToList();
                }

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
                    ImageUrl = requestItem.ImageUrl,
                    ImageUrls = returnedImageUrls
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 6. Trả về thông tin yêu cầu mua hộ vừa tạo
            return new PurchaseRequestResponseDto
            {
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode,
                CustomerId = purchaseRequest.CustomerId,
                Route = OrderService.MapRouteToDisplayName(purchaseRequest.Route),
                ReceiverName = purchaseRequest.ReceiverName,
                ReceiverPhone = purchaseRequest.ReceiverPhone,
                ReceiverAddress = purchaseRequest.ReceiverAddress,
                RequiresInspection = purchaseRequest.RequiresInspection,
                RequiresQuantityCheck = purchaseRequest.RequiresQuantityCheck,
                GeneralNote = purchaseRequest.GeneralNote,
                Status = purchaseRequest.Status,
                CreatedAt = purchaseRequest.CreatedAt ?? DateTime.UtcNow,
                Items = resultItems
            };
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
                    ReceiverName = r.ReceiverName ?? string.Empty,
                    ItemCount = products.Count,
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
                var urls = new List<string>();
                if (!string.IsNullOrWhiteSpace(i.ImageUrl))
                {
                    urls = i.ImageUrl
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(u => u.Trim())
                        .ToList();
                }
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
                    ImageUrl = i.ImageUrl,
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
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                ReceiverAddress = request.ReceiverAddress,
                RequiresInspection = request.RequiresInspection,
                RequiresQuantityCheck = request.RequiresQuantityCheck,
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
            decimal vatAmount = Math.Round((request.PurchaseFee + request.ShippingFee + totalAdditionalFees) * 0.08m, 2);
            if (vatAmount > 0)
            {
                quotationFees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    FeeName = "Thuế giá trị gia tăng (VAT 8%)",
                    FeeType = "TAX",
                    CalculationType = "PERCENTAGE",
                    Value = 8m,
                    Amount = vatAmount,
                    Note = "8% phí dịch vụ mua hộ",
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
    }
}
