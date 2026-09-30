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
    /// <summary>
    /// Đợt tất toán tính theo cân đo thực tế ở kho Việt Nam.
    /// <para>
    /// Báo giá ký gửi được lập từ số khách khai trước khi gửi hàng, nên cước chỉ là tạm tính. Số
    /// cân đo khi hàng về tới kho VN mới là số thật khách phải trả — trước đây số đó chỉ nằm
    /// trong biên bản kiểm kiện, còn đợt cuối vẫn thu theo báo giá cũ cộng phí Sale gõ tay.
    /// </para>
    /// </summary>
    public class VnSettlementService : IVnSettlementService
    {
        public const string FreightAdjustmentFeeType = "FREIGHT_ADJUSTMENT";
        public const string VatAdjustmentFeeType = "VAT_ADJUSTMENT";
        public const string TaxAdjustmentFeeType = PurchaseImportTax.AdjustmentFeeType;

        private static readonly HashSet<string> RetiredParcelStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "SPLIT", "MERGED" };

        private readonly IUnitOfWork _unitOfWork;
        private readonly IStorageFeeService _storageFeeService;
        private readonly IConfiguration _configuration;

        public VnSettlementService(IUnitOfWork unitOfWork, IStorageFeeService storageFeeService, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _storageFeeService = storageFeeService;
            _configuration = configuration;
        }

        public async Task<SettlementPreviewDto> PreviewAsync(Guid orderId)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn {orderId}.");

            var calc = await CalculateAsync(order);
            var preview = calc.Preview;

            var storage = await _storageFeeService.CalculateForOrderAsync(orderId);
            var billedStorage = calc.ExistingItems
                .Where(i => string.Equals(i.FeeType, "STORAGE", StringComparison.OrdinalIgnoreCase))
                .Sum(i => i.Amount ?? 0m);
            preview.StorageFee = storage.TotalAmount;

            var adjustmentDelta = preview.FreightAdjustment + preview.VatAdjustment + preview.ImportTaxAdjustment - calc.PreviousAdjustment;
            var storageDelta = storage.TotalAmount - billedStorage;
            var estimated = preview.InvoiceTotalBefore + adjustmentDelta + storageDelta - preview.DepositPaid;
            preview.EstimatedFinalAmount = estimated < 0m ? 0m : estimated;

            return preview;
        }

        public async Task EnsureCanIssueAsync(Guid orderId)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn {orderId}.");

            var calc = await CalculateAsync(order);
            var blocking = calc.Preview.Blockers
                .Where(b => b.Code is "PARCEL_NOT_INSPECTED" or "OPEN_INCIDENT")
                .ToList();

            if (blocking.Count > 0)
            {
                throw new ArgumentException(
                    "Chưa tất toán được: " + string.Join(" | ", blocking.Select(b => b.Message)));
            }
        }

        public async Task<decimal> ApplyFreightAdjustmentAsync(
            Guid orderId, Invoice invoice, List<InvoiceItem> existingItems, IGenericRepository<InvoiceItem> invoiceItemRepo)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn {orderId}.");

            var calc = await CalculateAsync(order);
            var p = calc.Preview;

            await UpsertAsync(existingItems, invoiceItemRepo, invoice, orderId, FreightAdjustmentFeeType, p.FreightAdjustment,
                $"Điều chỉnh cước theo cân đo tại kho VN: {p.VnChargeableWeight:0.###} kg × {p.FreightRate:#,##0}đ "
                + $"= {p.VnFreight:#,##0}đ (đã báo {p.QuotedFreight:#,##0}đ)");

            await UpsertAsync(existingItems, invoiceItemRepo, invoice, orderId, VatAdjustmentFeeType, p.VatAdjustment,
                $"VAT {p.VatRatePercent:G29}% trên phần điều chỉnh cước");

            // Mua hộ: không thu thuế NK phần hàng không tới tay khách (huỷ / thất lạc / NCC giao thiếu).
            await UpsertAsync(existingItems, invoiceItemRepo, invoice, orderId, TaxAdjustmentFeeType, p.ImportTaxAdjustment,
                $"Không thu thuế nhập khẩu phần hàng không giao: {p.ImportTaxAdjustmentNote}");

            return p.FreightAdjustment + p.VatAdjustment + p.ImportTaxAdjustment - calc.PreviousAdjustment;
        }

        private static async Task UpsertAsync(
            List<InvoiceItem> existingItems, IGenericRepository<InvoiceItem> repo, Invoice invoice, Guid orderId,
            string feeType, decimal amount, string description)
        {
            var existing = existingItems.FirstOrDefault(i => string.Equals(i.FeeType, feeType, StringComparison.OrdinalIgnoreCase));

            if (amount == 0m)
            {
                if (existing != null) repo.Delete(existing);
                return;
            }

            if (existing == null)
            {
                await repo.AddAsync(new InvoiceItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    FeeType = feeType,
                    ReferenceType = "ORDER",
                    ReferenceId = orderId,
                    Amount = amount,
                    Currency = "VND",
                    Description = description
                });
                return;
            }

            existing.Amount = amount;
            existing.Description = description;
            repo.Update(existing);
        }

        // ── Tính toán ──────────────────────────────────────────────────────────────────────

        private sealed class Calculation
        {
            public SettlementPreviewDto Preview { get; init; } = new();
            public List<InvoiceItem> ExistingItems { get; init; } = new();
            public decimal PreviousAdjustment { get; init; }
        }

        private async Task<Calculation> CalculateAsync(Order order)
        {
            var preview = new SettlementPreviewDto { OrderId = order.Id, ConsignmentCode = order.ConsignmentCode };

            var quotationIds = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == order.Id)
                .ToDictionary(q => q.Id);

            var invoice = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.ContainsKey(i.QuotationId.Value))
                .OrderByDescending(i => i.IssuedAt)
                .FirstOrDefault();

            var existingItems = invoice == null
                ? new List<InvoiceItem>()
                : (await _unitOfWork.Repository<InvoiceItem>().GetAllAsync()).Where(i => i.InvoiceId == invoice.Id).ToList();

            var payments = invoice == null
                ? new List<Payment>()
                : (await _unitOfWork.Repository<Payment>().GetAllAsync()).Where(pm => pm.InvoiceId == invoice.Id).ToList();

            if (invoice == null)
                preview.Blockers.Add(new SettlementBlockerDto { Code = "NO_INVOICE", Message = "Đơn chưa có hoá đơn (chưa chấp nhận báo giá)." });

            preview.InvoiceTotalBefore = invoice?.TotalAmount ?? 0m;
            preview.DepositPaid = payments.Where(pm => IsInstallment(pm, "DEPOSIT") && IsPaid(pm)).Sum(pm => pm.Amount ?? 0m);

            if (invoice != null && preview.DepositPaid <= 0m && !payments.Any(pm => IsInstallment(pm, "DEPOSIT") && IsPaid(pm)))
                preview.Blockers.Add(new SettlementBlockerDto { Code = "NO_DEPOSIT", Message = "Đơn chưa thanh toán cọc." });

            if (payments.Any(pm => IsInstallment(pm, "FINAL_PAYMENT") && IsPaid(pm)))
                preview.Blockers.Add(new SettlementBlockerDto { Code = "ALREADY_SETTLED", Message = "Đơn đã tất toán." });

            // Đơn giá cước và % VAT lấy đúng từ báo giá khách đã chấp nhận, không đọc lại bảng giá
            // hiện hành — giá có thể đã đổi từ lúc báo.
            var fees = invoice?.QuotationId != null
                ? (await _unitOfWork.Repository<QuotationFee>().GetAllAsync()).Where(f => f.QuotationId == invoice.QuotationId).ToList()
                : new List<QuotationFee>();

            var mainFees = fees.Where(f => string.Equals(f.FeeType, "MAIN_SERVICE", StringComparison.OrdinalIgnoreCase)).ToList();
            preview.QuotedFreight = mainFees.Sum(f => f.Amount);
            preview.FreightRate = mainFees
                .Where(f => string.Equals(f.CalculationType, "PER_KG", StringComparison.OrdinalIgnoreCase) && f.Value > 0m)
                .Select(f => f.Value)
                .FirstOrDefault();

            preview.VatRatePercent = fees
                .Where(f => string.Equals(f.FeeType, "TAX", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(f.CalculationType, "PERCENTAGE", StringComparison.OrdinalIgnoreCase)
                            && (f.FeeName ?? string.Empty).Contains("VAT", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Value)
                .FirstOrDefault();

            var quotation = invoice?.QuotationId != null && quotationIds.TryGetValue(invoice.QuotationId.Value, out var q) ? q : null;
            var rules = (await _unitOfWork.Repository<PricingRule>().GetAllAsync())
                .Where(r => string.IsNullOrWhiteSpace(r.Status) || string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();
            preview.VolumetricDivisor = VolumetricDivisorResolver.Resolve(rules, _configuration, quotation?.ServicePricingId);
            preview.MinimumWeight = ResolveMinimumWeight(rules, quotation?.ServicePricingId);

            // Cân đo VN: biên bản kiểm kiện mới nhất của từng kiện.
            var parcels = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(pc => pc.ConsignmentId == order.Id && !RetiredParcelStatuses.Contains(pc.PackageStatus?.Trim() ?? string.Empty))
                .OrderBy(pc => pc.PackageCode)
                .ToList();
            var parcelIds = parcels.Select(pc => pc.Id).ToHashSet();
            var inspections = (await _unitOfWork.Repository<ParcelInspection>().GetAllAsync())
                .Where(i => parcelIds.Contains(i.ParcelId))
                .GroupBy(i => i.ParcelId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.InspectedAt ?? DateTime.MinValue).First());
            var arrivalPhotos = await VnArrivalPhotos.LoadAsync(_unitOfWork, AttachmentEntityTypes.Parcel, parcelIds);

            // Kiện "đã qua kho VN": có biên bản kiểm ở VN, hoặc trạng thái chỉ có ở VN. Kiện kho cũ
            // đã nhận trước khi bắt buộc cân đo (không có biên bản) vẫn được tính là đã qua kho.
            var vnWarehouseIds = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .Where(w => w.Region == VCL_DLL.Enums.RegionCode.VN)
                .Select(w => w.Id)
                .ToHashSet();
            var vnBinIds = (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.WarehouseId.HasValue && vnWarehouseIds.Contains(b.WarehouseId.Value))
                .Select(b => b.Id)
                .ToHashSet();

            var notInspected = new List<string>();
            var missingVnMeasurement = 0;
            decimal totalChargeable = 0m;
            var activeCount = 0;

            foreach (var parcel in parcels)
            {
                var disposed = string.Equals(parcel.PackageStatus, VnParcelStatuses.Disposed, StringComparison.OrdinalIgnoreCase);
                inspections.TryGetValue(parcel.Id, out var ins);

                var weight = ins?.ActualWeight ?? parcel.ActualWeight ?? 0m;
                var length = ins?.Length ?? parcel.Length ?? 0m;
                var width = ins?.Width ?? parcel.Width ?? 0m;
                var height = ins?.Height ?? parcel.Height ?? 0m;
                var volumetric = preview.VolumetricDivisor > 0m ? Math.Round(length * width * height / preview.VolumetricDivisor, 4) : 0m;
                var chargeable = Math.Max(weight, volumetric);

                if (!disposed && ins == null && !IsPastVnReceiving(parcel, vnBinIds)) notInspected.Add(parcel.PackageCode);
                if (!disposed && (ins?.ActualWeight is not > 0m || ins.Length is not > 0m || ins.Width is not > 0m || ins.Height is not > 0m))
                    missingVnMeasurement++;

                if (!disposed)
                {
                    totalChargeable += chargeable;
                    activeCount++;
                }

                preview.Parcels.Add(new SettlementParcelDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode,
                    PackageStatus = parcel.PackageStatus,
                    IsDisposed = disposed,
                    OriginWeight = parcel.ActualWeight,
                    VnWeight = ins?.ActualWeight,
                    VnLength = ins?.Length,
                    VnWidth = ins?.Width,
                    VnHeight = ins?.Height,
                    VolumetricWeight = volumetric,
                    ChargeableWeight = disposed ? 0m : chargeable,
                    ArrivalPhotos = VnArrivalPhotos.Of(arrivalPhotos, parcel.Id)
                });
            }

            if (parcels.Count == 0)
                preview.Blockers.Add(new SettlementBlockerDto { Code = "PARCEL_NOT_INSPECTED", Message = "Đơn chưa có kiện nào về kho." });
            else if (notInspected.Count > 0)
                preview.Blockers.Add(new SettlementBlockerDto
                {
                    Code = "PARCEL_NOT_INSPECTED",
                    Message = $"Kho Việt Nam chưa kiểm {notInspected.Count} kiện: {string.Join(", ", notInspected)}."
                });

            var openIncidents = (await _unitOfWork.Repository<ParcelIncident>().GetAllAsync())
                .Where(i => i.OrderId == order.Id
                            && string.Equals(i.Stage, IncidentStages.Receiving, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(i.Status, IncidentStatuses.Resolved, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (openIncidents.Count > 0)
                preview.Blockers.Add(new SettlementBlockerDto
                {
                    Code = "OPEN_INCIDENT",
                    Message = $"Còn {openIncidents.Count} sự cố chưa xử lý xong: {string.Join(", ", openIncidents.Select(i => i.IncidentCode))}."
                });

            preview.VnChargeableWeight = activeCount == 0 ? 0m : Math.Max(totalChargeable, preview.MinimumWeight);

            // Chỉ tính lại khi MỌI kiện còn hiệu lực đều có cân đo VN. Đơn kho nhận theo cách cũ
            // (không cân đo) giữ nguyên cước đã báo — lấy số kho nguồn thế vào là tự đổi giá của
            // khách giữa chừng.
            if (missingVnMeasurement > 0)
            {
                preview.VnFreight = preview.QuotedFreight;
                preview.AdjustmentNote = $"{missingVnMeasurement} kiện chưa có cân đo tại kho VN (nhận theo quy trình cũ) — giữ nguyên cước đã báo.";
            }
            // Báo giá không có dòng cước theo kg (đơn cũ / đơn nhập tay) thì không tự điều chỉnh
            // được — giữ nguyên cước đã báo thay vì đoán đơn giá.
            else if (preview.FreightRate > 0m)
            {
                preview.VnFreight = Math.Round(preview.VnChargeableWeight * preview.FreightRate, 0);
                preview.FreightAdjustment = preview.VnFreight - preview.QuotedFreight;
                preview.VatAdjustment = Math.Round(preview.FreightAdjustment * preview.VatRatePercent / 100m, 0);
            }
            else
            {
                preview.VnFreight = preview.QuotedFreight;
                preview.AdjustmentNote = "Báo giá không có dòng cước theo kg — giữ nguyên cước đã báo.";
            }

            if (order.PurchaseOrderId.HasValue && invoice != null)
            {
                var (taxAdjustment, taxNote) = await CalculateImportTaxAdjustmentAsync(order, parcels);
                preview.ImportTaxAdjustment = taxAdjustment;
                preview.ImportTaxAdjustmentNote = taxNote;
            }

            var previousAdjustment = existingItems
                .Where(i => string.Equals(i.FeeType, FreightAdjustmentFeeType, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(i.FeeType, VatAdjustmentFeeType, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(i.FeeType, TaxAdjustmentFeeType, StringComparison.OrdinalIgnoreCase))
                .Sum(i => i.Amount ?? 0m);

            preview.CanIssue = preview.Blockers.Count == 0;

            return new Calculation { Preview = preview, ExistingItems = existingItems, PreviousAdjustment = previousAdjustment };
        }

        /// <summary>
        /// Thuế NK KHÔNG thu của đơn kho mua hộ (số âm): phần NCC giao thiếu (đã ghi ở khoản hoàn,
        /// chưa trả bằng tiền) + phần hàng trong kiện bị huỷ / thất lạc, chia theo số kiện của từng dòng.
        /// </summary>
        private async Task<(decimal Adjustment, string? Note)> CalculateImportTaxAdjustmentAsync(Order order, List<Parcel> parcels)
        {
            var orderItems = (await _unitOfWork.Repository<OrderItem>().GetAllAsync())
                .Where(i => i.OrderId == order.Id)
                .ToList();
            if (orderItems.Count == 0) return (0m, null);

            var taxByItem = await PurchaseImportTax.ByOrderItemAsync(_unitOfWork, order, orderItems);
            var poItems = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => i.PurchaseOrderId == order.PurchaseOrderId)
                .ToList();

            var liveRefundIds = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseRequestId == order.PurchaseRequestId && PurchasePaymentTypes.IsRefund(p)
                            && !string.Equals(p.Status, PurchaseRefundStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToHashSet();
            var shortLines = (await _unitOfWork.Repository<PurchaseRefundLine>().GetAllAsync())
                .Where(l => liveRefundIds.Contains(l.PurchasePaymentId) && l.PurchaseOrderId == order.PurchaseOrderId
                            && string.Equals(l.ReasonCode, PurchaseRefundReasons.SupplierShort, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var notes = new List<string>();
            decimal total = 0m;

            var shortQtyByOrderItem = new Dictionary<Guid, int>();
            foreach (var line in shortLines)
            {
                var poItem = poItems.FirstOrDefault(i => i.PurchaseRequestItemId == line.PurchaseRequestItemId);
                var orderItem = poItem == null ? null : PurchaseImportTax.MatchOrderItem(orderItems, poItem);
                if (orderItem != null)
                    shortQtyByOrderItem[orderItem.Id] = shortQtyByOrderItem.GetValueOrDefault(orderItem.Id) + line.Quantity;
                if (!line.ImportTaxInRefund && line.ImportTaxAdjustment > 0m)
                {
                    total += line.ImportTaxAdjustment;
                    notes.Add($"{line.ProductName}: NCC giao thiếu {line.Quantity} → {line.ImportTaxAdjustment:#,##0}đ");
                }
            }

            var linkedAny = parcels.Any(p => p.OrderItemId.HasValue);
            foreach (var item in orderItems)
            {
                var tax = taxByItem.GetValueOrDefault(item.Id);
                if (tax <= 0m) continue;

                var qty = Math.Max(item.Quantity ?? 1, 1);
                var shortQty = Math.Min(shortQtyByOrderItem.GetValueOrDefault(item.Id), qty);
                var deliverableTax = tax * (qty - shortQty) / qty;

                var ofItem = linkedAny
                    ? parcels.Where(p => p.OrderItemId == item.Id).ToList()
                    : parcels;
                if (ofItem.Count == 0) continue;
                var lost = ofItem.Count(p => PurchaseImportTax.NotDeliveredParcelStatuses.Contains(p.PackageStatus?.Trim() ?? string.Empty));
                if (lost == 0) continue;

                var part = Math.Round(deliverableTax * lost / ofItem.Count, 0);
                if (part <= 0m) continue;
                total += part;
                notes.Add($"{item.ProductName}: {lost}/{ofItem.Count} kiện huỷ/thất lạc → {part:#,##0}đ");
            }

            return total <= 0m ? (0m, null) : (-total, string.Join("; ", notes));
        }

        private static readonly HashSet<string> VnOnlyStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            VnParcelStatuses.ReceivedAtDestination, VnParcelStatuses.Quarantined, VnParcelStatuses.Delivered,
            VnParcelStatuses.Disposed, VnParcelStatuses.DeliveryFailed, VnParcelStatuses.Returning, VnParcelStatuses.Returned,
            "AWAITING_PICKUP", "PICKING_UP", "PICKED_UP", "OUT_FOR_DELIVERY", "PARTIALLY_DELIVERED", "DELIVERY_DELAYED"
        };

        private static bool IsPastVnReceiving(Parcel parcel, HashSet<Guid> vnBinIds) =>
            VnOnlyStatuses.Contains(parcel.PackageStatus?.Trim() ?? string.Empty)
            || (parcel.BinId.HasValue && vnBinIds.Contains(parcel.BinId.Value));

        private static decimal ResolveMinimumWeight(List<PricingRule> rules, Guid? servicePricingId)
        {
            var rule = rules.FirstOrDefault(r =>
                (r.ServicePricingId == null || r.ServicePricingId == servicePricingId)
                && (string.Equals(r.ConditionType, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.RuleType, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.RuleCode, "MIN_WEIGHT", StringComparison.OrdinalIgnoreCase)));

            if (rule == null) return 1.0m;
            if (rule.Value > 0m) return rule.Value;
            return decimal.TryParse(rule.ConditionValue, out var v) && v > 0m ? v : 1.0m;
        }

        private static bool IsInstallment(Payment p, string type) =>
            string.Equals(p.InstallmentType, type, StringComparison.OrdinalIgnoreCase);

        private static bool IsPaid(Payment p) =>
            string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase);
    }
}
