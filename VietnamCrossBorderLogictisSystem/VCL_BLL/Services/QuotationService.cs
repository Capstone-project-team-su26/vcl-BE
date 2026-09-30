using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class QuotationService : IQuotationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;
        private readonly INotificationService _notificationService;

        // Báo giá có hiệu lực 7 ngày
        private const int QuotationValidDays = 7;

        public QuotationService(IUnitOfWork unitOfWork, IConfiguration configuration, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
            _notificationService = notificationService;
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> RejectQuotationByOrderIdAsync(Guid orderId, string reason, Guid actorId, string actorRole)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var isStaff = await EnsureCanActOnOrderAsync(order, actorId, actorRole, "từ chối báo giá của");
            EnsureMayRejectQuotation(isStaff, actorRole);

            // Chỉ từ chối được báo giá đang chờ khách — không phải bản đã chấp nhận hay còn chờ duyệt giá.
            var latestQuotation = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId
                            && string.Equals(q.Status, QuotationAcceptanceRules.StatusPending, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault()
                ?? throw new ArgumentException("Đơn không có báo giá nào đang chờ khách xác nhận để từ chối.");

            return await RejectAsync(order, latestQuotation, reason, actorId);
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> RejectQuotationByIdAsync(Guid quotationId, string reason, Guid actorId, string actorRole)
        {
            var quotation = await _unitOfWork.Repository<Quotation>().GetByIdAsync(quotationId)
                ?? throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(quotation.OrderId)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng liên quan đến báo giá.");

            var isStaff = await EnsureCanActOnOrderAsync(order, actorId, actorRole, "từ chối báo giá của");
            EnsureMayRejectQuotation(isStaff, actorRole);

            if (!string.Equals(quotation.Status, QuotationAcceptanceRules.StatusPending, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Chỉ từ chối được báo giá đang chờ khách xác nhận (hiện tại: {StatusNames.Quote(quotation.Status)}).");

            return await RejectAsync(order, quotation, reason, actorId);
        }

        /// <summary>
        /// Chỉ khách (chủ đơn) hoặc Admin từ chối báo giá. Sale từ chối thay khách rồi lập lại là đi vòng
        /// qua luật "báo giá đã tới tay khách thì không lập đè".
        /// </summary>
        private static void EnsureMayRejectQuotation(bool isStaff, string actorRole)
        {
            if (isStaff && !WarehouseApprovalPolicy.IsAdmin(actorRole))
                throw new UnauthorizedAccessException(
                    "Chỉ khách hàng hoặc Admin được từ chối báo giá. Sale không từ chối thay khách — khách tự từ chối trên ứng dụng, hoặc nhờ Admin.");
        }

        private async Task<QuotationActionResponse> RejectAsync(Order order, Quotation quotation, string reason, Guid actorId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Vui lòng ghi lý do từ chối báo giá.");

            var statusBefore = order.Status;

            quotation.Status = QuotationAcceptanceRules.StatusRejected;
            _unitOfWork.Repository<Quotation>().Update(quotation);

            order.Status = "QUOTATION_REJECTED";
            order.Note = $"Từ chối báo giá: {reason}";
            _unitOfWork.Repository<Order>().Update(order);

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, statusBefore, order.Status,
                "QUOTATION_REJECTED", $"Báo giá {quotation.Id} bị từ chối: {reason}", actorId);

            await _unitOfWork.SaveChangesAsync();

            return new QuotationActionResponse
            {
                Message = "Đã từ chối báo giá.",
                Status = "QUOTATION_REJECTED",
                RejectionReason = reason,
                Consignment = new ConsignmentResponseDto
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode,
                    Status = order.Status,
                    ConsignmentType = order.ConsignmentType,
                    TotalVolume = order.TotalVolume,
                    TotalWeight = order.TotalWeight,
                    Route = order.Route,
                    CreatedAt = order.CreatedAt,
                    WarehouseId = quotation.WarehouseId
                }
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationDetailResponse> GetQuotationByOrderIdAsync(Guid orderId, Guid actorId, string actorRole)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var isStaff = await EnsureCanActOnOrderAsync(order, actorId, actorRole, "xem báo giá của");

            var quotation = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId)
                .Where(q => isStaff || !QuotationAcceptanceRules.IsHiddenFromCustomer(q))
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (quotation == null)
                throw new NotFoundException($"Chưa có báo giá nào được tạo cho đơn hàng: {orderId}");

            return await BuildDetailAsync(order, quotation, isStaff);
        }

        /// <inheritdoc/>
        public async Task<QuotationDetailResponse> GetQuotationByIdAsync(Guid quotationId, Guid actorId, string actorRole)
        {
            var quotation = await _unitOfWork.Repository<Quotation>().GetByIdAsync(quotationId);
            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(quotation.OrderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng liên quan đến báo giá: {quotation.OrderId}");

            var isStaff = await EnsureCanActOnOrderAsync(order, actorId, actorRole, "xem báo giá của");
            if (!isStaff && QuotationAcceptanceRules.IsHiddenFromCustomer(quotation))
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            return await BuildDetailAsync(order, quotation, isStaff);
        }

        /// <summary>
        /// Nhân viên nội bộ thao tác được mọi đơn; khách chỉ đơn của chính mình.
        /// </summary>
        /// <returns>true nếu người gọi là nhân viên nội bộ.</returns>
        private async Task<bool> EnsureCanActOnOrderAsync(Order order, Guid actorId, string actorRole, string action)
        {
            if (StaffRoleHelper.IsInternalStaff(actorRole)) return true;

            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .FirstOrDefault(c => c.UserId == actorId);

            if (customer == null || order.CustomerId != customer.Id)
                throw new UnauthorizedAccessException($"Bạn không có quyền {action} đơn hàng này.");

            return false;
        }

        private async Task<QuotationDetailResponse> BuildDetailAsync(Order order, Quotation quotation, bool isStaff = false)
        {
            // Màn lập báo giá của Sale đọc cờ này để khoá form — cùng luật với SendQuotationAsync.
            QuotationAcceptanceRules.RequoteDecision? requote = null;
            if (isStaff)
            {
                var orderQuotations = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                    .Where(q => q.OrderId == order.Id);
                requote = QuotationAcceptanceRules.EvaluateRequote(order, orderQuotations, DateTime.UtcNow);
            }

            var quotationFees = (await _unitOfWork.Repository<QuotationFee>().GetAllAsync())
                .Where(f => f.QuotationId == quotation.Id)
                .ToList();

            var activeRules = (await _unitOfWork.Repository<PricingRule>().GetAllAsync())
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var divisor = GetVolumetricDivisor(activeRules, quotation.ServicePricingId);
            var volumetricWeight = (order.TotalVolume ?? 0m) / divisor;

            var matchedPricing = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                .FirstOrDefault(p => p.Id == quotation.ServicePricingId);
            var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);

            var domesticShippingFee = quotationFees
                .Where(f => string.Equals(f.FeeType, "DOMESTIC_FEE", StringComparison.OrdinalIgnoreCase))
                .Sum(f => f.Amount);

            var totalEstimatedCost = (quotation.EstimatedFreightCharge ?? 0m) +
                                     (quotation.ServiceFee ?? 0m) +
                                     (quotation.TaxAndDuty ?? 0m);

            var ruleMap = activeRules.ToDictionary(r => r.Id, r => r);

            // Chi tiết cước từng kiện đọc lại từ dòng phí đã lưu lúc báo giá, không tính lại theo
            // bảng giá hiện tại — đổi giá sau này không được làm đổi báo giá khách đã nhận.
            var parcelFeeById = quotationFees
                .Where(f => f.ParcelId.HasValue && string.Equals(f.FeeType, "MAIN_SERVICE", StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f.ParcelId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => p.ConsignmentId == order.Id
                            && !string.Equals(p.PackageStatus, "SPLIT", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(p.PackageStatus, "MERGED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var parcelDetails = new List<ParcelQuotationDetailDto>();
            foreach (var p in parcels)
            {
                var pVol = (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m);
                var pVolWeight = Math.Round(pVol / divisor, 4);
                parcelFeeById.TryGetValue(p.Id, out var savedFee);
                var pChargeable = savedFee?.ChargeableWeight ?? Math.Max(p.ActualWeight ?? 0m, pVolWeight);

                parcelDetails.Add(new ParcelQuotationDetailDto
                {
                    ParcelId = p.Id,
                    PackageCode = p.PackageCode ?? string.Empty,
                    ActualWeight = p.ActualWeight ?? 0m,
                    Length = p.Length ?? 0m,
                    Width = p.Width ?? 0m,
                    Height = p.Height ?? 0m,
                    VolumetricWeight = pVolWeight,
                    ChargeableWeight = pChargeable,
                    ShippingFee = savedFee?.Amount ?? 0m
                });
            }

            var packageCount = parcels.Any() ? parcels.Count : 1;
            var orderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == order.Id)
                .ToList();
            var itemNames = orderItems.ToDictionary(i => i.Id, i => i.ProductName ?? string.Empty);
            var feeDtos = quotationFees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FeeType, "PACKING_FEE", StringComparison.OrdinalIgnoreCase))
                .Select(f => MapToFeeResponseDto(f, ruleMap, packageCount, itemNames))
                .ToList();

            return new QuotationDetailResponse
            {
                QuotationId = quotation.Id,
                OrderId = order.Id,
                QuoteType = quotation.QuoteType ?? string.Empty,
                Status = quotation.Status ?? string.Empty,
                ConsignmentType = order.ConsignmentType ?? string.Empty,
                ConsignmentCode = order.ConsignmentCode,
                TotalWeight = order.TotalWeight ?? 0m,
                TotalVolume = order.TotalVolume ?? 0m,
                VolumetricWeight = volumetricWeight,
                ChargeableWeight = Math.Max(order.TotalWeight ?? 0m, Math.Max(volumetricWeight, minimumWeight)),
                EstimatedFreightCharge = quotation.EstimatedFreightCharge ?? 0m,
                ServiceFee = Math.Max(0m, (quotation.ServiceFee ?? 0m) - domesticShippingFee),
                DomesticShippingFee = domesticShippingFee,
                TaxAndDuty = quotation.TaxAndDuty ?? 0m,
                Vat = quotation.Vat ?? 0m,
                ImportTax = quotation.ImportTax ?? 0m,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                ExpiredAt = quotation.ExpiredAt,
                SalesNote = quotation.SalesNote,
                WarehouseId = quotation.WarehouseId,
                AdditionalFees = feeDtos,
                // Cùng hàm dựng với màn xem trước → hai màn không thể lệch thứ tự hay lệch dòng.
                InvoiceLines = BuildInvoiceLines(quotationFees, ruleMap, orderItems),
                Parcels = parcelDetails,
                PriceApprovalStatus = quotation.PriceApprovalStatus,
                OverrideReason = quotation.OverrideReason,
                AcceptedAt = quotation.AcceptedAt,
                CanCustomerAccept = CanCustomerAccept(quotation),
                CanSalesRequote = requote?.Allowed,
                RequoteState = requote?.State,
                RequoteBlockedReason = requote?.Reason
            };
        }

        private bool CanCustomerAccept(Quotation quotation)
        {
            try
            {
                QuotationAcceptanceRules.EnsureCustomerCanAccept(quotation);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
