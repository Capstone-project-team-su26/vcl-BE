using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public partial class QuotationService : IQuotationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IConfiguration _configuration;

        // Giá mặc định khi không tìm thấy ServicePricing trong DB (VND/kg)
        private const decimal DefaultExpressRate = 50000m;
        private const decimal DefaultStandardRate = 30000m;

        // Phí dịch vụ cố định (VND)
        private const decimal ExpressServiceFee = 50000m;
        private const decimal StandardServiceFee = 30000m;

        // Tỷ lệ thuế tạm tính trên tổng declared value
        private const decimal TaxRate = 0.10m;

        // Báo giá tạm tính có hiệu lực 7 ngày
        private const int QuotationValidDays = 7;

        public QuotationService(IUnitOfWork unitOfWork, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _configuration = configuration;
        }

        /// <inheritdoc/>
        public async Task<AcceptQuotationResponse> AcceptQuotationAsync(Guid quotationId, Guid userId)
        {
            // 1. Tìm Quotation
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);

            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            // 2. Kiểm tra trạng thái — DRAFT hoặc PENDING mới được accept
            if (string.Equals(quotation.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Báo giá này đã được xác nhận trước đó.");

            if (string.Equals(quotation.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Báo giá này đã bị hủy, không thể xác nhận.");

            if (!string.Equals(quotation.Status, "DRAFT", StringComparison.OrdinalIgnoreCase) && 
                !string.Equals(quotation.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {quotation.Status}");

            // 3. Kiểm tra hết hạn
            if (quotation.ExpiredAt.HasValue && quotation.ExpiredAt.Value < DateTime.UtcNow)
                throw new ArgumentException("Báo giá đã hết hạn. Vui lòng tạo báo giá mới.");

            // 4. Kiểm tra Quotation thuộc đúng khách đang đăng nhập
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);

            if (order == null)
                throw new NotFoundException("Không tìm thấy đơn hàng liên quan đến báo giá.");

            // Tra ngược: Order.CustomerId → Customer.UserId == userId
            var customerRepo = _unitOfWork.Repository<Customer>();
            var allCustomers = await customerRepo.GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.Id == order.CustomerId);

            if (customer == null || customer.UserId != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận báo giá này.");

            // 5. Cập nhật trạng thái
            var now = DateTime.UtcNow;

            quotation.Status = "ACCEPTED";
            quotationRepo.Update(quotation);

            order.Status = "QUOTATION_CONFIRMED";
            orderRepo.Update(order);

            await _unitOfWork.SaveChangesAsync();

            // 6. Trả về response
            return new AcceptQuotationResponse
            {
                QuotationId = quotation.Id,
                OrderId = order.Id,
                QuotationStatus = quotation.Status,
                OrderStatus = order.Status,
                AcceptedAt = now
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> RejectQuotationByOrderIdAsync(Guid orderId, string reason)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            order.Status = "QUOTATION_REJECTED";
            order.Note = $"Từ chối báo giá: {reason}";
            orderRepo.Update(order);

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var latestQuotation = allQuotations
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (latestQuotation != null)
            {
                latestQuotation.Status = "REJECTED";
                quotationRepo.Update(latestQuotation);
            }

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
                    CreatedAt = order.CreatedAt
                }
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationActionResponse> RejectQuotationByIdAsync(Guid quotationId, string reason)
        {
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);
            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            quotation.Status = "REJECTED";
            quotationRepo.Update(quotation);

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);
            if (order != null)
            {
                order.Status = "QUOTATION_REJECTED";
                order.Note = $"Từ chối báo giá: {reason}";
                orderRepo.Update(order);
            }

            await _unitOfWork.SaveChangesAsync();

            return new QuotationActionResponse
            {
                Message = "Đã từ chối báo giá.",
                Status = "QUOTATION_REJECTED",
                RejectionReason = reason,
                Consignment = order != null ? new ConsignmentResponseDto
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode,
                    Status = order.Status,
                    ConsignmentType = order.ConsignmentType,
                    TotalVolume = order.TotalVolume,
                    TotalWeight = order.TotalWeight,
                    Route = order.Route,
                    CreatedAt = order.CreatedAt
                } : null
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationDetailResponse> GetQuotationByOrderIdAsync(Guid orderId)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var quotation = allQuotations
                .Where(q => q.OrderId == orderId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (quotation == null)
                throw new NotFoundException($"Chưa có báo giá nào được tạo cho đơn hàng: {orderId}");

            // Lấy danh sách phí phụ trội từ DB
            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            var allFees = await feeRepo.GetAllAsync();
            var quotationFees = allFees.Where(f => f.QuotationId == quotation.Id).ToList();

            // Tính cân nặng quy đổi thể tích (Volumetric Weight)
            var ruleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await ruleRepo.GetAllAsync();
            var activeRules = allRules.Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)).ToList();

            var divisor = GetVolumetricDivisor(activeRules);
            var volumetricWeight = (order.TotalVolume ?? 0m) / divisor;

            // Tìm Minimum Weight
            var matchedPricing = _unitOfWork.Repository<ServicePricing>().GetAllAsync().Result
                .FirstOrDefault(p => p.Id == quotation.ServicePricingId);
            var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);

            var totalEstimatedCost = (quotation.EstimatedFreightCharge ?? 0m) +
                                     (quotation.ServiceFee ?? 0m) +
                                     (quotation.TaxAndDuty ?? 0m);

            var ruleMap = activeRules.ToDictionary(r => r.Id, r => r);

            // Fetch parcels for detail list
            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcels = (await parcelRepo.GetAllAsync())
                .Where(p => p.ConsignmentId == orderId)
                .ToList();

            var parcelDetails = new List<ParcelQuotationDetailDto>();
            foreach (var p in parcels)
            {
                var pVol = (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m);
                var pVolWeight = Math.Round(pVol / divisor, 4);
                var pChargeable = Math.Max(p.ActualWeight ?? 0m, pVolWeight);
                var pRate = matchedPricing?.Price ?? (IsExpressServiceType(order.ConsignmentType ?? "Express") ? DefaultExpressRate : DefaultStandardRate);

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
                    ShippingFee = Math.Round(pChargeable * pRate, 2)
                });
            }

            var packageCount = parcels.Any() ? parcels.Count : 1;
            var feeDtos = quotationFees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase))
                .Select(f => MapToFeeResponseDto(f, ruleMap, packageCount))
                .ToList();

            return new QuotationDetailResponse
            {
                QuotationId = quotation.Id,
                OrderId = orderId,
                QuoteType = quotation.QuoteType ?? string.Empty,
                Status = quotation.Status ?? string.Empty,
                ConsignmentType = order.ConsignmentType ?? string.Empty,
                ConsignmentCode = order.ConsignmentCode,
                TotalWeight = order.TotalWeight ?? 0m,
                TotalVolume = order.TotalVolume ?? 0m,
                VolumetricWeight = volumetricWeight,
                ChargeableWeight = Math.Max(order.TotalWeight ?? 0m, Math.Max(volumetricWeight, minimumWeight)),
                EstimatedFreightCharge = quotation.EstimatedFreightCharge ?? 0m,
                ServiceFee = quotation.ServiceFee ?? 0m,
                TaxAndDuty = quotation.TaxAndDuty ?? 0m,
                Vat = quotation.Vat ?? 0m,
                ImportTax = quotation.ImportTax ?? 0m,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                ExpiredAt = quotation.ExpiredAt,
                SalesNote = quotation.SalesNote,
                AdditionalFees = feeDtos,
                Parcels = parcelDetails
            };
        }

        /// <inheritdoc/>
        public async Task<QuotationDetailResponse> GetQuotationByIdAsync(Guid quotationId)
        {
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);
            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng liên quan đến báo giá: {quotation.OrderId}");

            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            var allFees = await feeRepo.GetAllAsync();
            var quotationFees = allFees.Where(f => f.QuotationId == quotationId).ToList();

            var ruleRepo = _unitOfWork.Repository<PricingRule>();
            var allRules = await ruleRepo.GetAllAsync();
            var activeRules = allRules.Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)).ToList();

            var divisor = GetVolumetricDivisor(activeRules);
            var volumetricWeight = (order.TotalVolume ?? 0m) / divisor;

            var matchedPricing = _unitOfWork.Repository<ServicePricing>().GetAllAsync().Result
                .FirstOrDefault(p => p.Id == quotation.ServicePricingId);
            var minimumWeight = GetMinimumWeight(activeRules, matchedPricing);

            var totalEstimatedCost = (quotation.EstimatedFreightCharge ?? 0m) +
                                     (quotation.ServiceFee ?? 0m) +
                                     (quotation.TaxAndDuty ?? 0m);

            var ruleMap = activeRules.ToDictionary(r => r.Id, r => r);

            var parcelRepo = _unitOfWork.Repository<Parcel>();
            var parcels = (await parcelRepo.GetAllAsync())
                .Where(p => p.ConsignmentId == order.Id)
                .ToList();

            var parcelDetails = new List<ParcelQuotationDetailDto>();
            foreach (var p in parcels)
            {
                var pVol = (p.Length ?? 0m) * (p.Width ?? 0m) * (p.Height ?? 0m);
                var pVolWeight = Math.Round(pVol / divisor, 4);
                var pChargeable = Math.Max(p.ActualWeight ?? 0m, pVolWeight);
                var pRate = matchedPricing?.Price ?? (IsExpressServiceType(order.ConsignmentType ?? "Express") ? DefaultExpressRate : DefaultStandardRate);

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
                    ShippingFee = Math.Round(pChargeable * pRate, 2)
                });
            }

            var packageCount = parcels.Any() ? parcels.Count : 1;
            var feeDtos = quotationFees
                .Where(f => string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase))
                .Select(f => MapToFeeResponseDto(f, ruleMap, packageCount))
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
                ServiceFee = quotation.ServiceFee ?? 0m,
                TaxAndDuty = quotation.TaxAndDuty ?? 0m,
                Vat = quotation.Vat ?? 0m,
                ImportTax = quotation.ImportTax ?? 0m,
                TotalEstimatedCost = totalEstimatedCost,
                CreatedAt = quotation.CreatedAt ?? DateTime.UtcNow,
                ExpiredAt = quotation.ExpiredAt,
                SalesNote = quotation.SalesNote,
                AdditionalFees = feeDtos,
                Parcels = parcelDetails
            };
        }
    }
}
