using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class QuotationService : IQuotationService
    {
        private readonly IUnitOfWork _unitOfWork;

        // Hệ số chia thể tích để tính volumetric weight (đơn vị: cm → kg)
        private const decimal VolumetricDivisor = 5000m;

        // Tỷ lệ phí dịch vụ (5% tổng declared value)
        private const decimal ServiceFeeRate = 0.05m;

        // Tỷ lệ thuế & duty (10% tổng declared value)
        private const decimal TaxAndDutyRate = 0.10m;

        // Giá mặc định khi không tìm thấy ServicePricing (VND/kg)
        private const decimal DefaultExpressPrice = 50000m;
        private const decimal DefaultConsolidationPrice = 30000m;

        // Thời hạn báo giá (ngày)
        private const int QuotationExpiryDays = 7;

        public QuotationService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        /// <inheritdoc/>
        public async Task<EstimateQuotationResponseDto> CreateEstimateAsync(Guid orderId)
        {
            // 1. Tìm Order
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);

            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            // 2. Lấy danh sách OrderItem
            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var allItems = await orderItemRepo.GetAllAsync();
            var items = allItems.Where(i => i.OrderId == orderId).ToList();

            if (!items.Any())
                throw new InvalidOperationException("Đơn hàng chưa có sản phẩm nào. Vui lòng thêm sản phẩm trước khi tạo báo giá.");

            // 3. Tính toán trọng lượng và thể tích
            decimal totalActualWeight = 0m;
            decimal totalVolumetricWeight = 0m;
            decimal totalVolume = 0m;
            decimal totalDeclaredValue = 0m;

            var itemDetails = new List<QuotationItemDetailDto>();

            foreach (var item in items)
            {
                var weight = item.Weight ?? 0m;
                var length = item.Length ?? 0m;
                var width = item.Width ?? 0m;
                var height = item.Height ?? 0m;
                var declaredValue = item.DeclaredValue ?? 0m;

                var volume = length * width * height;
                var volumetricWeight = VolumetricDivisor > 0 ? volume / VolumetricDivisor : 0m;

                totalActualWeight += weight;
                totalVolumetricWeight += volumetricWeight;
                totalVolume += volume;
                totalDeclaredValue += declaredValue;

                itemDetails.Add(new QuotationItemDetailDto
                {
                    ItemId = item.Id,
                    ProductName = item.ProductName,
                    Weight = weight,
                    VolumetricWeight = Math.Round(volumetricWeight, 2),
                    DeclaredValue = declaredValue
                });
            }

            // Chargeable weight = max(actual, volumetric)
            var chargeableWeight = Math.Max(totalActualWeight, totalVolumetricWeight);

            // 4. Lookup giá dịch vụ từ ServicePricing
            var pricePerKg = await GetPricePerKgAsync(order.ConsignmentType);

            // 5. Tính chi phí
            var estimatedFreightCharge = Math.Round(chargeableWeight * pricePerKg, 2);
            var serviceFee = Math.Round(totalDeclaredValue * ServiceFeeRate, 2);
            var taxAndDuty = Math.Round(totalDeclaredValue * TaxAndDutyRate, 2);
            var totalEstimatedCost = estimatedFreightCharge + serviceFee + taxAndDuty;

            // 6. Tạo Quotation entity
            var now = DateTime.UtcNow;
            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                QuoteType = "ESTIMATE",
                Status = "DRAFT",
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                TotalVolume = Math.Round(totalVolume, 2),
                CreatedAt = now,
                ExpiredAt = now.AddDays(QuotationExpiryDays)
            };

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            await quotationRepo.AddAsync(quotation);

            // Cập nhật TotalWeight và TotalVolume trên Order
            order.TotalWeight = Math.Round(totalActualWeight, 2);
            order.TotalVolume = Math.Round(totalVolume, 2);
            orderRepo.Update(order);

            await _unitOfWork.SaveChangesAsync();

            // 7. Trả response
            return new EstimateQuotationResponseDto
            {
                QuotationId = quotation.Id,
                OrderId = orderId,
                ConsignmentType = order.ConsignmentType,
                QuoteType = quotation.QuoteType,
                Status = quotation.Status,
                TotalActualWeight = Math.Round(totalActualWeight, 2),
                TotalVolumetricWeight = Math.Round(totalVolumetricWeight, 2),
                ChargeableWeight = Math.Round(chargeableWeight, 2),
                TotalVolume = Math.Round(totalVolume, 2),
                TotalDeclaredValue = Math.Round(totalDeclaredValue, 2),
                EstimatedFreightCharge = estimatedFreightCharge,
                ServiceFee = serviceFee,
                TaxAndDuty = taxAndDuty,
                TotalEstimatedCost = Math.Round(totalEstimatedCost, 2),
                Currency = "VND",
                ItemCount = items.Count,
                CreatedAt = now,
                ExpiredAt = quotation.ExpiredAt,
                Items = itemDetails
            };
        }

        /// <summary>
        /// Tra cứu giá vận chuyển (VND/kg) từ bảng ServicePricing theo loại ký gửi.
        /// Nếu không tìm thấy, dùng giá mặc định.
        /// </summary>
        private async Task<decimal> GetPricePerKgAsync(string consignmentType)
        {
            var pricingRepo = _unitOfWork.Repository<ServicePricing>();
            var allPricings = await pricingRepo.GetAllAsync();

            // Tìm pricing phù hợp (ServiceType match ConsignmentType, còn hiệu lực)
            var pricing = allPricings
                .Where(p => string.Equals(p.ServiceType, consignmentType, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveDate == null || p.EffectiveDate <= DateTime.UtcNow)
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault();

            if (pricing?.Price != null)
                return pricing.Price.Value;

            // Giá mặc định
            return string.Equals(consignmentType, "Express", StringComparison.OrdinalIgnoreCase)
                ? DefaultExpressPrice
                : DefaultConsolidationPrice;
        }
    }
}
