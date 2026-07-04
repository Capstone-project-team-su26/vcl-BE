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
                Route = request.Route,
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
                    ImageUrl = item.ImageUrl
                };
                await itemRepo.AddAsync(requestItem);

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
                    ImageUrl = requestItem.ImageUrl
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 6. Trả về thông tin yêu cầu mua hộ vừa tạo
            return new PurchaseRequestResponseDto
            {
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode,
                CustomerId = purchaseRequest.CustomerId,
                Route = purchaseRequest.Route,
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

            // 5. Map sang DTO (kèm tên Customer và số lượng sản phẩm)
            var resultItems = pagedRequests.Select(r =>
            {
                var customerInfo = r.CustomerId.HasValue
                    ? allCustomers.FirstOrDefault(c => c.Id == r.CustomerId.Value)
                    : null;

                return new PurchaseRequestListItemDto
                {
                    PurchaseRequestId = r.Id,
                    PurchaseCode = r.PurchaseCode ?? string.Empty,
                    CustomerId = r.CustomerId,
                    CustomerName = customerInfo?.FullName ?? string.Empty,
                    ItemCount = allItems.Count(i => i.PurchaseRequestId == r.Id),
                    Status = r.Status ?? string.Empty,
                    GeneralNote = r.GeneralNote,
                    CreatedAt = r.CreatedAt ?? DateTime.MinValue
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

            var itemDtos = relatedItems.Select(i => new PurchaseRequestItemResponseDto
            {
                ItemId = i.Id,
                ProductLink = i.ProductLink ?? string.Empty,
                SourceWebsite = i.SourceWebsite,
                ProductType = i.ProductType,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                Attributes = i.Attributes,
                Note = i.Note,
                ImageUrl = i.ImageUrl
            }).ToList();

            return new PurchaseRequestDetailResponseDto
            {
                PurchaseRequestId = request.Id,
                PurchaseCode = request.PurchaseCode ?? string.Empty,
                CustomerId = request.CustomerId,
                CustomerName = owner?.FullName ?? string.Empty,
                CreatedByName = createdByName,
                Route = request.Route,
                ReceiverName = request.ReceiverName,
                ReceiverPhone = request.ReceiverPhone,
                ReceiverAddress = request.ReceiverAddress,
                RequiresInspection = request.RequiresInspection,
                RequiresQuantityCheck = request.RequiresQuantityCheck,
                GeneralNote = request.GeneralNote,
                Status = request.Status ?? string.Empty,
                CreatedAt = request.CreatedAt ?? DateTime.MinValue,
                TotalQuantity = relatedItems.Sum(i => i.Quantity),
                Items = itemDtos
            };
        }
    }
}
