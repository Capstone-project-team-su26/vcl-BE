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
    public class OrderService : IOrderService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IQuotationService _quotationService;

        // Các giá trị RestrictionType bị coi là hàng cấm (chặn tạo) → level BANNED.
        private static readonly string[] BannedRestrictionTypes = { "BANNED", "PROHIBITED", "CẤM", "CAM" };

        // Các giá trị RestrictionType được coi là hàng hạn chế → level RESTRICTED.
        private static readonly string[] RestrictedRestrictionTypes = { "RESTRICTED", "LIMITED", "HẠN CHẾ", "HAN CHE" };

        // Phương án vận chuyển hợp lệ (A2).
        private static readonly string[] ValidShippingOptions = { "Express", "Consolidation" };

        // Các trạng thái Order mà khách được phép tự hủy (hàng chưa vào kho VN).
        private static readonly string[] CancellableStatuses = { "PENDING_REVIEW", "APPROVED", "WAITING_FOR_PARCEL" };

        // Các role nội bộ được xem mọi yêu cầu ký gửi (không cần là chủ đơn).
        private static readonly string[] StaffRoles = { "ADMIN", "SALE", "WAREHOUSESTAFF", "OPERATIONSMANAGER", "DELIVERY" };

        public OrderService(IUnitOfWork unitOfWork, IQuotationService quotationService)
        {
            _unitOfWork = unitOfWork;
            _quotationService = quotationService;
        }

        public async Task<CreateConsignmentResponse> CreateConsignmentAsync(CreateConsignmentRequest request, Guid userId)
        {
            // 1. Validate phương án vận chuyển
            var shippingOption = ValidShippingOptions.FirstOrDefault(
                o => string.Equals(o, request.ShippingOption, StringComparison.OrdinalIgnoreCase));
            if (shippingOption == null)
                throw new ArgumentException("Phương án vận chuyển không hợp lệ. Chỉ chấp nhận: Express, Consolidation.");

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

            // 4. Tạo Order (chưa sinh ConsignmentCode — sinh khi APPROVED)
            var orderRepo = _unitOfWork.Repository<Order>();
            var now = DateTime.UtcNow;
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                CompanyId = customer.CompanyId,
                ConsignmentCode = null,
                ConsignmentType = shippingOption,
                Status = "PENDING_REVIEW",
                OrderType = "CONSIGNMENT",
                Note = request.Note,
                CreatedAt = now
            };
            await orderRepo.AddAsync(order);

            // 5. Tạo OrderItems
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            foreach (var item in request.Items)
            {
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
                    ReferenceUrl = item.ReferenceUrl,
                    DomesticTrackingCode = item.DomesticTrackingCode,
                    CreatedAt = now
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 6. Sinh báo giá tạm tính (tái sử dụng QuotationService)
            var quotation = await _quotationService.CreateEstimateQuotationAsync(order.Id);

            // 7. Trả về kết quả gộp
            return new CreateConsignmentResponse
            {
                OrderId = order.Id,
                Status = order.Status,
                ConsignmentType = order.ConsignmentType,
                ItemCount = request.Items.Count,
                Quotation = quotation,
                Warnings = warnings
            };
        }

        public async Task<CancelConsignmentResponse> CancelConsignmentAsync(Guid orderId, Guid userId, CancelConsignmentRequest request)
        {
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null)
                throw new UnauthorizedAccessException("Người dùng không phải là khách hàng hợp lệ.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");

            if (order.CustomerId != customer.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền hủy yêu cầu ký gửi này.");

            var currentStatus = order.Status?.ToUpperInvariant() ?? string.Empty;

            if (currentStatus == "CANCELLED")
                throw new ArgumentException("Yêu cầu ký gửi này đã được hủy trước đó.");

            if (!CancellableStatuses.Contains(currentStatus))
                throw new ArgumentException($"Không thể hủy yêu cầu khi đang ở trạng thái {order.Status}. Chỉ được hủy khi hàng chưa vào kho VN.");

            // Hủy Order
            order.Status = "CANCELLED";
            
            var reason = request.CancelReason;
            if (!string.IsNullOrWhiteSpace(reason))
                order.Note = reason;

            orderRepo.Update(order);

            // Hủy các Quotation liên quan đang DRAFT/ACCEPTED để tránh accept lại sau khi đơn đã hủy
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var relatedQuotations = allQuotations
                .Where(q => q.OrderId == orderId)
                .Where(q => q.Status == null
                    || q.Status.Equals("DRAFT", StringComparison.OrdinalIgnoreCase)
                    || q.Status.Equals("ACCEPTED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var quotation in relatedQuotations)
            {
                quotation.Status = "CANCELLED";
                quotationRepo.Update(quotation);
            }

            // Tạo thông báo cho khách hàng
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = customer.UserId,
                Title = "Yêu cầu ký gửi đã bị hủy",
                Content = $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã bị hủy.{(string.IsNullOrWhiteSpace(reason) ? "" : $" Lý do: {reason}")}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            var notificationRepo = _unitOfWork.Repository<Notification>();
            await notificationRepo.AddAsync(notification);

            await _unitOfWork.SaveChangesAsync();

            return new CancelConsignmentResponse
            {
                OrderId = order.Id,
                Status = order.Status,
                CancelReason = reason,
                CancelledQuotations = relatedQuotations.Count,
                ConsignmentCode = order.ConsignmentCode
            };
        }

        public async Task<ConsignmentDetailResponse> GetConsignmentDetailAsync(Guid orderId, Guid? userId, string role)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");

            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.Id == order.CustomerId);

            // Phân quyền: staff xem mọi đơn; customer chỉ xem đơn của mình.
            var isStaff = !string.IsNullOrWhiteSpace(role)
                && StaffRoles.Contains(role.Trim().ToUpperInvariant());
            if (!isStaff)
            {
                if (userId == null || customer == null || customer.UserId != userId)
                    throw new UnauthorizedAccessException("Bạn không có quyền xem yêu cầu ký gửi này.");
            }

            // Mặt hàng thuộc đơn
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var items = allItems
                .Where(i => i.OrderId == orderId)
                .Select(i => new ConsignmentItemDetail
                {
                    Id = i.Id,
                    ProductName = i.ProductName,
                    ProductType = i.ProductType,
                    Quantity = i.Quantity,
                    Weight = i.Weight,
                    Width = i.Width,
                    Height = i.Height,
                    Length = i.Length,
                    DeclaredValue = i.DeclaredValue,
                    ReferenceUrl = i.ReferenceUrl,
                    DomesticTrackingCode = i.DomesticTrackingCode
                })
                .ToList();

            // Báo giá mới nhất
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var latestQuotation = allQuotations
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            ConsignmentQuotationSummary? quotationSummary = null;
            if (latestQuotation != null)
            {
                var freight = latestQuotation.EstimatedFreightCharge ?? 0m;
                var service = latestQuotation.ServiceFee ?? 0m;
                var tax = latestQuotation.TaxAndDuty ?? 0m;

                quotationSummary = new ConsignmentQuotationSummary
                {
                    QuotationId = latestQuotation.Id,
                    QuoteType = latestQuotation.QuoteType,
                    Status = latestQuotation.Status,
                    EstimatedFreightCharge = latestQuotation.EstimatedFreightCharge,
                    ServiceFee = latestQuotation.ServiceFee,
                    TaxAndDuty = latestQuotation.TaxAndDuty,
                    TotalVolume = latestQuotation.TotalVolume,
                    TotalEstimatedCost = freight + service + tax,
                    CreatedAt = latestQuotation.CreatedAt,
                    ExpiredAt = latestQuotation.ExpiredAt
                };
            }

            return new ConsignmentDetailResponse
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode,
                ConsignmentType = order.ConsignmentType ?? string.Empty,
                Status = order.Status ?? string.Empty,
                OrderType = order.OrderType,
                TotalWeight = order.TotalWeight,
                TotalVolume = order.TotalVolume,
                Note = order.Note,
                CreatedAt = order.CreatedAt,
                Customer = new ConsignmentCustomerInfo
                {
                    CustomerId = customer?.Id,
                    FullName = customer?.FullName ?? string.Empty,
                    Email = customer?.Email,
                    Phone = customer?.Phone
                },
                Items = items,
                Quotation = quotationSummary
            };
        }

        /// <summary>
        /// Kiểm tra danh sách mặt hàng so với danh mục hàng cấm/hạn chế (chỉ kiểm tra, không tạo Order).
        /// Dùng cho endpoint validate-items.
        /// </summary>
        public async Task<ValidateItemsResponse> ValidateConsignmentItemsAsync(ValidateItemsRequest request)
        {
            if (request.Items == null || request.Items.Count == 0)
                throw new ArgumentException("Phải có ít nhất một mặt hàng để kiểm tra.");

            var results = await ValidateItemsAgainstRestrictedAsync(request.Items);

            return new ValidateItemsResponse
            {
                // Có hàng BANNED → không cho tạo; hàng hạn chế/cảnh báo vẫn cho tạo.
                CanCreate = !results.Any(r => r.Level == "BANNED"),
                Results = results
            };
        }

        /// <summary>
        /// Đối chiếu từng mặt hàng với bảng RestrictedItem, phân loại 3 mức BANNED/RESTRICTED/WARNING.
        /// Chỉ trả về các mặt hàng có khớp (có vấn đề). Logic dùng chung cho cả create lẫn validate.
        /// </summary>
        private async Task<List<ItemValidationResult>> ValidateItemsAgainstRestrictedAsync(List<ConsignmentItemRequest> items)
        {
            var restrictedRepo = _unitOfWork.Repository<RestrictedItem>();
            var allRestricted = (await restrictedRepo.GetAllAsync()).ToList();

            var results = new List<ItemValidationResult>();

            foreach (var item in items)
            {
                foreach (var r in allRestricted)
                {
                    if (!r.IsActive) continue;
                    if (string.IsNullOrWhiteSpace(r.ItemName)) continue;

                    var matchesName = !string.IsNullOrWhiteSpace(item.ProductName)
                        && item.ProductName.Contains(r.ItemName, StringComparison.OrdinalIgnoreCase);
                    var matchesType = !string.IsNullOrWhiteSpace(item.ProductType)
                        && item.ProductType.Contains(r.ItemName, StringComparison.OrdinalIgnoreCase);

                    if (!matchesName && !matchesType) continue;

                    results.Add(new ItemValidationResult
                    {
                        ProductName = item.ProductName,
                        Level = MapRestrictionLevel(r.RestrictionType),
                        MatchedItem = r.ItemName,
                        Country = r.Country,
                        Reason = r.Note
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Quy đổi RestrictionType (free text trong DB) về 1 trong 3 mức chuẩn: BANNED / RESTRICTED / WARNING.
        /// </summary>
        private static string MapRestrictionLevel(string? restrictionType)
        {
            if (string.IsNullOrWhiteSpace(restrictionType))
                return "WARNING";

            var normalized = restrictionType.Trim().ToUpperInvariant();

            if (BannedRestrictionTypes.Contains(normalized))
                return "BANNED";
            if (RestrictedRestrictionTypes.Contains(normalized))
                return "RESTRICTED";

            return "WARNING";
        }

        public async Task<UpdateConsignmentStatusResponse> UpdateConsignmentStatusAsync(Guid orderId, UpdateConsignmentStatusRequest request)
        {
            var validStatuses = new[] { "PENDING_REVIEW", "APPROVED", "REJECTED" };
            if (string.IsNullOrWhiteSpace(request.Status) || !validStatuses.Contains(request.Status.ToUpper()))
            {
                throw new ArgumentException("Trạng thái không hợp lệ. Các trạng thái hợp lệ: PENDING_REVIEW, APPROVED, REJECTED.");
            }

            var normalizedStatus = request.Status.ToUpper();

            if (normalizedStatus == "REJECTED" && string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                throw new ArgumentException("Vui lòng cung cấp lý do từ chối (RejectionReason).");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);

            if (order == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu ký gửi với ID {orderId}");
            }

            // Cannot update if CANCELLED or WAITING_FOR_PARCEL
            var invalidCurrentStatuses = new[] { "CANCELLED", "WAITING_FOR_PARCEL", "WAREHOUSE_RECEIVED" };
            if (order.Status != null && invalidCurrentStatuses.Contains(order.Status.ToUpper()))
            {
                throw new ArgumentException($"Không thể cập nhật trạng thái yêu cầu khi yêu cầu đang ở trạng thái {order.Status}");
            }

            if (normalizedStatus == "APPROVED" && string.IsNullOrEmpty(order.ConsignmentCode))
            {
                var allOrders = await orderRepo.GetAllAsync();
                var random = new Random();
                string generatedCode;
                do
                {
                    var now = DateTime.UtcNow;
                    var randomSuffix = random.Next(100000, 999999).ToString();
                    generatedCode = $"VCL-{now:yyyyMMddHHmmss}-{randomSuffix}";
                } while (allOrders.Any(o => o.ConsignmentCode == generatedCode));

                order.ConsignmentCode = generatedCode;
            }

            var oldStatus = order.Status;
            order.Status = normalizedStatus;
            
            if (normalizedStatus == "REJECTED")
            {
                order.Note = request.RejectionReason;
            }

            if (oldStatus != normalizedStatus)
            {
                if (normalizedStatus == "APPROVED" || normalizedStatus == "REJECTED")
                {
                    var customerRepo = _unitOfWork.Repository<Customer>();
                    var customer = await customerRepo.GetByIdAsync(order.CustomerId);
                    if (customer != null)
                    {
                        var notification = new Notification
                        {
                            Id = Guid.NewGuid(),
                            UserId = customer.UserId,
                            Title = normalizedStatus == "APPROVED" ? "Yêu cầu ký gửi đã được duyệt" : "Yêu cầu ký gửi bị từ chối",
                            Content = normalizedStatus == "APPROVED"
                                ? $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã được duyệt."
                                : $"Yêu cầu ký gửi (Mã: {order.ConsignmentCode ?? order.Id.ToString()}) của bạn đã bị từ chối.{(string.IsNullOrWhiteSpace(request.RejectionReason) ? "" : $" Lý do: {request.RejectionReason}")}",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        var notificationRepo = _unitOfWork.Repository<Notification>();
                        await notificationRepo.AddAsync(notification);
                    }
                }
            }

            orderRepo.Update(order);
            await _unitOfWork.SaveChangesAsync();

            return new UpdateConsignmentStatusResponse
            {
                Status = order.Status,
                ConsignmentCode = order.ConsignmentCode
            };
        }

        public async Task<PaginatedResponse<ConsignmentResponseDto>> GetConsignmentsAsync(Guid? userId, string role, int pageNumber, int pageSize, string? status, string? searchCode)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var customerRepo = _unitOfWork.Repository<Customer>();

            // Get all data into memory - suitable for GenericRepository pattern
            var allOrders = await orderRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();

            var query = allOrders.AsQueryable();

            var isStaff = !string.IsNullOrWhiteSpace(role)
                && StaffRoles.Contains(role.Trim().ToUpperInvariant());

            // Customer only sees their own requests, staff can see all requests
            if (!isStaff)
            {
                if (userId == null)
                {
                    throw new UnauthorizedAccessException("Người dùng không hợp lệ.");
                }

                var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null)
                {
                    return new PaginatedResponse<ConsignmentResponseDto>
                    {
                        Items = new List<ConsignmentResponseDto>(),
                        TotalCount = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                query = query.Where(o => o.CustomerId == customer.Id);
            }

            // Filter by status
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => string.Equals(o.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            // Search by consignment code
            if (!string.IsNullOrWhiteSpace(searchCode))
            {
                query = query.Where(o => o.ConsignmentCode != null && o.ConsignmentCode.Contains(searchCode, StringComparison.OrdinalIgnoreCase));
            }

            var totalCount = query.Count();

            // Pagination
            var paginatedOrders = query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Map to DTO
            var resultItems = paginatedOrders.Select(o => 
            {
                var customerInfo = allCustomers.FirstOrDefault(c => c.Id == o.CustomerId);
                return new ConsignmentResponseDto
                {
                    OrderId = o.Id,
                    ConsignmentCode = o.ConsignmentCode ?? string.Empty,
                    CustomerName = customerInfo?.FullName ?? string.Empty,
                    ConsignmentType = o.ConsignmentType ?? string.Empty,
                    Status = o.Status ?? string.Empty,
                    TotalWeight = o.TotalWeight,
                    TotalVolume = o.TotalVolume,
                    CreatedAt = o.CreatedAt
                };
            }).ToList();

            return new PaginatedResponse<ConsignmentResponseDto>
            {
                Items = resultItems,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

    }
}
