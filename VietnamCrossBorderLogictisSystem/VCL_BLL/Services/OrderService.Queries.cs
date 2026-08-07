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

            var packageConfigs = await _unitOfWork.Repository<PackageConfiguration>().GetAllAsync();
            var configMap = packageConfigs.ToDictionary(c => c.Id, c => c);

            var divisorSection = _configuration.GetSection("PricingSettings:VolumetricDivisor");
            decimal divisor = 5000m;
            if (divisorSection.Value != null && decimal.TryParse(divisorSection.Value, out var div))
            {
                divisor = div;
            }

            var items = allItems
                .Where(i => i.OrderId == orderId)
                .Select(i =>
                {
                    var qty = i.Quantity ?? 1;
                    var itemLength = i.Length ?? 0m;
                    var itemWidth = i.Width ?? 0m;
                    var itemHeight = i.Height ?? 0m;
                    var itemVolume = itemLength * itemWidth * itemHeight;
                    decimal? volumetricWeight = null;
                    if (itemLength > 0 && itemWidth > 0 && itemHeight > 0)
                    {
                        volumetricWeight = Math.Round(itemVolume / divisor, 4);
                    }

                    PackageConfigurationResponseDto? packageDto = null;
                    if (i.PackageConfigurationId.HasValue && configMap.TryGetValue(i.PackageConfigurationId.Value, out var pc))
                    {
                        packageDto = new PackageConfigurationResponseDto
                        {
                            Id = pc.Id,
                            ConfigCode = pc.ConfigCode,
                            ConfigName = pc.ConfigName,
                            Length = pc.Length,
                            Width = pc.Width,
                            Height = pc.Height,
                            MaxWeight = pc.MaxWeight,
                            PackageFee = pc.PackageFee,
                            Status = pc.Status
                        };
                    }

                    return new ConsignmentItemDetail
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
                        ReferenceUrls = ConsignmentItemRequest.SplitReferenceUrls(i.ReferenceUrl),
                        DomesticTrackingCode = i.DomesticTrackingCode,
                        PackageConfigurationId = i.PackageConfigurationId,
                        PackageConfiguration = packageDto,
                        VolumetricWeight = volumetricWeight
                    };
                })
                .ToList();

            // Báo giá mới nhất
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var latestQuotation = allQuotations
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            var pricingRuleIds = new List<Guid>();
            ConsignmentQuotationSummary? quotationSummary = null;
            if (latestQuotation != null)
            {
                var freight = latestQuotation.EstimatedFreightCharge ?? 0m;
                var service = latestQuotation.ServiceFee ?? 0m;
                var tax = latestQuotation.TaxAndDuty ?? 0m;

                var feeRepo = _unitOfWork.Repository<QuotationFee>();
                var allFees = await feeRepo.GetAllAsync();
                var fees = allFees.Where(f => f.QuotationId == latestQuotation.Id).ToList();

                var rules = await _unitOfWork.Repository<PricingRule>().GetAllAsync();
                var ruleMap = rules.ToDictionary(r => r.Id, r => r);

                var domesticShippingFee = fees
                    .Where(f => string.Equals(f.FeeType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase))
                    .Sum(f => f.Amount);

                var additionalSurchargesAmount = fees
                    .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase)
                                && (f.Amount > 0 || (f.PricingRuleId.HasValue && ruleMap.TryGetValue(f.PricingRuleId.Value, out var r) && r.IsRequired)))
                    .Sum(f => f.Amount);

                pricingRuleIds = fees
                    .Where(f => f.PricingRuleId.HasValue && f.Amount > 0)
                    .Select(f => f.PricingRuleId!.Value)
                    .Where(id => ruleMap.TryGetValue(id, out var r) && 
                                 !string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) && 
                                 !r.IsRequired)
                    .Distinct()
                    .ToList();

                var quotationWarehouse = latestQuotation.WarehouseId.HasValue
                    ? (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                        .FirstOrDefault(w => w.Id == latestQuotation.WarehouseId.Value)
                    : null;

                quotationSummary = new ConsignmentQuotationSummary
                {
                    QuotationId = latestQuotation.Id,
                    WarehouseId = latestQuotation.WarehouseId,
                    WarehouseName = quotationWarehouse?.WarehouseName,
                    WarehouseCode = quotationWarehouse?.WarehouseCode,
                    WarehouseAddress = quotationWarehouse?.Address,
                    WarehouseRegion = quotationWarehouse?.Region?.ToString(),
                    WarehouseType = quotationWarehouse?.WarehouseType,
                    QuoteType = latestQuotation.QuoteType,
                    Status = latestQuotation.Status,
                    EstimatedFreightCharge = latestQuotation.EstimatedFreightCharge,
                    DomesticShippingFee = domesticShippingFee,
                    ServiceFee = Math.Max(0m, (latestQuotation.ServiceFee ?? 0m) - domesticShippingFee),
                    TaxAndDuty = latestQuotation.TaxAndDuty,
                    TotalVolume = latestQuotation.TotalVolume,
                    TotalEstimatedCost = freight + service + tax,
                    CreatedAt = latestQuotation.CreatedAt,
                    ExpiredAt = latestQuotation.ExpiredAt
                };
            }

            var allInvoices = await _unitOfWork.Repository<Invoice>().GetAllAsync();
            var allPayments = await _unitOfWork.Repository<Payment>().GetAllAsync();
            var orderQuotationIds = allQuotations.Where(q => q.OrderId == orderId).Select(q => q.Id).ToHashSet();
            var orderInvoiceIds = allInvoices.Where(i => i.QuotationId.HasValue && orderQuotationIds.Contains(i.QuotationId.Value)).Select(i => i.Id).ToHashSet();
            var paidPayment = allPayments
                .Where(p => p.InvoiceId.HasValue && orderInvoiceIds.Contains(p.InvoiceId.Value) && (string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase) || string.Equals(p.PaymentStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                .FirstOrDefault();

            var paymentConfirmedAt = order.PaymentConfirmedAt ?? paidPayment?.PaidAt ?? paidPayment?.CreatedAt;

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
                Route = MapRouteToDisplayName(order.Route),
                ReceiverName = order.ReceiverName,
                ReceiverPhone = order.ReceiverPhone,
                ReceiverAddress = order.ReceiverAddress,
                RequiresInspection = order.RequiresInspection,
                CreatedAt = order.CreatedAt,
                PaymentConfirmedAt = paymentConfirmedAt,
                QuotationCreatedAt = latestQuotation?.CreatedAt,
                StatusUpdatedAt = paymentConfirmedAt ?? latestQuotation?.CreatedAt ?? order.CreatedAt,
                ReceiptPdfUrl = order.ReceiptPdfUrl,
                PricingRuleIds = pricingRuleIds,
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

        public async Task<PaginatedResponse<ConsignmentResponseDto>> GetConsignmentsAsync(Guid? userId, string role, int pageNumber, int pageSize, string? status, string? searchCode)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var customerRepo = _unitOfWork.Repository<Customer>();

            // Get all data into memory - suitable for GenericRepository pattern
            var allOrders = await orderRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();
            var allItems = await _unitOfWork.Repository<OrderItem>().GetAllAsync();

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

            var allQuotations = await _unitOfWork.Repository<Quotation>().GetAllAsync();
            var allFees = await _unitOfWork.Repository<QuotationFee>().GetAllAsync();
            var allRules = await _unitOfWork.Repository<PricingRule>().GetAllAsync();
            var ruleMap = allRules.ToDictionary(r => r.Id, r => r);

            var allInvoices = await _unitOfWork.Repository<Invoice>().GetAllAsync();
            var allPayments = await _unitOfWork.Repository<Payment>().GetAllAsync();
            var warehouseMap = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .ToDictionary(w => w.Id, w => w);

            var invoiceQuotationMap = allInvoices
                .Where(i => i.QuotationId.HasValue)
                .ToDictionary(i => i.Id, i => i.QuotationId!.Value);

            var quotationOrderMap = allQuotations
                .ToDictionary(q => q.Id, q => q.OrderId);

            var paidPaymentsByOrder = allPayments
                .Where(p => p.InvoiceId.HasValue
                            && invoiceQuotationMap.ContainsKey(p.InvoiceId.Value)
                            && (string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase) || string.Equals(p.PaymentStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase)))
                .Select(p => {
                    var qId = invoiceQuotationMap[p.InvoiceId!.Value];
                    var oId = quotationOrderMap.TryGetValue(qId, out var id) ? id : (Guid?)null;
                    return new { OrderId = oId, PaidTime = p.PaidAt ?? p.CreatedAt };
                })
                .Where(p => p.OrderId.HasValue)
                .GroupBy(p => p.OrderId!.Value)
                .ToDictionary(g => g.Key, g => g.Max(p => p.PaidTime));

            // Map to DTO
            var resultItems = paginatedOrders.Select(o => 
            {
                var customerInfo = allCustomers.FirstOrDefault(c => c.Id == o.CustomerId);
                var latestQuotation = allQuotations
                    .Where(q => q.OrderId == o.Id)
                    .OrderByDescending(q => q.CreatedAt)
                    .FirstOrDefault();

                var pricingRuleIds = new List<Guid>();
                if (latestQuotation != null)
                {
                    var fees = allFees.Where(f => f.QuotationId == latestQuotation.Id).ToList();
                    pricingRuleIds = fees
                        .Where(f => f.PricingRuleId.HasValue && f.Amount > 0)
                        .Select(f => f.PricingRuleId!.Value)
                        .Where(id => ruleMap.TryGetValue(id, out var r) && 
                                     !string.Equals(r.RuleCode, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase) && 
                                     !r.IsRequired)
                        .Distinct()
                        .ToList();
                }

                var paidTime = paidPaymentsByOrder.TryGetValue(o.Id, out var pt) ? pt : (DateTime?)null;
                var paymentConfirmed = o.PaymentConfirmedAt ?? paidTime;

                Warehouse? orderWarehouse = null;
                if (latestQuotation?.WarehouseId != null)
                {
                    warehouseMap.TryGetValue(latestQuotation.WarehouseId.Value, out orderWarehouse);
                }

                return new ConsignmentResponseDto
                {
                    OrderId = o.Id,
                    ConsignmentCode = o.ConsignmentCode ?? string.Empty,
                    CustomerName = customerInfo?.FullName ?? string.Empty,
                    ConsignmentType = o.ConsignmentType ?? string.Empty,
                    Status = o.Status ?? string.Empty,
                    TotalWeight = o.TotalWeight,
                    TotalVolume = o.TotalVolume,
                    Route = MapRouteToDisplayName(o.Route),
                    ReceiverName = o.ReceiverName,
                    ReceiverPhone = o.ReceiverPhone,
                    ReceiverAddress = o.ReceiverAddress,
                    RequiresInspection = o.RequiresInspection,
                    CreatedAt = o.CreatedAt,
                    PaymentConfirmedAt = paymentConfirmed,
                    QuotationCreatedAt = latestQuotation?.CreatedAt,
                    StatusUpdatedAt = paymentConfirmed ?? latestQuotation?.CreatedAt ?? o.CreatedAt,
                    WarehouseId = latestQuotation?.WarehouseId,
                    WarehouseName = orderWarehouse?.WarehouseName,
                    WarehouseCode = orderWarehouse?.WarehouseCode,
                    WarehouseRegion = orderWarehouse?.Region?.ToString(),
                    WarehouseType = orderWarehouse?.WarehouseType,
                    ReceiptPdfUrl = o.ReceiptPdfUrl,
                    PricingRuleIds = pricingRuleIds,
                    ItemNames = allItems
                        .Where(i => i.OrderId == o.Id && !string.IsNullOrWhiteSpace(i.ProductName))
                        .Select(i => i.ProductName!)
                        .ToList()
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

        public List<ConsignmentStatusDto> GetConsignmentStatuses()
        {
            return new List<ConsignmentStatusDto>
            {
                new() { Code = "PENDING_REVIEW", Label = "Chờ duyệt" },
                new() { Code = "QUOTATION_SENT", Label = "Đã gửi báo giá" },
                new() { Code = "APPROVED", Label = "Đã duyệt" },
                new() { Code = "REJECTED", Label = "Đã từ chối" },
                new() { Code = "CANCELLED", Label = "Đã hủy" },
                new() { Code = "COMPLETED", Label = "Hoàn tất" }
            };
        }

        public async Task<PublicTrackingResponse> TrackConsignmentByCodeAsync(string code)
        {
            // 1. Validate mã đầu vào (400 nếu rỗng)
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Vui lòng cung cấp mã vận đơn để tra cứu.");
            }

            var trimmedCode = code.Trim();

            // 2. Tìm đơn ký gửi theo mã vận đơn (404 nếu không có)
            var allOrders = await _unitOfWork.Repository<Order>().GetAllAsync();
            var order = allOrders.FirstOrDefault(o =>
                !string.IsNullOrEmpty(o.ConsignmentCode)
                && string.Equals(o.ConsignmentCode, trimmedCode, StringComparison.OrdinalIgnoreCase));
            if (order == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn hàng với mã vận đơn: {trimmedCode}");
            }

            // 3. Lấy nhãn trạng thái hiển thị
            var statusLabel = GetConsignmentStatuses()
                .FirstOrDefault(s => string.Equals(s.Code, order.Status, StringComparison.OrdinalIgnoreCase))
                ?.Label ?? order.Status ?? string.Empty;

            // 4. Số mặt hàng + danh sách kiện hàng (chỉ dữ liệu trạng thái, không lộ thông tin nhạy cảm)
            var allItems = await _unitOfWork.Repository<OrderItem>().GetAllAsync();
            var itemCount = allItems.Where(i => i.OrderId == order.Id).Sum(i => i.Quantity ?? 0);

            var allParcels = await _unitOfWork.Repository<Parcel>().GetAllAsync();
            var parcels = allParcels
                .Where(p => p.ConsignmentId == order.Id)
                .OrderBy(p => p.CheckInTime)
                .Select(p => new PublicTrackingParcelDto
                {
                    PackageCode = p.PackageCode ?? string.Empty,
                    PackageStatus = p.PackageStatus ?? string.Empty,
                    CheckInTime = p.CheckInTime,
                    PutAwayTime = p.PutAwayTime
                })
                .ToList();

            return new PublicTrackingResponse
            {
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                ConsignmentType = order.ConsignmentType,
                Route = MapRouteToDisplayName(order.Route),
                Status = order.Status ?? string.Empty,
                StatusLabel = statusLabel,
                ReceiverName = order.ReceiverName,
                ItemCount = itemCount,
                CreatedAt = order.CreatedAt,
                Parcels = parcels
            };
        }

        public async Task<List<string>> GetConsignmentRoutesAsync()
        {
            var pricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                .Where(p => !p.IsDeleted)
                .ToList();

            var routes = pricings
                .Where(p => !string.IsNullOrWhiteSpace(p.OriginCountry) && !string.IsNullOrWhiteSpace(p.DestinationCountry))
                .Select(p => MapRouteToDisplayName($"{p.OriginCountry}-{p.DestinationCountry}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r)
                .ToList();

            if (!routes.Any())
            {
                routes.Add("Trung quốc --> Việt Nam");
            }

            return routes;
        }

        public async Task<List<string>> GetShippingOptionsAsync(string? route)
        {
            var pricings = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync()).Where(p => !p.IsDeleted).ToList();
            var query = pricings.Where(p => !string.IsNullOrWhiteSpace(p.ServiceType));

            if (!string.IsNullOrWhiteSpace(route))
            {
                var normalizedRoute = NormalizeRoute(route);
                query = query.Where(p =>
                    !string.IsNullOrWhiteSpace(p.OriginCountry)
                    && !string.IsNullOrWhiteSpace(p.DestinationCountry)
                    && string.Equals($"{p.OriginCountry}-{p.DestinationCountry}", normalizedRoute, StringComparison.OrdinalIgnoreCase));
            }

            return query
                .Select(p => p.ServiceType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();
        }
    }
}
