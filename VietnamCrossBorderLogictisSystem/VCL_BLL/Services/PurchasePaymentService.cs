using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class PurchasePaymentService : IPurchasePaymentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPayOsService _payOsService;
        private readonly ISePayService _sePayService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PurchasePaymentService> _logger;

        /// <summary>Phần trăm phí dịch vụ &amp; cước thu ở đợt cọc (báo giá đời cũ).</summary>
        private const decimal DefaultServiceDepositRate = 50m;

        private const string DepositDescriptionTemplate = "100% tiền hàng + {0}% phí dịch vụ & cước";

        public PurchasePaymentService(
            IUnitOfWork unitOfWork,
            IPayOsService payOsService,
            ISePayService sePayService,
            IConfiguration configuration,
            ILogger<PurchasePaymentService> logger)
        {
            _unitOfWork = unitOfWork;
            _payOsService = payOsService;
            _sePayService = sePayService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ConfirmQuotationAndPayResponseDto> ConfirmQuotationAndPayAsync(
            Guid requestId, Guid userId, string userRole, ConfirmQuotationAndPayRequestDto request)
        {
            request ??= new ConfirmQuotationAndPayRequestDto();

            // 1. Phân quyền: chỉ Customer được xác nhận báo giá của mình
            if (!string.Equals(userRole, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Chỉ khách hàng mới được xác nhận báo giá mua hộ.");
            }

            // 2. Tìm yêu cầu mua hộ (404 nếu không có)
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            // 3. Kiểm tra quyền sở hữu: yêu cầu phải thuộc về Customer đang đăng nhập
            var customers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = customers.FirstOrDefault(c => c.UserId == userId);
            if (customer == null || purchaseRequest.CustomerId != customer.Id)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền thao tác trên yêu cầu mua hộ này.");
            }

            // 4. Tìm báo giá đang chờ khách xác nhận
            var quotationRepo = _unitOfWork.Repository<PurchaseQuotation>();
            var allQuotations = await quotationRepo.GetAllAsync();
            var quotation = allQuotations
                .Where(q => q.PurchaseRequestId == requestId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (quotation == null)
            {
                throw new NotFoundException("Không tìm thấy báo giá cho yêu cầu mua hộ này.");
            }

            var quotationStatus = quotation.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (quotationStatus != "PENDING_CUSTOMER_CONFIRMATION")
            {
                throw new ArgumentException(
                    $"Báo giá không còn ở trạng thái chờ xác nhận (hiện tại: {StatusNames.Quote(quotation.Status)}). Không thể xác nhận lại.");
            }

            // 5. Sinh mã đơn hàng payOS duy nhất
            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var allPayments = await paymentRepo.GetAllAsync();
            long orderCode = await GenerateUniqueOrderCode(allPayments);

            // 6. Khách trả 100% phần trả trước (tiền hàng + phí mua + ship nội địa + VAT phần phí).
            // Cước quốc tế + thuế chỉ là tạm tính, thu ở VN theo cân đo thật.
            if (!quotation.PrepayAmount.HasValue)
            {
                throw new ArgumentException(PurchasePrepayRule.LegacyMessage);
            }
            var deposit = BuildPrepayBreakdown(quotation);
            if (deposit.DepositAmount <= 0m)
            {
                throw new ArgumentException("Phần trả trước phải lớn hơn 0. Vui lòng kiểm tra lại báo giá.");
            }

            var purchaseCode = purchaseRequest.PurchaseCode ?? string.Empty;
            var (checkoutUrl, paymentLinkId, paymentMethodString) = await CreatePaymentLinkAsync(
                orderCode, deposit.DepositAmount, purchaseCode, request.PaymentMethod, request.ReturnUrl, request.CancelUrl);

            // 7. Lưu khoản thanh toán ở trạng thái PENDING
            var payment = new PurchasePayment
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                PurchaseQuotationId = quotation.Id,
                OrderCode = orderCode,
                Amount = deposit.DepositAmount,
                PaymentMethod = paymentMethodString,
                Status = "PENDING",
                PaymentType = PurchasePaymentTypes.Prepayment,
                PaymentLinkId = paymentLinkId,
                CheckoutUrl = checkoutUrl,
                CreatedAt = DateTime.UtcNow
            };
            await paymentRepo.AddAsync(payment);

            // 8. Cập nhật trạng thái báo giá và yêu cầu mua hộ
            quotation.Status = "ACCEPTED";
            quotationRepo.Update(quotation);

            purchaseRequest.Status = "WAITING_PAYMENT";
            requestRepo.Update(purchaseRequest);

            try
            {
                await _unitOfWork.Repository<Notification>().AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = "Khởi tạo thanh toán trước mua hộ",
                    Content = $"Khoản trả trước {deposit.DepositAmount:#,##0}đ cho yêu cầu mua hộ (Mã: {purchaseCode}) đã được khởi tạo thành công.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tạo thông báo thanh toán mua hộ cho mã {PurchaseCode}", purchaseCode);
            }

            await _unitOfWork.SaveChangesAsync();

            return new ConfirmQuotationAndPayResponseDto
            {
                PurchaseRequestId = requestId,
                PurchaseCode = purchaseCode,
                RequestStatus = purchaseRequest.Status,
                QuotationId = quotation.Id,
                QuotationStatus = quotation.Status,
                PaymentId = payment.Id,
                OrderCode = payment.OrderCode,
                Amount = payment.Amount,
                PaymentMethod = payment.PaymentMethod,
                PaymentStatus = payment.Status,
                DepositDescription = deposit.Description,
                TotalBillAmount = deposit.TotalBill,
                ProductsSubtotal = deposit.ProductsSubtotal,
                ServicesSubtotal = deposit.ServicesSubtotal,
                ServicesDepositAmount = deposit.ServicesDeposit,
                DepositAmount = deposit.DepositAmount,
                RemainingAmount = deposit.RemainingAmount,
                CheckoutUrl = payment.CheckoutUrl,
                PaymentLinkId = payment.PaymentLinkId
            };
        }

        public async Task<PurchasePayment> CreatePriceDifferencePaymentAsync(
            PurchaseRequest purchaseRequest, PurchaseOrder purchaseOrder, decimal amount, string? paymentMethod, string? returnUrl,
            string? cancelUrl = null)
        {
            if (amount <= 0m)
                throw new ArgumentException("Phần chênh phải lớn hơn 0.");

            var quotationId = purchaseOrder.PurchaseQuotationId
                ?? throw new ArgumentException("Đơn mua chưa gắn với báo giá khách đã chấp nhận.");

            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var orderCode = await GenerateUniqueOrderCode(await paymentRepo.GetAllAsync());
            var (checkoutUrl, paymentLinkId, method) = await CreatePaymentLinkAsync(
                orderCode, amount, purchaseRequest.PurchaseCode ?? string.Empty, paymentMethod, returnUrl, cancelUrl);

            var payment = new PurchasePayment
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseQuotationId = quotationId,
                PurchaseOrderId = purchaseOrder.Id,
                OrderCode = orderCode,
                Amount = amount,
                PaymentMethod = method,
                Status = "PENDING",
                PaymentType = PurchasePaymentTypes.PriceDifference,
                PaymentLinkId = paymentLinkId,
                CheckoutUrl = checkoutUrl,
                CreatedAt = DateTime.UtcNow
            };
            await paymentRepo.AddAsync(payment);
            return payment;
        }

        /// <summary>
        /// Khoản PHẢI HOÀN cho khách. Nằm chung bảng PURCHASE_PAYMENTS với các khoản thu vì
        /// đây vẫn là dòng tiền của cùng một yêu cầu mua hộ, và Sale đọc lịch sử tiền ở một
        /// chỗ duy nhất; phân biệt bằng PaymentType bắt đầu bằng REFUND.
        ///
        /// Không tạo link thanh toán: tiền đi RA, kế toán chuyển tay rồi ghi mã giao dịch.
        ///
        /// Số tiền = tổng các dòng chi tiết (PURCHASE_REFUND_LINES). Chặn cứng: tổng mọi khoản hoàn
        /// còn hiệu lực của yêu cầu không được vượt tổng tiền khách đã trả cho yêu cầu.
        /// </summary>
        public async Task<PurchasePayment> UpsertRefundAsync(
            PurchaseRequest purchaseRequest, PurchaseOrder? purchaseOrder, string refundType,
            List<PurchaseRefundLine> lines, Guid? actorId, bool mergeWithPending = true)
        {
            lines ??= new List<PurchaseRefundLine>();
            var amount = lines.Sum(l => l.Amount);
            if (amount <= 0m)
                throw new ArgumentException("Số tiền hoàn phải lớn hơn 0.");

            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var all = await paymentRepo.GetAllAsync();

            var quotationId = purchaseOrder?.PurchaseQuotationId
                ?? all.Where(p => p.PurchaseRequestId == purchaseRequest.Id && PurchasePaymentTypes.IsFirstInstalment(p))
                    .Select(p => (Guid?)p.PurchaseQuotationId).FirstOrDefault()
                ?? throw new ArgumentException("Yêu cầu chưa gắn với báo giá khách đã chấp nhận.");

            var pending = mergeWithPending && purchaseOrder != null
                ? all.FirstOrDefault(p =>
                    p.PurchaseOrderId == purchaseOrder.Id &&
                    string.Equals(p.PaymentType, refundType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Status, PurchaseRefundStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                : null;

            await EnsureWithinCollectedAsync(purchaseRequest, all, amount, pending?.Id);

            var lineRepo = _unitOfWork.Repository<PurchaseRefundLine>();
            PurchasePayment refund;
            if (pending != null)
            {
                pending.Amount = amount;
                paymentRepo.Update(pending);
                foreach (var old in (await lineRepo.GetAllAsync()).Where(l => l.PurchasePaymentId == pending.Id).ToList())
                    lineRepo.Delete(old);
                refund = pending;
            }
            else
            {
                refund = new PurchasePayment
                {
                    Id = Guid.NewGuid(),
                    PurchaseRequestId = purchaseRequest.Id,
                    PurchaseQuotationId = quotationId,
                    PurchaseOrderId = purchaseOrder?.Id,
                    OrderCode = await GenerateUniqueOrderCode(all),
                    Amount = amount,
                    PaymentMethod = "MANUAL",
                    Status = PurchaseRefundStatuses.Pending,
                    PaymentType = refundType,
                    PaymentLinkId = string.Empty,
                    CheckoutUrl = string.Empty,
                    CreatedAt = DateTime.UtcNow
                };
                await paymentRepo.AddAsync(refund);
            }

            var now = DateTime.UtcNow;
            foreach (var line in lines)
            {
                line.Id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id;
                line.PurchasePaymentId = refund.Id;
                line.PurchaseRequestId = purchaseRequest.Id;
                line.CreatedAt = now;
                line.CreatedBy ??= actorId;
                await lineRepo.AddAsync(line);
            }

            return refund;
        }

        /// <summary>
        /// Chặn tổng hoàn vượt tổng đã thu. "Đã thu" = mọi khoản thu mua hộ đã PAID (trả trước +
        /// chênh giá); "đã hoàn" = mọi khoản hoàn chưa huỷ (chờ hoàn + đã hoàn).
        /// </summary>
        private Task EnsureWithinCollectedAsync(
            PurchaseRequest purchaseRequest, IEnumerable<PurchasePayment> all, decimal newAmount, Guid? replacingRefundId)
        {
            var ofRequest = all.Where(p => p.PurchaseRequestId == purchaseRequest.Id).ToList();
            var collected = ofRequest.Where(p => !PurchasePaymentTypes.IsRefund(p) && PurchasePaymentTypes.IsPaid(p)).Sum(p => p.Amount);
            var refunded = ofRequest
                .Where(p => PurchasePaymentTypes.IsRefund(p) && p.Id != replacingRefundId
                            && !string.Equals(p.Status, PurchaseRefundStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Amount);

            if (refunded + newAmount > collected)
                throw new ArgumentException(
                    $"Tổng tiền hoàn ({refunded + newAmount:#,##0}đ) sẽ vượt tổng khách đã trả cho yêu cầu {purchaseRequest.PurchaseCode} "
                    + $"({collected:#,##0}đ; đã/đang hoàn {refunded:#,##0}đ). Kiểm tra lại các khoản hoàn trước khi hoàn thêm.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Kế toán đã chuyển trả khách: đóng ĐÚNG khoản hoàn và lưu mã giao dịch.
        /// Nhiều khoản đang chờ thì bắt buộc chỉ rõ <paramref name="refundId"/>; gửi số tiền thì
        /// phải khớp đúng số của khoản đó — không đóng nhầm khoản cũ nhất như trước.
        /// </summary>
        public async Task<PurchasePayment> MarkRefundDoneAsync(
            Guid? purchaseOrderId, Guid? purchaseRequestId, Guid? refundId, string? transactionCode, decimal? amount)
        {
            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var all = (await paymentRepo.GetAllAsync()).Where(PurchasePaymentTypes.IsRefund).ToList();

            List<PurchasePayment> scope;
            if (purchaseOrderId.HasValue)
            {
                var lineRefundIds = (await _unitOfWork.Repository<PurchaseRefundLine>().GetAllAsync())
                    .Where(l => l.PurchaseOrderId == purchaseOrderId.Value)
                    .Select(l => l.PurchasePaymentId)
                    .ToHashSet();
                scope = all.Where(p => p.PurchaseOrderId == purchaseOrderId.Value || lineRefundIds.Contains(p.Id)).ToList();
            }
            else
            {
                scope = all.Where(p => p.PurchaseRequestId == purchaseRequestId).ToList();
            }

            PurchasePayment refund;
            if (refundId.HasValue)
            {
                refund = scope.FirstOrDefault(p => p.Id == refundId.Value)
                    ?? throw new NotFoundException("Không tìm thấy khoản hoàn này trong đơn / yêu cầu.");
                if (!string.Equals(refund.Status, PurchaseRefundStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Khoản hoàn không ở trạng thái chờ hoàn (hiện tại: {PurchaseRefundStatuses.Text(refund.Status)}).");
            }
            else
            {
                var pending = scope
                    .Where(p => string.Equals(p.Status, PurchaseRefundStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (pending.Count == 0)
                    throw new NotFoundException("Không có khoản hoàn tiền nào đang chờ.");
                if (pending.Count > 1)
                    throw new ArgumentException(
                        $"Có {pending.Count} khoản hoàn đang chờ ({string.Join(", ", pending.Select(p => $"{p.Amount:#,##0}đ"))}) — "
                        + "gửi refundId để chỉ rõ khoản đã chuyển.");
                refund = pending[0];
            }

            if (amount.HasValue && amount.Value != refund.Amount)
                throw new ArgumentException(
                    $"Số tiền đã chuyển ({amount.Value:#,##0}đ) không khớp khoản hoàn ({refund.Amount:#,##0}đ).");

            refund.Status = PurchaseRefundStatuses.Refunded;
            refund.PaidAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(transactionCode))
                refund.TransactionCode = transactionCode.Trim();

            paymentRepo.Update(refund);
            await _unitOfWork.SaveChangesAsync();
            return refund;
        }

        /// <summary>Dựng DTO các khoản hoàn kèm chi tiết từng dòng (mới nhất trước).</summary>
        public async Task<List<PurchaseRefundDto>> BuildRefundDtosAsync(IEnumerable<PurchasePayment> refunds)
        {
            var list = refunds.Where(PurchasePaymentTypes.IsRefund).OrderByDescending(p => p.CreatedAt).ToList();
            if (list.Count == 0) return new List<PurchaseRefundDto>();

            var ids = list.Select(p => p.Id).ToHashSet();
            var lines = (await _unitOfWork.Repository<PurchaseRefundLine>().GetAllAsync())
                .Where(l => ids.Contains(l.PurchasePaymentId))
                .ToList();
            var poIds = list.Where(p => p.PurchaseOrderId.HasValue).Select(p => p.PurchaseOrderId!.Value)
                .Concat(lines.Where(l => l.PurchaseOrderId.HasValue).Select(l => l.PurchaseOrderId!.Value))
                .ToHashSet();
            var poCodes = poIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                    .Where(o => poIds.Contains(o.Id))
                    .ToDictionary(o => o.Id, o => o.PurchaseOrderCode ?? string.Empty);

            string? CodeOf(Guid? id) => id.HasValue && poCodes.TryGetValue(id.Value, out var c) ? c : null;

            return list.Select(p =>
            {
                var own = lines.Where(l => l.PurchasePaymentId == p.Id)
                    .OrderBy(l => l.CreatedAt).ThenBy(l => l.ProductName)
                    .ToList();
                return new PurchaseRefundDto
                {
                    RefundId = p.Id,
                    PurchaseRequestId = p.PurchaseRequestId,
                    PurchaseOrderId = p.PurchaseOrderId,
                    PurchaseOrderCode = CodeOf(p.PurchaseOrderId),
                    RefundType = PurchasePaymentTypes.Of(p),
                    RefundTypeText = PurchasePaymentTypes.RefundText(p.PaymentType),
                    Amount = p.Amount,
                    Status = p.Status ?? string.Empty,
                    StatusText = PurchaseRefundStatuses.Text(p.Status),
                    TransactionCode = p.TransactionCode,
                    CreatedAt = p.CreatedAt,
                    RefundedAt = string.Equals(p.Status, PurchaseRefundStatuses.Refunded, StringComparison.OrdinalIgnoreCase) ? p.PaidAt : null,
                    GoodsAmount = own.Sum(l => l.GoodsAmount),
                    PriceDifferenceAmount = own.Sum(l => l.PriceDifferenceAmount),
                    ServiceFeeAmount = own.Sum(l => l.ServiceFeeAmount),
                    VatAmount = own.Sum(l => l.VatAmount),
                    ImportTaxAdjustment = own.Sum(l => l.ImportTaxAdjustment),
                    CancelFeeAmount = own.Sum(l => l.CancelFeeAmount),
                    IsLegacy = own.Count == 0,
                    Lines = own.Select(l => new PurchaseRefundLineDto
                    {
                        LineId = l.Id,
                        PurchaseOrderId = l.PurchaseOrderId,
                        PurchaseOrderCode = CodeOf(l.PurchaseOrderId),
                        PurchaseRequestItemId = l.PurchaseRequestItemId,
                        ProductName = l.ProductName,
                        ReasonCode = l.ReasonCode ?? string.Empty,
                        ReasonText = PurchaseRefundReasons.Text(l.ReasonCode),
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice,
                        GoodsAmount = l.GoodsAmount,
                        PriceDifferenceAmount = l.PriceDifferenceAmount,
                        ServiceFeeAmount = l.ServiceFeeAmount,
                        VatAmount = l.VatAmount,
                        ImportTaxAdjustment = l.ImportTaxAdjustment,
                        ImportTaxInRefund = l.ImportTaxInRefund,
                        CancelFeeAmount = l.CancelFeeAmount,
                        Amount = l.Amount,
                        Formula = l.Formula,
                        Note = l.Note
                    }).ToList()
                };
            }).ToList();
        }

        /// <summary>Tổng hợp tiền hoàn của một yêu cầu (không kiểm quyền — người gọi tự kiểm).</summary>
        public async Task<PurchaseRefundSummaryDto> BuildRefundSummaryAsync(PurchaseRequest purchaseRequest)
        {
            var payments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseRequestId == purchaseRequest.Id)
                .ToList();
            var refunds = payments.Where(PurchasePaymentTypes.IsRefund).ToList();
            var collected = payments.Where(p => !PurchasePaymentTypes.IsRefund(p) && PurchasePaymentTypes.IsPaid(p)).Sum(p => p.Amount);
            var refunded = refunds.Where(p => string.Equals(p.Status, PurchaseRefundStatuses.Refunded, StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
            var pendingRefund = refunds.Where(p => string.Equals(p.Status, PurchaseRefundStatuses.Pending, StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);

            return new PurchaseRefundSummaryDto
            {
                PurchaseRequestId = purchaseRequest.Id,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                TotalCollected = collected,
                TotalRefunded = refunded,
                TotalPendingRefund = pendingRefund,
                RefundableRemaining = Math.Max(0m, collected - refunded - pendingRefund),
                Refunds = await BuildRefundDtosAsync(refunds)
            };
        }

        public async Task<PurchaseRefundSummaryDto> GetRefundSummaryAsync(Guid requestId, Guid userId, string userRole)
        {
            var purchaseRequest = await _unitOfWork.Repository<PurchaseRequest>().GetByIdAsync(requestId)
                ?? throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            await EnsureCanViewAsync(purchaseRequest, userId, userRole);
            return await BuildRefundSummaryAsync(purchaseRequest);
        }

        private async Task EnsureCanViewAsync(PurchaseRequest purchaseRequest, Guid userId, string userRole)
        {
            if (StaffRoleHelper.IsInternalStaff(userRole)) return;
            var customer = (await _unitOfWork.Repository<Customer>().GetAllAsync()).FirstOrDefault(c => c.UserId == userId);
            if (customer == null || purchaseRequest.CustomerId != customer.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền xem tiền hoàn của yêu cầu mua hộ này.");
        }

        public async Task<PurchasePaymentHistoryResponseDto> GetPaymentHistoryAsync(
            Guid requestId, Guid userId, string userRole)
        {
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            // Customer chỉ được xem đơn của chính mình; Staff/Sales xem tất cả
            bool isStaff = StaffRoleHelper.CanHandlePayments(userRole);
            if (!isStaff)
            {
                var customers = await _unitOfWork.Repository<Customer>().GetAllAsync();
                var customer = customers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null || purchaseRequest.CustomerId != customer.Id)
                {
                    throw new UnauthorizedAccessException("Bạn không có quyền xem thanh toán của đơn mua hộ này.");
                }
            }

            var payments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Where(p => p.PurchaseRequestId == requestId)
                .OrderBy(p => p.CreatedAt)
                .ToList();

            var quotation = (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                .Where(q => q.PurchaseRequestId == requestId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            var totalBill = quotation?.TotalAmount ?? 0m;
            var totalPaid = payments.Where(IsPaid).Sum(p => p.Amount);
            var outstanding = totalBill - totalPaid;
            if (outstanding < 0m) outstanding = 0m;

            var deposit = quotation == null
                ? null
                : quotation.PrepayAmount.HasValue ? BuildPrepayBreakdown(quotation) : await BuildDepositBreakdownAsync(quotation);

            // Khoản đầu đã tạo thì lấy đúng số đã thu, không tính lại theo báo giá hiện tại.
            var depositPayment = payments.FirstOrDefault(PurchasePaymentTypes.IsFirstInstalment);
            var depositAmount = depositPayment?.Amount ?? deposit?.DepositAmount ?? 0m;

            // Luồng mới: phần trả trước thu ở đây, phần cước + thuế thu ở VN (hoá đơn của đơn kho).
            if (quotation?.PrepayAmount.HasValue == true)
            {
                totalBill = quotation.PrepayAmount.Value + payments
                    .Where(p => PurchasePaymentTypes.Of(p) == PurchasePaymentTypes.PriceDifference && IsPaid(p))
                    .Sum(p => p.Amount);
                outstanding = Math.Max(totalBill - totalPaid, 0m);
            }

            var refundSummary = await BuildRefundSummaryAsync(purchaseRequest);

            return new PurchasePaymentHistoryResponseDto
            {
                TotalRefunded = refundSummary.TotalRefunded,
                TotalPendingRefund = refundSummary.TotalPendingRefund,
                Refunds = refundSummary.Refunds,
                PurchaseRequestId = requestId,
                PurchaseCode = purchaseRequest.PurchaseCode ?? string.Empty,
                RequestStatus = purchaseRequest.Status ?? string.Empty,
                TotalBillAmount = totalBill,
                TotalPaid = totalPaid,
                Outstanding = outstanding,
                ProductsSubtotal = deposit?.ProductsSubtotal ?? 0m,
                ServicesSubtotal = deposit?.ServicesSubtotal ?? 0m,
                DepositAmount = depositAmount,
                DepositDescription = deposit?.Description ?? string.Empty,
                IsLegacy = quotation != null && !quotation.PrepayAmount.HasValue,
                EstimatedLaterAmount = quotation?.EstimatedLaterAmount ?? 0m,
                Payments = payments.Select(p => new PurchasePaymentHistoryItemDto
                {
                    PaymentId = p.Id,
                    OrderCode = p.OrderCode,
                    PaymentType = PurchasePaymentTypes.Of(p),
                    PurchaseOrderId = p.PurchaseOrderId,
                    Amount = p.Amount,
                    PaymentMethod = p.PaymentMethod,
                    Status = p.Status ?? string.Empty,
                    CheckoutUrl = p.CheckoutUrl,
                    TransactionCode = p.TransactionCode,
                    CreatedAt = p.CreatedAt,
                    PaidAt = p.PaidAt
                }).ToList()
            };
        }

        /// <summary>Kết quả tách bill thành phần tiền hàng và phần dịch vụ để tính cọc.</summary>
        private sealed class DepositBreakdown
        {
            public decimal TotalBill { get; init; }
            public decimal ProductsSubtotal { get; init; }
            public decimal ServicesSubtotal { get; init; }
            public decimal ServicesDeposit { get; init; }
            public decimal DepositAmount { get; init; }
            public decimal RemainingAmount { get; init; }
            public string Description { get; init; } = string.Empty;
        }

        /// <summary>Luồng mới: khách trả 100% phần trả trước; phần còn lại là tạm tính thu ở VN.</summary>
        private static DepositBreakdown BuildPrepayBreakdown(PurchaseQuotation quotation)
        {
            var prepay = quotation.PrepayAmount ?? 0m;
            var products = Math.Max(quotation.ProductsSubtotal, 0m);
            return new DepositBreakdown
            {
                TotalBill = quotation.TotalAmount,
                ProductsSubtotal = products,
                ServicesSubtotal = Math.Max(prepay - products, 0m),
                ServicesDeposit = Math.Max(prepay - products, 0m),
                DepositAmount = prepay,
                RemainingAmount = quotation.EstimatedLaterAmount ?? 0m,
                Description = PurchasePrepayRule.Description
            };
        }

        /// <summary>
        /// Đời cũ — cọc mua hộ = 100% tiền hàng + N% phần còn lại của bill (phí mua hộ, cước, thuế, phụ phí).
        /// "Phần dịch vụ" lấy bằng tổng bill trừ tiền hàng thay vì cộng từng khoản, để không bỏ sót
        /// phụ phí phát sinh và luôn khớp với cách web khách hàng tính.
        /// </summary>
        private async Task<DepositBreakdown> BuildDepositBreakdownAsync(PurchaseQuotation quotation)
        {
            var rate = await GetServiceDepositRateAsync();

            var totalBill = quotation.TotalAmount;
            var productsSubtotal = Math.Max(quotation.ProductsSubtotal, 0m);
            if (productsSubtotal > totalBill) productsSubtotal = totalBill;

            var servicesSubtotal = Math.Max(totalBill - productsSubtotal, 0m);
            var servicesDeposit = Math.Round(servicesSubtotal * rate / 100m, MidpointRounding.AwayFromZero);
            var depositAmount = productsSubtotal + servicesDeposit;

            return new DepositBreakdown
            {
                TotalBill = totalBill,
                ProductsSubtotal = productsSubtotal,
                ServicesSubtotal = servicesSubtotal,
                ServicesDeposit = servicesDeposit,
                DepositAmount = depositAmount,
                RemainingAmount = Math.Max(totalBill - depositAmount, 0m),
                Description = string.Format(DepositDescriptionTemplate, rate.ToString("0.##"))
            };
        }

        /// <summary>
        /// Phần trăm dịch vụ thu trước, đọc từ <c>AdditionalServiceFee</c> mã
        /// <c>PURCHASE_SERVICE_DEPOSIT_RATE</c> (cùng cách ký gửi đọc <c>DEPOSIT_RATE</c>).
        /// </summary>
        private async Task<decimal> GetServiceDepositRateAsync()
        {
            var fees = await _unitOfWork.Repository<AdditionalServiceFee>().GetAllAsync();
            var rateFee = fees.FirstOrDefault(f =>
                f.IsActive && string.Equals(f.FeeCode, "PURCHASE_SERVICE_DEPOSIT_RATE", StringComparison.OrdinalIgnoreCase));

            if (rateFee == null) return DefaultServiceDepositRate;

            var value = rateFee.Value;
            if (value < 0m) value = 0m;
            if (value > 100m) value = 100m;
            return value;
        }

        private static bool IsPaid(PurchasePayment payment)
            => string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Tạo link thanh toán (payOS hoặc SePay) dùng chung cho cả đợt đầu lẫn đợt cuối.
        /// </summary>
        private async Task<(string CheckoutUrl, string PaymentLinkId, string PaymentMethod)> CreatePaymentLinkAsync(
            long orderCode, decimal amount, string purchaseCode, string? requestedMethod, string? requestedReturnUrl,
            string? requestedCancelUrl)
        {
            var amountInt = (int)Math.Round(amount, MidpointRounding.AwayFromZero);

            if (string.Equals(requestedMethod, "SEPAY", StringComparison.OrdinalIgnoreCase))
            {
                return (PaymentReturnUrls.SePayCheckoutUrl(_configuration, orderCode, requestedReturnUrl, requestedCancelUrl),
                    $"SEPAY-{orderCode}", "SEPAY");
            }

            var description = BuildDescription(purchaseCode);

            // URL web gửi (đã lọc origin) hoặc URL cũ /history/buy-on-behalf; luôn kèm orderCode + status.
            var (returnUrl, cancelUrl) = PaymentReturnUrls.Build(
                _configuration, requestedReturnUrl, requestedCancelUrl, PaymentReturnUrls.PurchaseHistoryPath, orderCode);
            var items = new List<PayOsItem> { new PayOsItem(BuildItemName(purchaseCode), 1, amountInt) };

            PayOsPaymentLink link = await _payOsService.CreatePaymentLinkAsync(
                orderCode, amountInt, description, returnUrl, cancelUrl, items);

            return (link.CheckoutUrl, link.PaymentLinkId, "PAYOS");
        }

        public async Task<bool> HandlePayOsWebhookAsync(string rawJsonBody)
        {
            // 1. Xác thực chữ ký + parse dữ liệu (ném lỗi nếu chữ ký sai)
            var result = _payOsService.VerifyWebhook(rawJsonBody);

            // 2. Tìm khoản thanh toán theo orderCode
            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode == result.OrderCode);
            if (payment == null)
            {
                // Có thể là webhook test của payOS (orderCode không thuộc hệ thống) -> bỏ qua nhưng vẫn ack
                return false;
            }

            // 3. Bỏ qua nếu đã xử lý xong (idempotent)
            if (string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!result.Success)
            {
                _logger.LogWarning(
                    "PayOS webhook reports unsuccessful payment for OrderCode={OrderCode}. Code={Code} Desc={Desc}",
                    result.OrderCode, result.Code, result.Desc);
                return false;
            }

            // 4. Cập nhật thanh toán thành công + đẩy yêu cầu mua hộ sang PAID
            await MarkPaymentAsPaidAsync(payment, result.Reference);
            return true;
        }

        public async Task<string> SyncPaymentStatusAsync(long orderCode)
        {
            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode == orderCode);
            if (payment == null)
            {
                throw new NotFoundException($"Không tìm thấy khoản thanh toán mua hộ với mã đơn {orderCode}.");
            }

            // Đã ghi nhận rồi thì thôi (idempotent, giống webhook)
            if (string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return "PAID";
            }

            // Hỏi thẳng payOS thay vì chờ webhook gọi về
            var status = await _payOsService.GetPaymentStatusAsync(orderCode);
            _logger.LogInformation(
                "Synced payOS status for OrderCode={OrderCode}: Status={Status} AmountPaid={AmountPaid}",
                orderCode, status.Status, status.AmountPaid);

            if (string.Equals(status.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                await MarkPaymentAsPaidAsync(payment, status.Reference);
                return "PAID";
            }

            // CANCELLED / EXPIRED / PENDING -> ghi nhận trạng thái, chưa đổi nghiệp vụ
            return status.Status;
        }

        /// <summary>
        /// Ghi nhận thanh toán thành công + đẩy yêu cầu mua hộ sang PAID.
        /// Dùng chung cho cả webhook lẫn sync chủ động để nghiệp vụ không bị lệch.
        /// </summary>
        private async Task MarkPaymentAsPaidAsync(PurchasePayment payment, string reference)
        {
            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            payment.Status = "PAID";
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionCode = reference;
            paymentRepo.Update(payment);

            await PurchasePaymentEffects.ApplyPaidAsync(_unitOfWork, payment);

            await _unitOfWork.SaveChangesAsync();
        }

        private static async Task<long> GenerateUniqueOrderCode(IEnumerable<PurchasePayment> existing)
        {
            await Task.CompletedTask;
            var used = new HashSet<long>(existing.Select(p => p.OrderCode));
            var random = new Random();
            long code;
            do
            {
                // yyMMddHHmmss (12 số) + 2 số ngẫu nhiên -> nằm dưới giới hạn số nguyên an toàn của payOS
                var baseCode = long.Parse(DateTime.UtcNow.ToString("yyMMddHHmmss"));
                code = baseCode * 100 + random.Next(0, 100);
            } while (used.Contains(code));
            return code;
        }

        private static string BuildDescription(string purchaseCode)
        {
            var desc = string.IsNullOrWhiteSpace(purchaseCode) ? "Thanh toan mua ho" : $"TT {purchaseCode}";
            // payOS giới hạn mô tả tối đa 25 ký tự
            return desc.Length > 25 ? desc.Substring(0, 25) : desc;
        }

        private static string BuildItemName(string purchaseCode)
        {
            return string.IsNullOrWhiteSpace(purchaseCode) ? "Đơn mua hộ" : $"Mua hộ {purchaseCode}";
        }

        public async Task<bool> HandleSePayWebhookAsync(SePayWebhookDto dto)
        {
            if (dto == null) return false;

            long? orderCode = null;

            // 1. Tìm trong Content theo format VCL + số (ví dụ: VCL26072212545433)
            if (!string.IsNullOrWhiteSpace(dto.Content))
            {
                var vclMatch = System.Text.RegularExpressions.Regex.Match(dto.Content, @"VCL(\d{10,18})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (vclMatch.Success && long.TryParse(vclMatch.Groups[1].Value, out var parsedVclCode))
                {
                    orderCode = parsedVclCode;
                }
            }

            // 2. Tìm trong Code theo format VCL + số (ví dụ: VCL26072212545433)
            if (!orderCode.HasValue && !string.IsNullOrWhiteSpace(dto.Code))
            {
                var vclMatch = System.Text.RegularExpressions.Regex.Match(dto.Code, @"VCL(\d{10,18})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (vclMatch.Success && long.TryParse(vclMatch.Groups[1].Value, out var parsedVclCode))
                {
                    orderCode = parsedVclCode;
                }
            }

            // 3. Fallback: tìm dãy số 10-18 chữ số đầu tiên trong Code
            if (!orderCode.HasValue && !string.IsNullOrWhiteSpace(dto.Code))
            {
                var match = System.Text.RegularExpressions.Regex.Match(dto.Code, @"\d{10,18}");
                if (match.Success && long.TryParse(match.Value, out var parsedCode))
                {
                    orderCode = parsedCode;
                }
            }

            // 4. Fallback: tìm dãy số 10-18 chữ số đầu tiên trong Content
            if (!orderCode.HasValue && !string.IsNullOrWhiteSpace(dto.Content))
            {
                var match = System.Text.RegularExpressions.Regex.Match(dto.Content, @"\d{10,18}");
                if (match.Success && long.TryParse(match.Value, out var parsedContentCode))
                {
                    orderCode = parsedContentCode;
                }
            }

            if (!orderCode.HasValue)
            {
                _logger.LogWarning("SePay Webhook (Mua hộ): Không tìm thấy orderCode trong Code={Code} hoặc Content={Content}", dto.Code, dto.Content);
                return false;
            }

            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode == orderCode.Value);
            if (payment == null)
            {
                _logger.LogWarning("SePay Webhook (Mua hộ): Không tìm thấy khoản thanh toán với OrderCode={OrderCode}", orderCode.Value);
                return false;
            }

            if (string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (dto.TransferAmount < payment.Amount)
            {
                _logger.LogWarning(
                    "SePay Webhook (Mua hộ): Số tiền thanh toán ({TransferAmount}) nhỏ hơn số tiền yêu cầu ({PaymentAmount}) cho OrderCode={OrderCode}",
                    dto.TransferAmount, payment.Amount, orderCode.Value);
                return false;
            }

            // Dùng chung đường ghi nhận với payOS/sync để trạng thái nghiệp vụ không bị lệch
            await MarkPaymentAsPaidAsync(payment, dto.ReferenceCode);
            return true;
        }
    }
}
