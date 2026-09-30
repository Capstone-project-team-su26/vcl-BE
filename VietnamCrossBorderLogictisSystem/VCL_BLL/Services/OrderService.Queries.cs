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
            var isStaff = StaffRoleHelper.IsInternalStaff(role);
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
            var servicesByItem = await ItemServiceRules.LoadDtosAsync(
                _unitOfWork, allItems.Where(i => i.OrderId == orderId).Select(i => i.Id));

            // Cùng nguồn với báo giá / check-in: rule VOLUMETRIC_DIVISOR trước, cấu hình sau.
            var divisor = await VolumetricDivisorResolver.ResolveAsync(_unitOfWork, _configuration);

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
                        Services = ItemServiceRules.For(servicesByItem, i.Id),
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
                    ExpiredAt = latestQuotation.ExpiredAt,
                    PriceApprovalStatus = latestQuotation.PriceApprovalStatus
                };

                var requote = QuotationAcceptanceRules.EvaluateRequote(
                    order, allQuotations.Where(q => q.OrderId == orderId), DateTime.UtcNow);
                quotationSummary.CanSalesRequote = requote.Allowed;
                quotationSummary.RequoteState = requote.State;
                quotationSummary.RequoteBlockedReason = requote.Reason;
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

            var shipmentGroups = await GetOrderShipmentJourneyAsync(orderId);

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
                DefaultDestinationHandling = order.DefaultDestinationHandling,
                DefaultDestinationHandlingText = DescribeDestinationHandling(order.DefaultDestinationHandling),
                PricingRuleIds = pricingRuleIds,
                Customer = new ConsignmentCustomerInfo
                {
                    CustomerId = customer?.Id,
                    FullName = customer?.FullName ?? string.Empty,
                    Email = customer?.Email,
                    Phone = customer?.Phone
                },
                Items = items,
                Quotation = quotationSummary,
                Shipments = shipmentGroups,
                DiscrepancyParcelCount = shipmentGroups
                    .SelectMany(g => g.Parcels)
                    .Count(p => p.Inspection?.HasDiscrepancy == true),
                WarehouseNotifiedAt = order.WarehouseNotifiedAt,
                WarehouseNotifiedNote = order.WarehouseNotifiedNote
            };
        }

        /// <summary>
        /// Dựng hành trình vận chuyển quốc tế của đơn: kiện nào đang đi lô nào, lô tới đâu,
        /// kho Việt Nam kiểm đếm ra sao.
        /// </summary>
        /// <remarks>
        /// Gom theo lô chứ không liệt kê phẳng từng kiện, vì một đơn hoàn toàn có thể bị tách
        /// làm nhiều chuyến — khách hỏi thì Sale phải trả lời được "kiện này về rồi, kiện kia
        /// còn ở Quảng Châu", không phải một trạng thái chung chung cho cả đơn.
        /// <para>
        /// Public vì đơn mua hộ cũng cần đúng khối này; bên đó tra ra Order tương ứng rồi gọi
        /// lại hàm này thay vì chép một bản thứ hai.
        /// </para>
        /// </remarks>
        public async Task<List<ConsignmentShipmentGroup>> GetOrderShipmentJourneyAsync(Guid orderId)
        {
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .OrderBy(p => p.CheckInTime ?? DateTime.MaxValue)
                .ThenBy(p => p.PackageCode)
                .ToList();

            if (parcels.Count == 0)
            {
                return new List<ConsignmentShipmentGroup>();
            }

            var parcelIds = parcels.Select(p => p.Id).ToHashSet();

            // Kiện bị tách rồi ghép lại có thể còn nhiều dòng liên kết; lấy dòng cuối vì đó là
            // lô đang thực sự chở kiện.
            var shipmentIdByParcel = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                .Where(sp => parcelIds.Contains(sp.ParcelId))
                .GroupBy(sp => sp.ParcelId)
                .ToDictionary(g => g.Key, g => g.Last().ShipmentId);

            var shipmentIds = shipmentIdByParcel.Values.ToHashSet();
            var shipments = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .Where(s => shipmentIds.Contains(s.Id))
                .ToDictionary(s => s.Id, s => s);

            var warehouseNames = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .ToDictionary(w => w.Id, w => w.WarehouseName);

            // Kho có thể sửa lại số đã đếm; chỉ bản mới nhất mới phản ánh đúng hiện trạng.
            var inspections = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync())
                .Where(i => parcelIds.Contains(i.ParcelId))
                .GroupBy(i => i.ParcelId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.InspectedAt).First());

            var groups = new List<ConsignmentShipmentGroup>();

            foreach (var byShipment in parcels.GroupBy(p =>
                shipmentIdByParcel.TryGetValue(p.Id, out var sid) ? sid : (Guid?)null))
            {
                Shipment? shipment = null;
                if (byShipment.Key.HasValue)
                {
                    shipments.TryGetValue(byShipment.Key.Value, out shipment);
                }

                groups.Add(new ConsignmentShipmentGroup
                {
                    ShipmentId = shipment?.Id,
                    ShipmentCode = shipment?.ShipmentCode,
                    Status = shipment?.Status,
                    StatusText = shipment == null
                        ? "Kiện đã đóng, chờ xếp lô"
                        : DescribeShipmentStatus(shipment.Status),
                    CarrierTrackingCode = shipment?.CarrierTrackingCode,
                    OriginWarehouseName = LookupWarehouseName(warehouseNames, shipment?.OriginWarehouseId),
                    DestinationWarehouseName = LookupWarehouseName(warehouseNames, shipment?.DestinationWarehouseId),
                    ShippedAt = shipment?.ShippedAt,
                    DeliveredAt = shipment?.DeliveredAt,
                    Parcels = byShipment
                        .Select(p => BuildParcelStatus(p, inspections))
                        .ToList(),
                });
            }

            // Lô đã đi xếp trước, kiện chưa lên lô xuống cuối: Sale mở ra là thấy ngay hàng
            // đang chạy, phần chưa khởi hành đọc sau cũng được.
            return groups
                .OrderBy(g => g.ShipmentId.HasValue ? 0 : 1)
                .ThenByDescending(g => g.ShippedAt ?? DateTime.MinValue)
                .ToList();
        }

        private static string? LookupWarehouseName(
            IReadOnlyDictionary<Guid, string> names, Guid? warehouseId) =>
            warehouseId.HasValue && names.TryGetValue(warehouseId.Value, out var name) ? name : null;

        private static ConsignmentParcelStatus BuildParcelStatus(
            Parcel parcel, IReadOnlyDictionary<Guid, ParcelInspection> inspections)
        {
            ConsignmentParcelCheckResult? check = null;

            if (inspections.TryGetValue(parcel.Id, out var inspection))
            {
                var quantityDiff = inspection.ActualQuantity.HasValue
                    ? inspection.ActualQuantity.Value - inspection.DeclaredQuantity
                    : (int?)null;

                var weightDiff = inspection.ActualWeight.HasValue && inspection.DeclaredWeight.HasValue
                    ? inspection.ActualWeight.Value - inspection.DeclaredWeight.Value
                    : (decimal?)null;

                check = new ConsignmentParcelCheckResult
                {
                    HasDiscrepancy = inspection.HasDiscrepancy,
                    Summary = inspection.HasDiscrepancy
                        ? SummarizeParcelDiscrepancy(quantityDiff, weightDiff, inspection.Condition)
                        : null,
                    ConditionText = DescribeParcelCondition(inspection.Condition),
                    ActualQuantity = inspection.ActualQuantity,
                    ActualWeight = inspection.ActualWeight,
                    DeclaredWeight = inspection.DeclaredWeight,
                    Note = inspection.Note,
                    InspectedAt = inspection.InspectedAt,
                };
            }

            return new ConsignmentParcelStatus
            {
                ParcelId = parcel.Id,
                PackageCode = parcel.PackageCode,
                Status = parcel.PackageStatus,
                StatusText = DescribeParcelJourney(parcel.PackageStatus),
                Weight = parcel.ActualWeight ?? parcel.ChargeableWeight,
                DestinationHandling = parcel.DestinationHandling,
                DestinationHandlingText = DescribeDestinationHandling(parcel.DestinationHandling),
                CheckInTime = parcel.CheckInTime,
                PutAwayTime = parcel.PutAwayTime,
                Inspection = check,
            };
        }

        /// <summary>Trạng thái kiện, viết theo cách Sale đọc xong là trả lời khách được ngay.</summary>
        private static string DescribeParcelJourney(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            "PENDING_CHECKIN" => "Chờ kho nước ngoài nhận",
            "RECEIVED" => "Kho nước ngoài đã nhận",
            "IN_WAREHOUSE" => "Đang nằm kho nước ngoài",
            "STORED" => "Đang lưu kho",
            "MERGED" => "Đã gộp kiện",
            "SPLIT" => "Đã tách kiện",
            "RELEASED" => "Đã xuất kho, chờ lên lô",
            "IN_TRANSIT" => "Đang trên đường về Việt Nam",
            "ARRIVED_DESTINATION" => "Đã về kho Việt Nam, chờ kiểm đếm",
            "RECEIVED_AT_DESTINATION" => "Kho Việt Nam đã kiểm đếm xong",
            "RETURNED_TO_WAREHOUSE" => "Giao không thành, đã hoàn về kho",
            "AWAITING_PICKUP" => "Chờ đơn vị giao tới lấy",
            "PICKING_UP" => "Đơn vị giao đang lấy hàng",
            "DELIVERED" => "Đã giao cho khách",
            _ => "Đang xử lý",
        };

        /// <summary>Nhãn trạng thái lô, dùng chung cách gọi với màn Vận chuyển của OM.</summary>
        private static string DescribeShipmentStatus(string? status) => status?.Trim().ToUpperInvariant() switch
        {
            "CREATED" or "LOT_CREATED" => "Lô mới tạo",
            "PREPARING" => "Đang chuẩn bị",
            "READY_TO_SHIP" => "Sẵn sàng xuất kho",
            "IN_TRANSIT" => "Đang vận chuyển về Việt Nam",
            "ARRIVED_VN" => "Đã về Việt Nam, chờ chuyển về kho",
            "ARRIVED" or "ARRIVED_DESTINATION" or "ARRIVED_IN_VN" => "Đã đến kho Việt Nam",

            // Nhãn của các mốc đã bỏ khỏi vòng đời, giữ để lô cũ trong DB vẫn hiện đúng chữ.
            "MANIFESTED" => "Đã lên manifest",
            "CUSTOMS_EXPORT_PENDING" => "Chờ thông quan xuất",
            "CUSTOMS_IMPORT_PENDING" => "Chờ thông quan nhập",
            "CUSTOMS_REJECTED" => "Hải quan từ chối",
            "HOLD" => "Đang tạm giữ",
            "ISSUE" => "Lô đang có sự cố",
            "CANCELLED" => "Lô đã huỷ",
            _ => "Đang xử lý",
        };

        private static string? DescribeParcelCondition(string? condition) =>
            condition?.Trim().ToUpperInvariant() switch
            {
                "INTACT" => "Nguyên vẹn",
                "MISSING" => "Thiếu hàng",
                "DAMAGED" => "Hư hỏng",
                "WET" => "Ẩm ướt",
                "SEAL_BROKEN" => "Rách niêm phong",
                _ => null,
            };

        /// <summary>Gộp chênh lệch thành một câu để Sale đọc lướt là hiểu phải báo khách gì.</summary>
        private static string SummarizeParcelDiscrepancy(int? quantityDiff, decimal? weightDiff, string? condition)
        {
            var parts = new List<string>();

            if (quantityDiff.HasValue && quantityDiff.Value != 0)
            {
                parts.Add(quantityDiff.Value < 0
                    ? $"Thiếu {Math.Abs(quantityDiff.Value)} kiện"
                    : $"Thừa {quantityDiff.Value} kiện");
            }

            // Cân điện tử luôn lệch vài chục gram; dưới ngưỡng này không đáng gọi là chênh lệch.
            if (weightDiff.HasValue && Math.Abs(weightDiff.Value) > 0.05m)
            {
                parts.Add(weightDiff.Value < 0
                    ? $"nhẹ hơn {Math.Abs(weightDiff.Value):0.###} kg"
                    : $"nặng hơn {weightDiff.Value:0.###} kg");
            }

            var normalized = condition?.Trim().ToUpperInvariant();
            if (normalized is "MISSING" or "DAMAGED" or "WET" or "SEAL_BROKEN")
            {
                var conditionText = DescribeParcelCondition(normalized);
                if (conditionText != null)
                {
                    parts.Add(conditionText.ToLowerInvariant());
                }
            }

            return parts.Count == 0 ? "Có ghi nhận bất thường" : string.Join(" · ", parts);
        }

        public async Task<PaginatedResponse<ConsignmentResponseDto>> GetConsignmentsAsync(Guid? userId, string role, int pageNumber, int pageSize, string? status, string? searchCode, string? orderType = null)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var customerRepo = _unitOfWork.Repository<Customer>();

            // Get all data into memory - suitable for GenericRepository pattern
            var allOrders = await orderRepo.GetAllAsync();
            var allCustomers = await customerRepo.GetAllAsync();
            var allItems = await _unitOfWork.Repository<OrderItem>().GetAllAsync();

            var query = allOrders.AsQueryable();

            var isStaff = StaffRoleHelper.IsInternalStaff(role);

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

            // Lọc theo loại đơn kho. Bỏ trống thì trả cả hai loại — màn kho cần thấy đủ, nên không
            // mặc định chỉ CONSIGNMENT, làm vậy sẽ nuốt mất đơn mua hộ khỏi màn đó.
            // Màn ký gửi phải truyền orderType=CONSIGNMENT, không thì đơn mua hộ (mã PUR-) hiện lẫn vào.
            if (!string.IsNullOrWhiteSpace(orderType))
            {
                var normalizedOrderType = orderType.Trim();
                query = query.Where(o => string.Equals(o.OrderType, normalizedOrderType, StringComparison.OrdinalIgnoreCase));
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
                    OrderType = o.OrderType,
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
                    DefaultDestinationHandling = o.DefaultDestinationHandling,
                    DefaultDestinationHandlingText = DescribeDestinationHandling(o.DefaultDestinationHandling),
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
                new() { Code = "QUOTATION_CONFIRMED", Label = "Khách đã chốt báo giá" },
                new() { Code = "DEPOSIT_PAID", Label = "Đã đặt cọc" },
                new() { Code = "CHECKED_IN", Label = "Đã nhập kho" },
                new() { Code = "WAREHOUSE_RECEIVED", Label = "Đã lưu kho" },
                new() { Code = "IN_TRANSIT", Label = "Đang vận chuyển" },
                new() { Code = "AT_DESTINATION_WAREHOUSE", Label = "Đã lưu kho tại kho đích" },
                new() { Code = "WAITING_FINAL_PAYMENT", Label = "Chờ thanh toán cuối" },
                new() { Code = "DELIVERED", Label = "Đã giao tới khách" },
                new() { Code = "CUSTOMER_CONFIRMED", Label = "Khách đã xác nhận nhận hàng" },
                new() { Code = "CANCELLED_FORFEITED", Label = "Huỷ do quá hạn thanh toán, mất cọc" },
                new() { Code = "REJECTED", Label = "Đã từ chối" },
                new() { Code = "QUOTATION_REJECTED", Label = "Báo giá bị từ chối" },
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
                .Where(p => ServicePricingHelper.IsShippingOption(p.ServiceType))
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
            var query = pricings.Where(p => ServicePricingHelper.IsShippingOption(p.ServiceType));

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
