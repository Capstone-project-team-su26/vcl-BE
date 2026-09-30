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
    /// <summary>
    /// Đơn mua NCC của luồng mua hộ (docs/migrate-source-cu/08-luong-mua-ho.md mục 6):
    /// Sale lập đơn (NCC trong danh mục, kho nhận, SL, giá thực) → giá vượt báo giá quá ngưỡng thì
    /// khách xem + trả phần chênh → Admin duyệt → Sale đặt NCC (mã đơn + bằng chứng) → hệ thống
    /// sinh đơn kho PURCHASE + phiếu tiếp nhận ACTIVE → Sale cập nhật tiến độ NCC.
    /// </summary>
    public partial class PurchaseOrderService : IPurchaseOrderService
    {
        private const decimal DefaultPriceToleranceRate = 5m;
        private const string PriceToleranceFeeCode = "PURCHASE_PRICE_TOLERANCE_RATE";

        /*
         * Phí huỷ đơn mua SAU khi đã đặt nhà cung cấp, tính trên tiền hàng đã báo.
         *
         * Mặc định 0: hệ thống KHÔNG tự trừ tiền của khách khi chưa ai cấu hình con số.
         * Muốn thu thì Admin thêm dòng phí PURCHASE_CANCEL_FEE_RATE trong bảng phí,
         * giống hệt cách ngưỡng lệch giá 5% đang làm — không sửa mã nguồn.
         */
        private const decimal DefaultCancelFeeRate = 0m;
        private const string CancelFeeCode = "PURCHASE_CANCEL_FEE_RATE";

        /// <summary>Yêu cầu ở các trạng thái này mới lập đơn mua (đời cũ: Sale đã bấm tay tiến độ nhưng chưa có đơn kho).</summary>
        private static readonly HashSet<string> OrderableRequestStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "PAID", "DEPOSIT_PAID", "PURCHASING", "PURCHASED", "SELLER_SHIPPED", "ARRIVED_ORIGIN_WAREHOUSE", "WAITING_STORED" };

        private const string LegacyAdjustmentFeeType = "PREPAID_ADJUSTMENT";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IExchangeRateService _exchangeRateService;
        private readonly IPurchasePaymentService _purchasePaymentService;
        private readonly INotificationService _notificationService;

        public PurchaseOrderService(
            IUnitOfWork unitOfWork,
            IExchangeRateService exchangeRateService,
            IPurchasePaymentService purchasePaymentService,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _exchangeRateService = exchangeRateService;
            _purchasePaymentService = purchasePaymentService;
            _notificationService = notificationService;
        }

        // ── Lập / sửa ────────────────────────────────────────────────────────────────────

        public async Task<PurchaseOrderResponseDto> CreateAsync(Guid requestId, Guid userId, string userRole, SavePurchaseOrderDto dto)
        {
            EnsureBuyer(userRole);
            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");

            await EnsureRequestOrderableAsync(purchaseRequest);
            var quotation = await GetAcceptedQuotationAsync(requestId)
                ?? throw new ArgumentException("Yêu cầu chưa có báo giá được khách chấp nhận.");

            var existing = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(o => o.PurchaseRequestId == requestId)
                .ToList();

            var po = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                PurchaseQuotationId = quotation.Id,
                SequenceNo = (existing.Max(o => o.SequenceNo) ?? 0) + 1,
                Status = PurchaseOrderStatuses.Draft,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            po.PurchaseOrderCode = $"PO-{purchaseRequest.PurchaseCode}-{po.SequenceNo}";

            await ApplyContentAsync(po, purchaseRequest, quotation, dto, isNew: true);
            await _unitOfWork.Repository<PurchaseOrder>().AddAsync(po);
            await _unitOfWork.SaveChangesAsync();

            return await BuildDtoAsync(po, forCustomer: false);
        }

        public async Task<PurchaseOrderResponseDto> UpdateAsync(Guid purchaseOrderId, Guid userId, string userRole, SavePurchaseOrderDto dto)
        {
            EnsureBuyer(userRole);
            var po = await GetPoAsync(purchaseOrderId);
            if (!PurchaseOrderStatuses.Editable.Contains(po.Status ?? string.Empty))
                throw new ArgumentException($"Chỉ sửa được đơn mua ở trạng thái nháp hoặc bị từ chối (hiện tại: {PurchaseOrderStatuses.Text(po.Status)}).");

            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId)
                ?? throw new NotFoundException("Không tìm thấy yêu cầu mua hộ của đơn mua.");
            var quotation = await GetAcceptedQuotationAsync(po.PurchaseRequestId)
                ?? throw new ArgumentException("Yêu cầu chưa có báo giá được khách chấp nhận.");

            var itemRepo = _unitOfWork.Repository<PurchaseOrderItem>();
            foreach (var old in (await itemRepo.GetAllAsync()).Where(i => i.PurchaseOrderId == po.Id).ToList())
                itemRepo.Delete(old);

            await ApplyContentAsync(po, purchaseRequest, quotation, dto, isNew: false);
            po.Status = PurchaseOrderStatuses.Draft;
            po.PriceDifferenceAmount = null;
            po.UpdatedBy = userId;
            po.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Repository<PurchaseOrder>().Update(po);
            await _unitOfWork.SaveChangesAsync();

            return await BuildDtoAsync(po, forCustomer: false);
        }

        /// <summary>Ghi NCC, kho, tỷ giá và các dòng hàng vào đơn (chưa lưu).</summary>
        private async Task ApplyContentAsync(
            PurchaseOrder po, PurchaseRequest purchaseRequest, PurchaseQuotation quotation, SavePurchaseOrderDto dto, bool isNew)
        {
            if (dto == null) throw new ArgumentException("Dữ liệu đơn mua không được để trống.");
            if (dto.Items == null || dto.Items.Count == 0)
                throw new ArgumentException("Đơn mua phải có ít nhất một sản phẩm.");

            var supplier = dto.SupplierId.HasValue
                ? await _unitOfWork.Repository<Supplier>().GetByIdAsync(dto.SupplierId.Value)
                : null;
            if (supplier == null) throw new ArgumentException("Phải chọn nhà cung cấp có trong danh mục.");
            if (!supplier.IsActive) throw new ArgumentException($"Nhà cung cấp {supplier.SupplierName} đang ngừng hoạt động.");

            var warehouseId = dto.WarehouseId ?? purchaseRequest.ExpectedWarehouseId
                ?? throw new ArgumentException("Phải chọn kho nguồn nhận hàng.");
            var warehouse = await _unitOfWork.Repository<Warehouse>().GetByIdAsync(warehouseId)
                ?? throw new NotFoundException("Không tìm thấy kho nhận hàng.");
            if (!warehouse.IsActive || !string.Equals(warehouse.WarehouseType?.Trim(), "ORIGIN", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Kho nhận hàng phải là kho nguồn (ORIGIN) đang hoạt động.");

            var currency = string.IsNullOrWhiteSpace(dto.Currency) ? "VND" : dto.Currency.Trim().ToUpperInvariant();
            var rate = currency == "VND" ? 1m : await _exchangeRateService.GetRateToVndAsync(currency);

            var requestItems = (await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync())
                .Where(i => i.PurchaseRequestId == purchaseRequest.Id)
                .ToDictionary(i => i.Id);
            var quotedPrice = (await _unitOfWork.Repository<PurchaseQuotationItem>().GetAllAsync())
                .Where(qi => qi.PurchaseQuotationId == quotation.Id)
                .GroupBy(qi => qi.PurchaseRequestItemId)
                .ToDictionary(g => g.Key, g => g.First().UnitPrice);

            // Mỗi sản phẩm chỉ nằm trong một đơn mua còn hiệu lực.
            var otherActivePoIds = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(o => o.PurchaseRequestId == purchaseRequest.Id && o.Id != po.Id && PurchaseOrderStatuses.IsActive(o))
                .Select(o => o.Id)
                .ToHashSet();
            var takenItemIds = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => otherActivePoIds.Contains(i.PurchaseOrderId))
                .Select(i => i.PurchaseRequestItemId)
                .ToHashSet();

            if (dto.Items.Select(i => i.PurchaseRequestItemId).Distinct().Count() != dto.Items.Count)
                throw new ArgumentException("Một sản phẩm chỉ được xuất hiện một lần trong đơn mua.");

            // Phần đã đóng "không mua được" (đã hoàn tiền cho khách) thì không mua lại được nữa.
            var closedQty = await ClosedUnfulfilledQtyAsync(purchaseRequest.Id);

            decimal total = 0m, quotedTotal = 0m;
            var itemRepo = _unitOfWork.Repository<PurchaseOrderItem>();
            foreach (var line in dto.Items)
            {
                if (!requestItems.TryGetValue(line.PurchaseRequestItemId, out var reqItem))
                    throw new ArgumentException($"Sản phẩm {line.PurchaseRequestItemId} không thuộc yêu cầu mua hộ này.");
                if (takenItemIds.Contains(reqItem.Id))
                    throw new ArgumentException($"Sản phẩm '{reqItem.ProductName}' đã nằm trong một đơn mua khác còn hiệu lực.");
                if (line.Quantity <= 0 || line.Quantity > reqItem.Quantity)
                    throw new ArgumentException($"Số lượng mua '{reqItem.ProductName}' phải từ 1 đến {reqItem.Quantity} (số khách đặt).");
                var closed = closedQty.GetValueOrDefault(reqItem.Id);
                if (closed > 0 && line.Quantity > reqItem.Quantity - closed)
                    throw new ArgumentException(
                        $"'{reqItem.ProductName}' đã đóng {closed} sản phẩm không mua được (đã hoàn tiền) — chỉ còn mua được tối đa {Math.Max(reqItem.Quantity - closed, 0)}.");
                if (line.UnitPrice < 0)
                    throw new ArgumentException("Đơn giá không được âm.");
                if (!quotedPrice.TryGetValue(reqItem.Id, out var quotedUnit))
                    throw new ArgumentException($"Sản phẩm '{reqItem.ProductName}' không có trong báo giá khách đã chấp nhận.");

                var unitVnd = currency == "VND" ? line.UnitPrice : Math.Round(line.UnitPrice * rate, 0, MidpointRounding.AwayFromZero);
                var lineTotal = unitVnd * line.Quantity;
                total += lineTotal;
                quotedTotal += quotedUnit * line.Quantity;

                await itemRepo.AddAsync(new PurchaseOrderItem
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = po.Id,
                    PurchaseRequestItemId = reqItem.Id,
                    ProductName = reqItem.ProductName,
                    Quantity = line.Quantity,
                    UnitPrice = unitVnd,
                    UnitPriceOriginal = currency == "VND" ? null : line.UnitPrice,
                    QuotedUnitPrice = quotedUnit,
                    LineTotal = lineTotal
                });
            }

            po.SupplierId = supplier.Id;
            po.SupplierName = supplier.SupplierName;
            po.WarehouseId = warehouse.Id;
            po.Currency = currency;
            po.ExchangeRate = rate;
            po.PurchaseNote = string.IsNullOrWhiteSpace(dto.PurchaseNote) ? null : dto.PurchaseNote.Trim();
            po.TotalAmount = total;
            po.QuotedGoodsAmount = quotedTotal;
        }

        // ── Gửi duyệt / khách xem giá chênh / Admin duyệt ────────────────────────────────

        public async Task<PurchaseOrderResponseDto> SubmitAsync(Guid purchaseOrderId, Guid userId, string userRole)
        {
            EnsureBuyer(userRole);
            var po = await GetPoAsync(purchaseOrderId);
            if (!PurchaseOrderStatuses.Editable.Contains(po.Status ?? string.Empty))
                throw new ArgumentException($"Chỉ gửi duyệt được đơn nháp hoặc bị từ chối (hiện tại: {PurchaseOrderStatuses.Text(po.Status)}).");

            var hasItems = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync()).Any(i => i.PurchaseOrderId == po.Id);
            if (!hasItems) throw new ArgumentException("Đơn mua chưa có sản phẩm.");

            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId)
                ?? throw new NotFoundException("Không tìm thấy yêu cầu mua hộ.");
            await EnsureRequestOrderableAsync(purchaseRequest);

            var tolerance = await GetPriceToleranceRateAsync();
            var quoted = po.QuotedGoodsAmount ?? 0m;
            var exceeds = po.TotalAmount > Math.Round(quoted * (1m + tolerance / 100m), 0);

            po.SubmittedAt = DateTime.UtcNow;
            po.SubmittedBy = userId;
            po.UpdatedAt = DateTime.UtcNow;
            po.UpdatedBy = userId;
            po.DecisionNote = null;

            if (exceeds)
            {
                // Giá thực vượt giá báo quá ngưỡng: khách xem và trả phần chênh trước khi gửi Admin.
                po.PriceDifferenceAmount = po.TotalAmount - quoted;
                po.Status = PurchaseOrderStatuses.AwaitingCustomer;
                po.CustomerRejectReason = null;
            }
            else
            {
                po.PriceDifferenceAmount = 0m;
                po.Status = PurchaseOrderStatuses.PendingApproval;
            }

            /*
             * Giá mua THỰC thấp hơn giá đã báo.
             *
             * Khách trả trước 100% theo giá báo, nên phần thừa là tiền của khách. Trước đây
             * nhánh này chỉ đặt PriceDifferenceAmount = 0 rồi đi tiếp, tiền thừa nằm im.
             *
             * Không chặn luồng: đơn vẫn gửi Admin duyệt bình thường, chỉ sinh thêm một khoản
             * PHẢI HOÀN đang chờ để kế toán trả lại. Gửi duyệt lại thì số tiền được cập nhật
             * chứ không đẻ thêm dòng (xem UpsertRefundAsync).
             *
             * Mốc 1.000đ: chênh vài đồng do làm tròn tỷ giá thì không đáng một lần chuyển khoản.
             */
            var overpaid = quoted - po.TotalAmount;
            if (!exceeds && overpaid >= 1000m && po.PurchaseQuotationId.HasValue)
            {
                await _purchasePaymentService.UpsertRefundAsync(
                    purchaseRequest, po, PurchasePaymentTypes.RefundPriceDifference,
                    await BuildPriceDiffRefundLinesAsync(po), userId);
            }
            else
            {
                // Gửi lại với giá không còn thấp hơn giá báo: khoản hoàn chênh giá cũ đang chờ không còn đúng.
                await CancelPendingRefundsAsync(po.Id, PurchasePaymentTypes.RefundPriceDifference);
            }

            _unitOfWork.Repository<PurchaseOrder>().Update(po);
            await _unitOfWork.SaveChangesAsync();

            if (!exceeds && overpaid >= 1000m)
                await NotifyCustomerAsync(purchaseRequest, "Hoàn lại phần chênh giá mua",
                    $"Giá mua thực tế của đơn {po.PurchaseOrderCode} (yêu cầu {purchaseRequest.PurchaseCode}) THẤP hơn giá đã báo "
                    + $"{overpaid:#,##0}đ. Chúng tôi sẽ hoàn lại phần này cho bạn.");

            if (exceeds)
                await NotifyCustomerAsync(purchaseRequest, "Giá mua thực tế thay đổi",
                    $"Giá mua thực tế của đơn {po.PurchaseOrderCode} (yêu cầu {purchaseRequest.PurchaseCode}) cao hơn giá đã báo "
                    + $"{po.PriceDifferenceAmount:#,##0}đ. Vui lòng xem và xác nhận để chúng tôi tiếp tục đặt hàng.");
            else
                await _notificationService.NotifyRolesAsync(new[] { "Admin" }, "Đơn mua NCC chờ duyệt",
                    $"Đơn mua {po.PurchaseOrderCode} ({po.TotalAmount:#,##0}đ, NCC {po.SupplierName}) đang chờ duyệt.", userId);

            return await BuildDtoAsync(po, forCustomer: false);
        }

        public async Task<PurchaseOrderResponseDto> CustomerDecisionAsync(
            Guid purchaseOrderId, Guid userId, string userRole, PurchaseOrderCustomerDecisionDto dto)
        {
            dto ??= new PurchaseOrderCustomerDecisionDto();
            var po = await GetPoAsync(purchaseOrderId);
            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId)
                ?? throw new NotFoundException("Không tìm thấy yêu cầu mua hộ.");
            await EnsureOwnerAsync(purchaseRequest, userId);

            var awaitingDecision = PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.AwaitingCustomer);
            var awaitingPayment = PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.AwaitingCustomerPayment);
            if (!awaitingDecision && !(awaitingPayment && dto.Accept))
                throw new ArgumentException($"Đơn mua không chờ khách xác nhận giá (hiện tại: {PurchaseOrderStatuses.Text(po.Status)}).");

            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var pending = (await paymentRepo.GetAllAsync())
                .Where(p => p.PurchaseOrderId == po.Id
                            && PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.PriceDifference
                            && string.Equals(p.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (dto.Accept)
            {
                // Trả lại link mới (đổi phương thức / link hết hạn): huỷ khoản đang chờ rồi tạo khoản mới.
                foreach (var p in pending)
                {
                    p.Status = "CANCELLED";
                    paymentRepo.Update(p);
                }

                await _purchasePaymentService.CreatePriceDifferencePaymentAsync(
                    purchaseRequest, po, po.PriceDifferenceAmount ?? 0m, dto.PaymentMethod, dto.ReturnUrl, dto.CancelUrl);
                po.Status = PurchaseOrderStatuses.AwaitingCustomerPayment;
                po.CustomerDecisionAt = DateTime.UtcNow;
                po.CustomerRejectReason = null;
            }
            else
            {
                var reason = dto.Reason?.Trim();
                if (string.IsNullOrWhiteSpace(reason))
                    throw new ArgumentException("Vui lòng nhập lý do không đồng ý giá mới.");
                po.Status = PurchaseOrderStatuses.Draft;
                po.CustomerDecisionAt = DateTime.UtcNow;
                po.CustomerRejectReason = reason;
            }

            po.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Repository<PurchaseOrder>().Update(po);
            await _unitOfWork.SaveChangesAsync();

            if (!dto.Accept)
                await _notificationService.NotifyRolesAsync(new[] { "Sale", "Sales" }, "Khách không đồng ý giá mua mới",
                    $"Khách không đồng ý giá mới của đơn mua {po.PurchaseOrderCode}: {po.CustomerRejectReason}", userId);

            return await BuildDtoAsync(po, forCustomer: true);
        }

        public async Task<PurchaseOrderResponseDto> DecideAsync(Guid purchaseOrderId, Guid userId, string userRole, PurchaseOrderDecisionDto dto)
        {
            if (!IsAdmin(userRole))
                throw new UnauthorizedAccessException("Chỉ Admin được duyệt đơn mua NCC.");
            dto ??= new PurchaseOrderDecisionDto();

            var po = await GetPoAsync(purchaseOrderId);
            if (!PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.PendingApproval))
                throw new ArgumentException($"Đơn mua không ở trạng thái chờ duyệt (hiện tại: {PurchaseOrderStatuses.Text(po.Status)}).");

            if (dto.Approve)
            {
                var payments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                    .Where(p => p.PurchaseRequestId == po.PurchaseRequestId && PurchasePaymentTypes.IsPaid(p))
                    .ToList();
                if (!payments.Any(PurchasePaymentTypes.IsFirstInstalment))
                    throw new ArgumentException("Khách chưa thanh toán phần trả trước của yêu cầu mua hộ.");
                if ((po.PriceDifferenceAmount ?? 0m) > 0m
                    && !payments.Any(p => p.PurchaseOrderId == po.Id && PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.PriceDifference))
                    throw new ArgumentException("Khách chưa trả phần chênh giá của đơn mua này.");

                var supplier = po.SupplierId.HasValue ? await _unitOfWork.Repository<Supplier>().GetByIdAsync(po.SupplierId.Value) : null;
                if (supplier == null || !supplier.IsActive)
                    throw new ArgumentException("Nhà cung cấp của đơn mua không còn hoạt động.");

                po.Status = PurchaseOrderStatuses.Approved;
                po.DecisionNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            }
            else
            {
                var note = dto.Note?.Trim();
                if (string.IsNullOrWhiteSpace(note))
                    throw new ArgumentException("Phải ghi lý do từ chối đơn mua.");
                po.Status = PurchaseOrderStatuses.Rejected;
                po.DecisionNote = note;
            }

            po.DecidedAt = DateTime.UtcNow;
            po.DecidedBy = userId;
            po.UpdatedAt = DateTime.UtcNow;
            po.UpdatedBy = userId;
            _unitOfWork.Repository<PurchaseOrder>().Update(po);
            await _unitOfWork.SaveChangesAsync();

            await NotifyUserAsync(po.CreatedBy,
                dto.Approve ? "Đơn mua NCC đã được duyệt" : "Đơn mua NCC bị từ chối",
                dto.Approve
                    ? $"Đơn mua {po.PurchaseOrderCode} đã được duyệt — có thể đặt hàng với NCC."
                    : $"Đơn mua {po.PurchaseOrderCode} bị từ chối: {po.DecisionNote}");

            return await BuildDtoAsync(po, forCustomer: false);
        }

        // ── Đặt NCC và tiến độ NCC ───────────────────────────────────────────────────────

        public async Task<PurchaseOrderResponseDto> PlaceAsync(Guid purchaseOrderId, Guid userId, string userRole, PlacePurchaseOrderDto dto)
        {
            EnsureBuyer(userRole);
            var po = await GetPoAsync(purchaseOrderId);
            if (!PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.Approved))
                throw new ArgumentException($"Chỉ đặt NCC được đơn mua đã duyệt (hiện tại: {PurchaseOrderStatuses.Text(po.Status)}).");

            var supplierOrderCode = dto?.SupplierOrderCode?.Trim();
            if (string.IsNullOrWhiteSpace(supplierOrderCode))
                throw new ArgumentException("Phải nhập mã đơn bên NCC.");

            var proofCount = await CountProofsAsync(po.Id);
            if (proofCount == 0)
                throw new ArgumentException("Phải tải ảnh chứng từ đặt hàng (chứng từ mua hộ) lên đơn mua trước khi xác nhận đặt NCC.");

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(po.PurchaseRequestId)
                ?? throw new NotFoundException("Không tìm thấy yêu cầu mua hộ.");

            var now = DateTime.UtcNow;
            po.Status = PurchaseOrderStatuses.Ordered;
            po.SupplierOrderCode = supplierOrderCode;
            po.OrderedAt = now;
            po.OrderedBy = userId;
            po.StatusNote = string.IsNullOrWhiteSpace(dto!.Note) ? po.StatusNote : dto.Note.Trim();
            po.UpdatedAt = now;
            po.UpdatedBy = userId;
            _unitOfWork.Repository<PurchaseOrder>().Update(po);

            var (order, note) = await HandOffToWarehouseAsync(po, purchaseRequest, userId);

            if (!string.Equals(purchaseRequest.Status, "PURCHASING", StringComparison.OrdinalIgnoreCase))
            {
                await AddRequestHistoryAsync(purchaseRequest, "PURCHASING", $"Đặt NCC đơn {po.PurchaseOrderCode} (mã NCC {supplierOrderCode}).", userId);
                purchaseRequest.Status = "PURCHASING";
                requestRepo.Update(purchaseRequest);
            }

            await _unitOfWork.SaveChangesAsync();

            await NotifyCustomerAsync(purchaseRequest, "Đã đặt hàng với nhà cung cấp",
                $"Yêu cầu mua hộ {purchaseRequest.PurchaseCode}: đã đặt hàng NCC cho đơn kho {order.ConsignmentCode}. "
                + "Chúng tôi sẽ báo khi NCC xác nhận và phát hàng.");
            await _notificationService.NotifyRolesAsync(new[] { "WarehouseStaff", "OperationsManager" }, "Hàng mua hộ sắp về kho",
                $"Đơn kho {order.ConsignmentCode} (phiếu {note.ReceivingNoteCode}) — hàng mua hộ từ NCC {po.SupplierName} sẽ về kho.", userId);

            return await BuildDtoAsync(po, forCustomer: false);
        }

        public async Task<PurchaseOrderResponseDto> UpdateProgressAsync(Guid purchaseOrderId, Guid userId, string userRole, PurchaseOrderProgressDto dto)
        {
            EnsureBuyer(userRole);
            if (dto == null) throw new ArgumentException("Dữ liệu cập nhật không được để trống.");
            var po = await GetPoAsync(purchaseOrderId);
            var target = dto.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var now = DateTime.UtcNow;

            if (target == PurchaseOrderStatuses.SupplierConfirmed)
            {
                if (!PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.Ordered))
                    throw new ArgumentException($"NCC chỉ xác nhận được đơn đã đặt (hiện tại: {PurchaseOrderStatuses.Text(po.Status)}).");
                po.SupplierConfirmedAt = now;
            }
            else if (target == PurchaseOrderStatuses.SupplierShipped)
            {
                if (!PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.Ordered)
                    && !PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.SupplierConfirmed))
                    throw new ArgumentException($"Chỉ ghi NCC phát hàng cho đơn đã đặt / đã xác nhận (hiện tại: {PurchaseOrderStatuses.Text(po.Status)}).");
                var tracking = dto.DomesticTrackingCode?.Trim();
                if (string.IsNullOrWhiteSpace(tracking))
                    throw new ArgumentException("Phải nhập mã vận đơn nội địa khi NCC phát hàng.");
                po.SupplierConfirmedAt ??= now;
                po.SupplierShippedAt = now;
                po.DomesticTrackingCode = tracking;
                po.DomesticCarrier = string.IsNullOrWhiteSpace(dto.DomesticCarrier) ? null : dto.DomesticCarrier.Trim();

                // Kho nhìn mã vận đơn nội địa trên từng dòng hàng khi nhận.
                var order = await FindWarehouseOrderAsync(po.Id);
                if (order != null)
                {
                    var orderItemRepo = _unitOfWork.Repository<OrderItem>();
                    foreach (var item in (await orderItemRepo.GetAllAsync()).Where(i => i.OrderId == order.Id))
                    {
                        item.DomesticTrackingCode = tracking;
                        orderItemRepo.Update(item);
                    }
                }
            }
            else
            {
                throw new ArgumentException($"Trạng thái không hợp lệ. Chỉ chuyển được sang {StatusNames.QuoteAny("SUPPLIER_CONFIRMED", "SUPPLIER_SHIPPED")}.");
            }

            po.Status = target;
            po.StatusNote = string.IsNullOrWhiteSpace(dto.Note) ? po.StatusNote : dto.Note.Trim();
            po.UpdatedAt = now;
            po.UpdatedBy = userId;
            _unitOfWork.Repository<PurchaseOrder>().Update(po);
            await _unitOfWork.SaveChangesAsync();

            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId);
            if (purchaseRequest != null)
            {
                var warehouseOrder = await FindWarehouseOrderAsync(po.Id);
                var code = warehouseOrder?.ConsignmentCode ?? po.PurchaseOrderCode;
                await NotifyCustomerAsync(purchaseRequest,
                    target == PurchaseOrderStatuses.SupplierShipped ? "Nhà cung cấp đã phát hàng" : "Nhà cung cấp đã xác nhận đơn",
                    target == PurchaseOrderStatuses.SupplierShipped
                        ? $"Đơn {code}: NCC đã phát hàng tới kho (mã vận đơn {po.DomesticTrackingCode})."
                        : $"Đơn {code}: NCC đã xác nhận đơn hàng.");
            }

            return await BuildDtoAsync(po, forCustomer: false);
        }

        public async Task<PurchaseOrderResponseDto> CancelAsync(Guid purchaseOrderId, Guid userId, string userRole, CancelPurchaseOrderDto dto)
        {
            EnsureBuyer(userRole);
            var reason = dto?.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Phải ghi lý do huỷ đơn mua.");

            var po = await GetPoAsync(purchaseOrderId);
            if (PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.Cancelled))
                throw new ArgumentException("Đơn mua đã huỷ.");

            var placed = PurchaseOrderStatuses.Placed.Contains(po.Status ?? string.Empty);
            if (placed)
            {
                if (!IsAdmin(userRole))
                    throw new UnauthorizedAccessException("Đơn đã đặt NCC — chỉ Admin được huỷ.");

                var order = await FindWarehouseOrderAsync(po.Id);
                if (order != null)
                {
                    var hasParcel = (await _unitOfWork.Repository<Parcel>().GetAllAsync()).Any(p => p.ConsignmentId == order.Id);
                    if (hasParcel)
                        throw new ArgumentException($"Kho đã nhận kiện của đơn {order.ConsignmentCode} — không huỷ được, xử lý qua sự cố / hoàn hàng.");

                    await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, order.Status, "CANCELLED",
                        "PURCHASE_ORDER_CANCELLED", $"Huỷ đơn mua {po.PurchaseOrderCode}: {reason}", userId);
                    order.Status = "CANCELLED";
                    _unitOfWork.Repository<Order>().Update(order);

                    // Hoá đơn chặng VN (cước + VAT + thuế NK) của đơn kho không còn gì để thu.
                    await VoidWarehouseBillingAsync(order);

                    var noteRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
                    foreach (var note in (await noteRepo.GetAllAsync()).Where(n => n.ConsignmentOrderId == order.Id))
                    {
                        note.Status = "CANCELLED";
                        noteRepo.Update(note);
                    }
                }
            }

            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            foreach (var p in (await paymentRepo.GetAllAsync())
                         .Where(p => p.PurchaseOrderId == po.Id && string.Equals(p.Status, "PENDING", StringComparison.OrdinalIgnoreCase)))
            {
                p.Status = "CANCELLED";
                paymentRepo.Update(p);
            }

            /*
             * CHÍNH SÁCH HUỶ ĐƠN MUA NCC
             *
             * Chưa đặt NCC (nháp / chờ duyệt / bị từ chối): chưa có đồng nào ra khỏi công ty,
             *   tiền trả trước vẫn nằm ở yêu cầu và dùng cho đơn mua kế tiếp → không hoàn, không phí.
             *   Riêng phần CHÊNH GIÁ khách đã trả thêm cho đơn này thì trả lại (không còn dùng vào đâu).
             *
             * Đã đặt NCC (ORDERED trở đi): chỉ Admin huỷ được, bắt buộc ghi lý do (chặn ở trên).
             *   - Khách huỷ (mặc định): hoàn tiền hàng KHÁCH THỰC TRẢ (giá báo + chênh giá đã trả,
             *     trừ phần chênh đã được hoàn) − phí huỷ; GIỮ phí mua hộ + VAT phí.
             *   - NCC huỷ / hết hàng (cause = SUPPLIER): hoàn đủ tiền hàng thực trả + phí mua hộ +
             *     VAT phí tương ứng, không phí huỷ.
             *   Phí huỷ lấy từ bảng phí (PURCHASE_CANCEL_FEE_RATE), chưa cấu hình thì bằng 0.
             *   Từng thành phần ghi ở PURCHASE_REFUND_LINES để đọc lại luôn khớp với số đã chốt.
             *   Hoá đơn chặng VN của đơn kho (cước + VAT + thuế NK) huỷ theo.
             */
            var requestForRefund = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId);
            if (requestForRefund != null && po.PurchaseQuotationId.HasValue)
            {
                var supplierFault = string.Equals(dto?.Cause?.Trim(), "SUPPLIER", StringComparison.OrdinalIgnoreCase);
                var cancelLines = placed
                    ? await BuildCancellationRefundLinesAsync(po, supplierFault, reason, userId)
                    : await BuildPriceDiffReturnLinesAsync(po, reason, userId);
                if (cancelLines.Sum(l => l.Amount) > 0m)
                {
                    await _purchasePaymentService.UpsertRefundAsync(
                        requestForRefund, po, PurchasePaymentTypes.RefundCancellation, cancelLines, userId);
                }
            }

            po.Status = PurchaseOrderStatuses.Cancelled;
            po.CancelledAt = DateTime.UtcNow;
            po.CancelledBy = userId;
            po.CancelReason = reason;
            po.UpdatedAt = DateTime.UtcNow;
            po.UpdatedBy = userId;
            _unitOfWork.Repository<PurchaseOrder>().Update(po);

            // Không còn đơn nào đã đặt thì yêu cầu quay về "đã trả trước, chờ đặt mua".
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(po.PurchaseRequestId);
            if (placed && purchaseRequest != null)
            {
                var stillPlaced = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                    .Any(o => o.PurchaseRequestId == po.PurchaseRequestId && o.Id != po.Id
                              && PurchaseOrderStatuses.Placed.Contains(o.Status ?? string.Empty));
                if (!stillPlaced && string.Equals(purchaseRequest.Status, "PURCHASING", StringComparison.OrdinalIgnoreCase))
                {
                    await AddRequestHistoryAsync(purchaseRequest, "PAID", $"Huỷ đơn mua {po.PurchaseOrderCode}: {reason}", userId);
                    purchaseRequest.Status = "PAID";
                    requestRepo.Update(purchaseRequest);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            if (placed && purchaseRequest != null)
                await NotifyCustomerAsync(purchaseRequest, "Đơn mua NCC đã huỷ",
                    $"Đơn mua {po.PurchaseOrderCode} của yêu cầu {purchaseRequest.PurchaseCode} đã huỷ: {reason}. Sale sẽ liên hệ về hướng xử lý.");

            return await BuildDtoAsync(po, forCustomer: false);
        }

        /// <summary>
        /// Đóng sổ một khoản hoàn tiền.
        ///
        /// Bắt buộc có mã giao dịch: nguyên tắc "trạng thái phải có chứng cứ" — không có nút
        /// cho nhân viên bấm cho nó xong. Đơn mua giữ nguyên trạng thái, kể cả đơn đã huỷ,
        /// vì hoàn tiền là việc của sổ tiền chứ không phải một bước của đơn.
        /// </summary>
        public async Task<PurchaseOrderResponseDto> CompleteRefundAsync(
            Guid purchaseOrderId, Guid userId, string userRole, CompletePurchaseRefundDto dto)
        {
            if (!StaffRoleHelper.CanHandlePayments(userRole))
                throw new UnauthorizedAccessException("Chỉ Admin / Sale được xác nhận hoàn tiền.");

            var transactionCode = dto?.TransactionCode?.Trim();
            if (string.IsNullOrWhiteSpace(transactionCode))
                throw new ArgumentException("Phải nhập mã giao dịch chuyển khoản đã hoàn cho khách.");

            var po = await GetPoAsync(purchaseOrderId);
            var refund = await _purchasePaymentService.MarkRefundDoneAsync(po.Id, null, dto!.RefundId, transactionCode, dto.Amount);

            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId);
            if (purchaseRequest != null)
                await NotifyCustomerAsync(purchaseRequest, "Đã hoàn tiền",
                    $"Chúng tôi đã hoàn {refund.Amount:#,##0}đ cho đơn mua {po.PurchaseOrderCode} "
                    + $"(yêu cầu {purchaseRequest.PurchaseCode}). Mã giao dịch: {transactionCode}.");

            return await BuildDtoAsync(po, forCustomer: false);
        }

        // ── Đọc ──────────────────────────────────────────────────────────────────────────

        public async Task<PurchaseOrderResponseDto> GetAsync(Guid purchaseOrderId, Guid userId, string userRole)
        {
            var po = await GetPoAsync(purchaseOrderId);
            var isStaff = StaffRoleHelper.IsInternalStaff(userRole);
            if (!isStaff)
            {
                var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId)
                    ?? throw new NotFoundException("Không tìm thấy yêu cầu mua hộ.");
                await EnsureOwnerAsync(purchaseRequest, userId);
            }
            return await BuildDtoAsync(po, forCustomer: !isStaff);
        }

        public async Task<List<PurchaseOrderResponseDto>> ListByRequestAsync(Guid requestId, Guid userId, string userRole)
        {
            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            var isStaff = StaffRoleHelper.IsInternalStaff(userRole);
            if (!isStaff) await EnsureOwnerAsync(purchaseRequest, userId);

            var pos = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(o => o.PurchaseRequestId == requestId)
                .Where(o => isStaff || !PurchaseOrderStatuses.Is(o.Status, PurchaseOrderStatuses.Draft))
                .OrderBy(o => o.SequenceNo ?? 0)
                .ToList();

            var result = new List<PurchaseOrderResponseDto>();
            foreach (var po in pos) result.Add(await BuildDtoAsync(po, forCustomer: !isStaff));
            return result;
        }

        public async Task<PaginatedResponse<PurchaseOrderResponseDto>> ListAsync(
            string userRole, string? status, string? search, int pageNumber, int pageSize)
        {
            if (!StaffRoleHelper.IsInternalStaff(userRole))
                throw new UnauthorizedAccessException("Chỉ nhân viên xem được danh sách đơn mua NCC.");
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var statuses = (status ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keyword = search?.Trim();

            var query = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(o => statuses.Count == 0 || statuses.Contains(o.Status ?? string.Empty))
                .Where(o => string.IsNullOrWhiteSpace(keyword)
                            || (o.PurchaseOrderCode ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            || (o.SupplierOrderCode ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                            || (o.SupplierName ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.UpdatedAt ?? o.CreatedAt)
                .ToList();

            var page = query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
            var items = new List<PurchaseOrderResponseDto>();
            foreach (var po in page) items.Add(await BuildDtoAsync(po, forCustomer: false));

            return new PaginatedResponse<PurchaseOrderResponseDto>
            {
                Items = items,
                TotalCount = query.Count,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        // ── Bàn giao cho kho: đơn kho + hoá đơn chặng VN + phiếu tiếp nhận ─────────────────

        /// <summary>
        /// Sinh đơn kho PURCHASE (mã PUR-xxx-n) để hàng mua hộ đi chung luồng ký gửi từ đây:
        /// phiếu tiếp nhận ACTIVE cho kho quét → check-in cân đo → xếp kệ → xuất kho → về VN.
        /// Kèm một báo giá + hoá đơn chặng quốc tế (cước theo đơn giá /kg đã báo, VAT, thuế NK theo
        /// giá mua thật) và khoản "cọc" 0đ đã trả (tiền đã thu ở phần trả trước) để tất toán ở VN
        /// dùng nguyên cơ chế của ký gửi.
        /// </summary>
        private async Task<(Order Order, WarehouseReceivingNote Note)> HandOffToWarehouseAsync(
            PurchaseOrder po, PurchaseRequest purchaseRequest, Guid actorId)
        {
            var now = DateTime.UtcNow;
            var existingOrder = await FindWarehouseOrderAsync(po.Id);
            if (existingOrder != null)
                throw new ConflictException($"Đơn mua {po.PurchaseOrderCode} đã có đơn kho {existingOrder.ConsignmentCode}.");

            var poItems = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => i.PurchaseOrderId == po.Id)
                .ToList();
            var requestItems = (await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync())
                .Where(i => i.PurchaseRequestId == purchaseRequest.Id)
                .ToDictionary(i => i.Id);

            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = purchaseRequest.CustomerId,
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseOrderId = po.Id,
                ConsignmentCode = $"{purchaseRequest.PurchaseCode}-{po.SequenceNo}",
                ConsignmentType = purchaseRequest.ShippingOption,
                Route = purchaseRequest.Route,
                ReceiverName = purchaseRequest.ReceiverName,
                ReceiverPhone = purchaseRequest.ReceiverPhone,
                ReceiverAddress = purchaseRequest.ReceiverAddress,
                RequiresInspection = purchaseRequest.RequiresInspection,
                OrderType = "PURCHASE",
                // Tiền đã thu ở phần trả trước — tương đương đơn ký gửi đã trả cọc.
                Status = "DEPOSIT_PAID",
                Note = $"Sinh tự động khi đặt NCC đơn mua {po.PurchaseOrderCode} (yêu cầu {purchaseRequest.PurchaseCode}).",
                CreatedById = actorId,
                CreatedAt = now
            };
            await _unitOfWork.Repository<Order>().AddAsync(order);

            var orderItemRepo = _unitOfWork.Repository<OrderItem>();
            var orderItemByPoItem = new Dictionary<Guid, Guid>();
            foreach (var item in poItems)
            {
                requestItems.TryGetValue(item.PurchaseRequestItemId, out var reqItem);
                var orderItemId = Guid.NewGuid();
                orderItemByPoItem[item.Id] = orderItemId;
                await orderItemRepo.AddAsync(new OrderItem
                {
                    Id = orderItemId,
                    OrderId = order.Id,
                    ProductName = item.ProductName ?? reqItem?.ProductName,
                    ProductType = reqItem?.ProductType,
                    Quantity = item.Quantity,
                    DeclaredValue = item.LineTotal,
                    ReferenceUrl = reqItem?.ImageUrl,
                    Description = string.IsNullOrWhiteSpace(reqItem?.ProductLink)
                        ? reqItem?.Note
                        : $"{reqItem!.ProductLink}{(string.IsNullOrWhiteSpace(reqItem.Note) ? "" : " — " + reqItem.Note)}",
                    CreatedAt = now
                });
            }

            await CreateVnLegBillingAsync(po, purchaseRequest, order, poItems, requestItems, orderItemByPoItem, actorId);

            var noteRepo = _unitOfWork.Repository<WarehouseReceivingNote>();
            var existingCodes = (await noteRepo.GetAllAsync())
                .Select(n => n.ReceivingNoteCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var random = new Random();
            string code;
            do { code = $"WRN-{random.Next(10000000, 99999999)}"; } while (existingCodes.Contains(code));

            var note = new WarehouseReceivingNote
            {
                Id = Guid.NewGuid(),
                ReceivingNoteCode = code,
                ConsignmentOrderId = order.Id,
                WarehouseId = po.WarehouseId ?? throw new ArgumentException("Đơn mua chưa có kho nhận hàng."),
                WarehouseNote = $"Hàng mua hộ từ NCC {po.SupplierName}, mã đơn NCC {po.SupplierOrderCode}.",
                // Đơn mua đã được Admin duyệt nên phiếu mở luôn cho kho nhận, không qua bước OM duyệt phiếu.
                Status = WarehouseReceivingNoteService.StatusActive,
                CreatedById = actorId,
                CreatedAt = now,
                ReceiveApprovedById = actorId,
                ReceiveApprovedAt = now,
                ApprovalNote = $"Tự mở khi đặt NCC đơn mua {po.PurchaseOrderCode} (đơn mua đã được Admin duyệt)."
            };
            await noteRepo.AddAsync(note);

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, null, order.Status, "PURCHASE_ORDER_PLACED",
                $"Đặt NCC đơn mua {po.PurchaseOrderCode}; lập phiếu tiếp nhận {code}.", actorId);

            return (order, note);
        }

        private async Task CreateVnLegBillingAsync(
            PurchaseOrder po, PurchaseRequest purchaseRequest, Order order,
            List<PurchaseOrderItem> poItems, Dictionary<Guid, PurchaseRequestItem> requestItems,
            Dictionary<Guid, Guid> orderItemByPoItem, Guid actorId)
        {
            var now = DateTime.UtcNow;
            var pq = po.PurchaseQuotationId.HasValue
                ? await _unitOfWork.Repository<PurchaseQuotation>().GetByIdAsync(po.PurchaseQuotationId.Value)
                : null;

            // Cân tạm tính chia theo số lượng — tất toán ở VN tính lại theo cân đo thật.
            var requestedQty = Math.Max(requestItems.Values.Sum(i => i.Quantity), 1);
            var poQty = poItems.Sum(i => i.Quantity);
            var totalWeight = pq?.EstimatedWeight ?? Math.Max(1.0m, requestedQty * 0.2m);
            var weightShare = Math.Round(totalWeight * poQty / requestedQty, 3);

            var (rate, pricingId) = await ResolveFreightRateAsync(pq, purchaseRequest);
            var freight = rate > 0m
                ? Math.Round(weightShare * rate, 0)
                : Math.Round((pq?.ShippingFee ?? 0m) * poQty / requestedQty, 0);
            var vatRate = pq?.VatRate ?? await ResolveVatRateAsync();
            var vatFreight = Math.Round(freight * vatRate / 100m, 0);

            var quotation = new Quotation
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                QuoteType = "PURCHASE",
                Status = "ACCEPTED",
                EstimatedFreightCharge = freight,
                ServiceFee = 0m,
                ServicePricingId = pricingId,
                WarehouseId = po.WarehouseId,
                CreatedAt = now,
                CreatedById = actorId,
                AcceptedAt = now,
                PriceApprovalStatus = "NOT_REQUIRED",
                SalesNote = $"Chặng quốc tế của đơn mua {po.PurchaseOrderCode} — tạm tính, tất toán theo cân đo tại kho VN."
            };

            var fees = new List<QuotationFee>
            {
                new()
                {
                    Id = Guid.NewGuid(), QuotationId = quotation.Id,
                    FeeName = "Cước vận chuyển quốc tế (tạm tính)", FeeType = "MAIN_SERVICE",
                    CalculationType = rate > 0m ? "PER_KG" : "FIXED", Value = rate > 0m ? rate : freight,
                    Amount = freight, ChargeableWeight = weightShare, CreatedAt = now,
                    Note = $"Tạm tính {weightShare:0.###} kg × {rate:#,##0}đ"
                }
            };
            if (vatFreight > 0m)
                fees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(), QuotationId = quotation.Id,
                    FeeName = $"VAT {vatRate:G29}% cước quốc tế", FeeType = "TAX", CalculationType = "PERCENTAGE",
                    Value = vatRate, Amount = vatFreight, CreatedAt = now
                });

            // Thuế nhập khẩu tính trên giá mua thật của từng dòng.
            var categories = await _unitOfWork.Repository<ProductType>().GetAllAsync();
            decimal importTax = 0m;
            foreach (var item in poItems)
            {
                requestItems.TryGetValue(item.PurchaseRequestItemId, out var reqItem);
                var rawType = reqItem?.ProductType?.Trim() ?? string.Empty;
                var category = Guid.TryParse(rawType, out var typeId)
                    ? categories.FirstOrDefault(c => c.Id == typeId)
                    : categories.FirstOrDefault(c => string.Equals(c.Name?.Trim(), rawType, StringComparison.OrdinalIgnoreCase));
                var taxRate = category?.ImportTaxRate ?? 0m;
                var tax = Math.Round(item.LineTotal * taxRate, 0);
                if (tax <= 0m) continue;
                importTax += tax;
                fees.Add(new QuotationFee
                {
                    Id = Guid.NewGuid(), QuotationId = quotation.Id,
                    FeeName = $"{PurchaseImportTax.FeeNamePrefix} - {category!.Name}", FeeType = "TAX", CalculationType = "PERCENTAGE",
                    Value = taxRate * 100m, Amount = tax, CreatedAt = now,
                    // Gắn đúng dòng hàng để kiện bị huỷ / NCC giao thiếu trừ đúng phần thuế của dòng đó.
                    OrderItemId = orderItemByPoItem.TryGetValue(item.Id, out var taxedOrderItemId) ? taxedOrderItemId : null,
                    Note = $"{taxRate * 100m:G29}% × {item.LineTotal:#,##0}đ ({item.ProductName})"
                });
            }

            quotation.Vat = vatFreight;
            quotation.ImportTax = importTax;
            quotation.TaxAndDuty = vatFreight + importTax;
            await _unitOfWork.Repository<Quotation>().AddAsync(quotation);
            foreach (var fee in fees) await _unitOfWork.Repository<QuotationFee>().AddAsync(fee);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                InvoiceNo = $"INV-{now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                Currency = "VND",
                InvoiceStatus = "PENDING",
                IssuedAt = now
            };
            var invoiceItems = new List<InvoiceItem>();
            void AddItem(string type, decimal amount, string description)
            {
                if (amount == 0m) return;
                invoiceItems.Add(new InvoiceItem
                {
                    Id = Guid.NewGuid(), InvoiceId = invoice.Id, FeeType = type, ReferenceType = "ORDER",
                    ReferenceId = order.Id, Amount = amount, Currency = "VND", Description = description
                });
            }
            AddItem("FREIGHT", freight, "Cước vận chuyển quốc tế (tạm tính)");
            AddItem("TAX_AND_DUTY", vatFreight + importTax, "VAT cước + thuế nhập khẩu");

            // Yêu cầu đời cũ đã trả cọc theo tỷ lệ (gồm một phần cước / thuế): phần thu dư hoặc
            // thiếu so với "tiền hàng + phí mua" dồn vào hoá đơn của đơn kho đầu tiên.
            if (pq != null && !pq.PrepayAmount.HasValue && !await HasLegacyAdjustmentAsync(purchaseRequest.Id))
            {
                var firstPaid = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                    .Where(p => p.PurchaseRequestId == purchaseRequest.Id && PurchasePaymentTypes.IsPaid(p)
                                && PurchasePaymentTypes.IsFirstInstalment(p))
                    .Sum(p => p.Amount);
                var adjustment = pq.ProductsSubtotal + pq.PurchaseFee - firstPaid;
                AddItem(LegacyAdjustmentFeeType, adjustment,
                    adjustment < 0m
                        ? $"Trừ phần đã thu trước ở đợt cọc mua hộ {purchaseRequest.PurchaseCode}"
                        : $"Phần tiền hàng + phí mua hộ {purchaseRequest.PurchaseCode} còn thiếu sau đợt cọc");
            }

            invoice.TotalAmount = invoiceItems.Sum(i => i.Amount ?? 0m);
            await _unitOfWork.Repository<Invoice>().AddAsync(invoice);
            foreach (var item in invoiceItems) await _unitOfWork.Repository<InvoiceItem>().AddAsync(item);

            await _unitOfWork.Repository<Payment>().AddAsync(new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = 0m,
                PaymentMethod = "PREPAID",
                PaymentStatus = "PAID",
                InstallmentType = "DEPOSIT",
                TransactionCode = $"PREPAID-{purchaseRequest.PurchaseCode}",
                ConfirmationSource = "SYSTEM_ZERO",
                ConfirmedAt = now,
                PaidAt = now,
                CreatedAt = now
            });
        }

        private async Task<bool> HasLegacyAdjustmentAsync(Guid requestId)
        {
            var orderIds = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(o => o.PurchaseRequestId == requestId)
                .Select(o => o.Id)
                .ToHashSet();
            var quotationIds = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId.HasValue && orderIds.Contains(q.OrderId.Value))
                .Select(q => q.Id)
                .ToHashSet();
            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotationIds.Contains(i.QuotationId.Value))
                .Select(i => i.Id)
                .ToHashSet();
            return (await _unitOfWork.Repository<InvoiceItem>().GetAllAsync())
                .Any(i => i.InvoiceId.HasValue && invoiceIds.Contains(i.InvoiceId.Value)
                          && string.Equals(i.FeeType, LegacyAdjustmentFeeType, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<(decimal Rate, Guid? PricingId)> ResolveFreightRateAsync(PurchaseQuotation? pq, PurchaseRequest purchaseRequest)
        {
            if (pq?.FreightRatePerKg is > 0m) return (pq.FreightRatePerKg.Value, pq.ServicePricingId);

            // Báo giá đời cũ không lưu đơn giá /kg: lấy bảng giá theo loại dịch vụ của yêu cầu.
            var pricing = (await _unitOfWork.Repository<ServicePricing>().GetAllAsync())
                .Where(p => !p.IsDeleted && string.Equals(p.ServiceType, purchaseRequest.ShippingOption ?? "Standard", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.EffectiveDate)
                .FirstOrDefault();
            return (pricing?.Price ?? 0m, pricing?.Id);
        }

        private async Task<decimal> ResolveVatRateAsync()
        {
            var vatRule = (await _unitOfWork.Repository<PricingRule>().GetAllAsync())
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(r => string.Equals(r.RuleCode, "VAT", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(r.RuleType, "VAT", StringComparison.OrdinalIgnoreCase));
            if (vatRule != null && vatRule.Value > 0)
                return vatRule.Value <= 1.0m ? vatRule.Value * 100m : vatRule.Value;
            return 8m;
        }

        // ── Tiện ích ─────────────────────────────────────────────────────────────────────

        private async Task EnsureRequestOrderableAsync(PurchaseRequest purchaseRequest)
        {
            if (!OrderableRequestStatuses.Contains(purchaseRequest.Status?.Trim() ?? string.Empty))
                throw new ArgumentException($"Chỉ lập đơn mua khi khách đã thanh toán phần trả trước (trạng thái yêu cầu: {StatusNames.Quote(purchaseRequest.Status)}).");

            var paid = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Any(p => p.PurchaseRequestId == purchaseRequest.Id && PurchasePaymentTypes.IsPaid(p) && PurchasePaymentTypes.IsFirstInstalment(p));
            if (!paid)
                throw new ArgumentException("Khách chưa thanh toán phần trả trước của yêu cầu mua hộ.");

            // Đơn đời cũ đã sinh đơn kho qua "duyệt nhập kho" thì đi tiếp đường cũ.
            var legacyOrder = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Any(o => o.PurchaseRequestId == purchaseRequest.Id && !o.PurchaseOrderId.HasValue);
            if (legacyOrder)
                throw new ArgumentException("Yêu cầu này đã có đơn kho theo quy trình cũ — không lập đơn mua mới.");
        }

        private async Task<PurchaseQuotation?> GetAcceptedQuotationAsync(Guid requestId) =>
            (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                .Where(q => q.PurchaseRequestId == requestId && string.Equals(q.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(q => q.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();

        private async Task<PurchaseOrder> GetPoAsync(Guid id) =>
            await _unitOfWork.Repository<PurchaseOrder>().GetByIdAsync(id)
            ?? throw new NotFoundException($"Không tìm thấy đơn mua NCC với ID {id}");

        private async Task<Order?> FindWarehouseOrderAsync(Guid purchaseOrderId) =>
            (await _unitOfWork.Repository<Order>().GetAllAsync()).FirstOrDefault(o => o.PurchaseOrderId == purchaseOrderId);

        private async Task<int> CountProofsAsync(Guid purchaseOrderId) =>
            (await _unitOfWork.Repository<Attachment>().GetAllAsync())
                .Count(a => a.EntityId == purchaseOrderId
                            && string.Equals(a.EntityType, AttachmentEntityTypes.PurchaseOrder, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.DocumentType, AttachmentDocumentTypes.PurchaseProof, StringComparison.OrdinalIgnoreCase));

        /// <summary>Tỷ lệ phí huỷ (%) trên tiền hàng đã báo; không cấu hình thì bằng 0.</summary>
        private async Task<decimal> GetCancelFeeRateAsync()
        {
            var fee = (await _unitOfWork.Repository<AdditionalServiceFee>().GetAllAsync())
                .FirstOrDefault(f => f.IsActive && string.Equals(f.FeeCode, CancelFeeCode, StringComparison.OrdinalIgnoreCase));
            return fee == null ? DefaultCancelFeeRate : Math.Clamp(fee.Value, 0m, 100m);
        }

        private async Task<decimal> GetPriceToleranceRateAsync()
        {
            var fee = (await _unitOfWork.Repository<AdditionalServiceFee>().GetAllAsync())
                .FirstOrDefault(f => f.IsActive && string.Equals(f.FeeCode, PriceToleranceFeeCode, StringComparison.OrdinalIgnoreCase));
            return fee == null ? DefaultPriceToleranceRate : Math.Clamp(fee.Value, 0m, 100m);
        }

        private async Task EnsureOwnerAsync(PurchaseRequest purchaseRequest, Guid userId)
        {
            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync()).FirstOrDefault(c => c.UserId == userId);
            if (customer == null || purchaseRequest.CustomerId != customer.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền thao tác trên yêu cầu mua hộ này.");
        }

        private static void EnsureBuyer(string userRole)
        {
            if (!StaffRoleHelper.CanUpdatePurchaseProgress(userRole))
                throw new UnauthorizedAccessException("Chỉ Sale / nhân viên mua hàng được thao tác đơn mua NCC.");
        }

        private static bool IsAdmin(string? role) =>
            string.Equals(role?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role?.Trim(), "Administrator", StringComparison.OrdinalIgnoreCase);

        private async Task AddRequestHistoryAsync(PurchaseRequest purchaseRequest, string toStatus, string note, Guid actorId)
        {
            var actor = await _unitOfWork.Repository<User>().GetByIdAsync(actorId);
            await _unitOfWork.Repository<PurchaseRequestHistory>().AddAsync(new PurchaseRequestHistory
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = purchaseRequest.Id,
                FromStatus = purchaseRequest.Status,
                ToStatus = toStatus,
                Note = note,
                ChangedBy = actorId,
                ChangedByName = actor?.FullName,
                CreatedAt = DateTime.UtcNow
            });
        }

        private async Task NotifyCustomerAsync(PurchaseRequest purchaseRequest, string title, string content)
        {
            if (!purchaseRequest.CustomerId.HasValue) return;
            var customer = await _unitOfWork.Repository<Customer>().GetByIdAsync(purchaseRequest.CustomerId.Value);
            await NotifyUserAsync(customer?.UserId, title, content);
        }

        private async Task NotifyUserAsync(Guid? userId, string title, string content)
        {
            if (!userId.HasValue) return;
            try
            {
                await _notificationService.NotifyUserAsync(userId.Value, title, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PURCHASE ORDER NOTIFICATION ERROR: {ex.Message}");
            }
        }

        private async Task<PurchaseOrderResponseDto> BuildDtoAsync(PurchaseOrder po, bool forCustomer)
        {
            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(po.PurchaseRequestId);
            var customer = purchaseRequest?.CustomerId != null
                ? await _unitOfWork.Repository<Customer>().GetByIdAsync(purchaseRequest.CustomerId.Value)
                : null;
            var warehouse = po.WarehouseId.HasValue ? await _unitOfWork.Repository<Warehouse>().GetByIdAsync(po.WarehouseId.Value) : null;
            var requestItems = (await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync())
                .Where(i => i.PurchaseRequestId == po.PurchaseRequestId)
                .ToDictionary(i => i.Id);
            var items = (await _unitOfWork.Repository<PurchaseOrderItem>().GetAllAsync())
                .Where(i => i.PurchaseOrderId == po.Id)
                .ToList();
            /*
             * Các khoản tiền gắn với ĐƠN MUA này. Phải tách hai chiều: khoản THU thêm của
             * khách (chênh giá) và khoản HOÀN trả khách. Trước đây chỗ này lấy "khoản mới
             * nhất chưa huỷ" cho cả hai, nên từ lúc có khoản hoàn thì ô "chênh giá" trên
             * màn hình sẽ hiện nhầm số tiền đang chờ trả lại khách.
             */
            var poPayments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseOrderId == po.Id && !string.Equals(p.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var diffPayment = poPayments
                .Where(p => PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.PriceDifference)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault();

            var refundPayment = poPayments
                .Where(PurchasePaymentTypes.IsRefund)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault();

            // Mọi khoản hoàn liên quan đơn này: gắn thẳng vào đơn, hoặc có dòng chi tiết thuộc đơn
            // (NCC giao thiếu được hoàn chung trong khoản "không mua được" của cả yêu cầu).
            var lineRefundIds = (await _unitOfWork.Repository<PurchaseRefundLine>().GetAllAsync())
                .Where(l => l.PurchaseOrderId == po.Id)
                .Select(l => l.PurchasePaymentId)
                .ToHashSet();
            var relatedRefunds = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => PurchasePaymentTypes.IsRefund(p) && (p.PurchaseOrderId == po.Id || lineRefundIds.Contains(p.Id)))
                .ToList();
            var refundDtos = await _purchasePaymentService.BuildRefundDtosAsync(relatedRefunds);
            var liveCancelRefunds = refundDtos
                .Where(r => r.PurchaseOrderId == po.Id
                            && string.Equals(r.RefundType, PurchasePaymentTypes.RefundCancellation, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(r.Status, PurchaseRefundStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var cancelFeeAmount = liveCancelRefunds.Sum(r => r.IsLegacy
                ? Math.Max(0m, (po.QuotedGoodsAmount ?? 0m) - r.Amount)   // khoản hoàn đời cũ không có chi tiết
                : r.CancelFeeAmount);
            var warehouseInvoiceStatus = await FindWarehouseInvoiceStatusAsync(po.Id);
            var order = await FindWarehouseOrderAsync(po.Id);
            var note = order == null
                ? null
                : (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                    .Where(n => n.ConsignmentOrderId == order.Id)
                    .OrderByDescending(n => n.CreatedAt)
                    .FirstOrDefault();

            return new PurchaseOrderResponseDto
            {
                PurchaseOrderId = po.Id,
                PurchaseOrderCode = po.PurchaseOrderCode ?? string.Empty,
                SequenceNo = po.SequenceNo,
                PurchaseRequestId = po.PurchaseRequestId,
                PurchaseCode = purchaseRequest?.PurchaseCode ?? string.Empty,
                PurchaseQuotationId = po.PurchaseQuotationId,
                CustomerName = customer?.FullName,
                SupplierId = forCustomer ? null : po.SupplierId,
                SupplierName = forCustomer ? null : po.SupplierName,
                WarehouseId = po.WarehouseId,
                WarehouseName = warehouse?.WarehouseName,
                Currency = forCustomer ? null : po.Currency,
                ExchangeRate = forCustomer ? null : po.ExchangeRate,
                PurchaseNote = forCustomer ? null : po.PurchaseNote,
                TotalAmount = po.TotalAmount,
                QuotedGoodsAmount = po.QuotedGoodsAmount ?? 0m,
                PriceDifferenceAmount = po.PriceDifferenceAmount ?? 0m,
                PriceToleranceRate = await GetPriceToleranceRateAsync(),
                Status = po.Status ?? string.Empty,
                StatusText = PurchaseOrderStatuses.Text(po.Status),
                CustomerRejectReason = po.CustomerRejectReason,
                CustomerDecisionAt = po.CustomerDecisionAt,
                SubmittedAt = po.SubmittedAt,
                DecidedAt = po.DecidedAt,
                DecisionNote = forCustomer ? null : po.DecisionNote,
                SupplierOrderCode = forCustomer ? null : po.SupplierOrderCode,
                OrderedAt = po.OrderedAt,
                SupplierConfirmedAt = po.SupplierConfirmedAt,
                SupplierShippedAt = po.SupplierShippedAt,
                DomesticCarrier = po.DomesticCarrier,
                DomesticTrackingCode = po.DomesticTrackingCode,
                CancelledAt = po.CancelledAt,
                CancelReason = po.CancelReason,
                PriceDifferencePaymentId = diffPayment?.Id,
                PriceDifferenceCheckoutUrl = diffPayment?.CheckoutUrl,
                PriceDifferencePaymentStatus = diffPayment?.Status,
                RefundAmount = refundPayment?.Amount ?? 0m,
                RefundPaymentId = refundPayment?.Id,
                RefundType = refundPayment?.PaymentType,
                RefundStatus = refundPayment?.Status,
                RefundStatusText = refundPayment == null ? null : PurchaseRefundStatuses.Text(refundPayment.Status),
                RefundedAt = refundPayment?.PaidAt,
                CancelFeeRate = await GetCancelFeeRateAsync(),
                /* Phí huỷ lấy đúng số đã trừ ghi ở chi tiết khoản hoàn (tính trên tiền hàng khách thực trả). */
                CancelFeeAmount = cancelFeeAmount,
                TotalRefundAmount = refundDtos
                    .Where(r => !string.Equals(r.Status, PurchaseRefundStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                    .Sum(r => r.Amount),
                Refunds = refundDtos,
                WarehouseInvoiceStatus = warehouseInvoiceStatus,
                WarehouseOrderId = order?.Id,
                WarehouseOrderCode = order?.ConsignmentCode,
                WarehouseOrderStatus = order?.Status,
                ReceivingNoteCode = forCustomer ? null : note?.ReceivingNoteCode,
                ReceivingNoteStatus = forCustomer ? null : note?.Status,
                ProofCount = forCustomer ? 0 : await CountProofsAsync(po.Id),
                CreatedAt = po.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = po.UpdatedAt,
                Items = items.Select(i => new PurchaseOrderItemResponseDto
                {
                    OrderItemId = i.Id,
                    PurchaseRequestItemId = i.PurchaseRequestItemId,
                    ProductName = i.ProductName,
                    RequestedQuantity = requestItems.TryGetValue(i.PurchaseRequestItemId, out var r) ? r.Quantity : 0,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    UnitPriceOriginal = forCustomer ? null : i.UnitPriceOriginal,
                    QuotedUnitPrice = i.QuotedUnitPrice,
                    LineTotal = i.LineTotal
                }).ToList()
            };
        }
    }
}
