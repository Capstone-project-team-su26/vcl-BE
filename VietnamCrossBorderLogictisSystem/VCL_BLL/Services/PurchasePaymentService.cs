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

        /// <summary>
        /// Đợt 1 (dữ liệu cũ): khách trả toàn bộ giá trị báo giá. Giữ lại để đọc được các bản ghi
        /// đã tạo trước khi chuyển sang thu cọc — không dùng cho khoản thanh toán mới.
        /// </summary>
        private const string PaymentTypeFull = "FULL_PAYMENT";

        /// <summary>Đợt 1: cọc 100% tiền hàng + phần trăm cấu hình của phí dịch vụ &amp; cước.</summary>
        private const string PaymentTypeDeposit = "DEPOSIT";

        /// <summary>Phần trăm phí dịch vụ &amp; cước thu ở đợt cọc. Khớp với web khách hàng.</summary>
        private const decimal DefaultServiceDepositRate = 50m;

        private const string DepositDescriptionTemplate = "100% tiền hàng + {0}% phí dịch vụ & cước";

        /// <summary>Đợt 2: chi phí phát sinh sau khi hàng về (lưu kho, giao lại...).</summary>
        private const string PaymentTypeFinal = "FINAL_PAYMENT";

        // Các role được phép tạo đợt thanh toán cuối
        private static readonly string[] StaffRoles =
            { "ADMIN", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS" };

        // Đơn mua hàng phải tới bước này thì mới được chốt chi phí phát sinh
        private const string PurchaseOrderReadyForFinal = "WAITING_WAREHOUSE_RECEIVE";

        /// <summary>
        /// Trạng thái của chính yêu cầu mua hộ cho phép chốt chi phí phát sinh — hàng đã vào kho.
        /// Dùng khi luồng không đi qua thực thể PurchaseOrder.
        /// </summary>
        private static readonly HashSet<string> RequestStatusesReadyForFinal =
            new(StringComparer.OrdinalIgnoreCase) { "STORED", "DELIVERED", "CUSTOMER_CONFIRMED" };

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
                    $"Báo giá không còn ở trạng thái chờ xác nhận (hiện tại: {quotation.Status}). Không thể xác nhận lại.");
            }

            // 5. Sinh mã đơn hàng payOS duy nhất
            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var allPayments = await paymentRepo.GetAllAsync();
            long orderCode = await GenerateUniqueOrderCode(allPayments);

            // 6. Tạo link thanh toán theo phương thức đã chọn — đợt đầu chỉ thu tiền cọc
            var deposit = await BuildDepositBreakdownAsync(quotation);
            if (deposit.DepositAmount <= 0m)
            {
                throw new ArgumentException("Số tiền cọc phải lớn hơn 0. Vui lòng kiểm tra lại báo giá.");
            }

            var purchaseCode = purchaseRequest.PurchaseCode ?? string.Empty;
            var (checkoutUrl, paymentLinkId, paymentMethodString) = await CreatePaymentLinkAsync(
                orderCode, deposit.DepositAmount, purchaseCode, request.PaymentMethod, request.ReturnUrl);

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
                PaymentType = PaymentTypeDeposit,
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
                    Title = "Khởi tạo thanh toán cọc mua hộ",
                    Content = $"Khoản cọc {deposit.DepositAmount:#,##0}đ cho yêu cầu mua hộ (Mã: {purchaseCode}) đã được khởi tạo thành công.",
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

        public async Task<CreatePurchaseFinalPaymentResponseDto> CreateFinalPaymentAsync(
            Guid requestId, Guid userId, string userRole, CreatePurchaseFinalPaymentRequestDto request)
        {
            request ??= new CreatePurchaseFinalPaymentRequestDto();

            // 1. Phân quyền: chỉ Staff/Sales được chốt chi phí phát sinh
            if (!StaffRoleHelper.CanHandlePayments(userRole))
            {
                throw new UnauthorizedAccessException("Chỉ Staff hoặc Sales được tạo thanh toán cuối cho đơn mua hộ.");
            }

            // 2. Tìm yêu cầu mua hộ
            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(requestId);
            if (purchaseRequest == null)
            {
                throw new NotFoundException($"Không tìm thấy yêu cầu mua hộ với ID {requestId}");
            }

            var currentStatus = purchaseRequest.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (currentStatus == "COMPLETED")
            {
                throw new ArgumentException("Đơn mua hộ đã hoàn tất thanh toán.");
            }
            if (currentStatus == "CANCELLED" || currentStatus == "REJECTED")
            {
                throw new ArgumentException($"Không thể tạo thanh toán cuối do đơn đang ở trạng thái {purchaseRequest.Status}.");
            }

            // 3. Phải thanh toán xong đợt đầu (toàn bộ giá trị báo giá) mới được chốt phát sinh
            var paymentRepo = _unitOfWork.Repository<PurchasePayment>();
            var allPayments = await paymentRepo.GetAllAsync();
            var requestPayments = allPayments.Where(p => p.PurchaseRequestId == requestId).ToList();

            // Đơn mới trả cọc (DEPOSIT); đơn tạo trước khi đổi công thức trả FULL_PAYMENT — chấp nhận cả hai.
            bool firstInstalmentDone = requestPayments.Any(p =>
                (IsPaymentType(p, PaymentTypeDeposit) || IsPaymentType(p, PaymentTypeFull)) && IsPaid(p));
            if (!firstInstalmentDone)
            {
                throw new ArgumentException("Chưa thể tạo thanh toán cuối: khách chưa thanh toán đợt cọc.");
            }

            // 4. Không cho trùng đợt cuối
            if (requestPayments.Any(p => IsPaymentType(p, PaymentTypeFinal) && IsPaid(p)))
            {
                throw new ArgumentException("Đơn mua hộ đã hoàn tất thanh toán cuối.");
            }
            if (requestPayments.Any(p => IsPaymentType(p, PaymentTypeFinal)
                && string.Equals(p.Status, "PENDING", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Đơn mua hộ đã có một đợt thanh toán cuối đang chờ khách thanh toán.");
            }

            // 5. Điều kiện trigger: hàng đã về tới kho.
            // Có hai cách theo dõi tiến độ mua song song trong hệ thống:
            //   - PURCHASE_ORDERS.status (PURCHASE_ORDER_CREATED → ... → WAITING_WAREHOUSE_RECEIVE)
            //   - PURCHASE_REQUESTS.status (… → WAITING_STORED → STORED) do PUT .../confirm-purchase đặt
            // Luồng đang chạy thật chỉ dùng cách thứ hai — không nơi nào trong luồng tạo PurchaseOrder,
            // nên chỉ canh WAITING_WAREHOUSE_RECEIVE thì thanh toán cuối vĩnh viễn không mở được.
            // Chấp nhận cả hai để không phá đường cũ mà đường đang dùng vẫn đi được.
            var purchaseOrders = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Where(o => o.PurchaseRequestId == requestId
                    && !string.Equals(o.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool purchaseOrderReady = purchaseOrders.Any(o =>
                string.Equals(o.Status, PurchaseOrderReadyForFinal, StringComparison.OrdinalIgnoreCase));
            bool requestStored = RequestStatusesReadyForFinal.Contains(currentStatus);

            if (!purchaseOrderReady && !requestStored)
            {
                throw new ArgumentException(
                    "Chưa thể tạo thanh toán cuối: hàng chưa về tới kho. "
                    + $"Trạng thái đơn mua hộ hiện tại: {purchaseRequest.Status}.");
            }

            // 6. Lấy báo giá đã được khách xác nhận
            var quotationRepo = _unitOfWork.Repository<PurchaseQuotation>();
            var quotation = (await quotationRepo.GetAllAsync())
                .Where(q => q.PurchaseRequestId == requestId
                    && string.Equals(q.Status, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefault();

            if (quotation == null)
            {
                throw new ArgumentException("Đơn mua hộ chưa có báo giá được khách xác nhận.");
            }

            // 7. Ghi nhận chi phí phát sinh vào chính báo giá đã chốt + cộng dồn tổng bill
            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            decimal extrasTotal = 0m;

            foreach (var fee in request.ExtraFees ?? new List<ExtraFeeLineDto>())
            {
                if (fee.Amount <= 0m) continue;

                extrasTotal += fee.Amount;
                await feeRepo.AddAsync(new QuotationFee
                {
                    Id = Guid.NewGuid(),
                    PurchaseQuotationId = quotation.Id,
                    FeeName = fee.Name,
                    FeeType = "EXTRA",
                    CalculationType = "FIXED",
                    Value = fee.Amount,
                    Amount = fee.Amount,
                    Note = fee.Note,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var totalPaid = requestPayments.Where(IsPaid).Sum(p => p.Amount);
            var newGrandTotal = quotation.TotalAmount + extrasTotal;
            quotation.TotalAmount = newGrandTotal;
            quotationRepo.Update(quotation);

            var finalAmount = newGrandTotal - totalPaid;
            if (finalAmount < 0m) finalAmount = 0m;

            var purchaseCode = purchaseRequest.PurchaseCode ?? string.Empty;

            // 8a. Không phát sinh gì -> đơn hoàn tất luôn, không bắt khách thanh toán thêm
            if (finalAmount <= 0m)
            {
                var zeroPayment = new PurchasePayment
                {
                    Id = Guid.NewGuid(),
                    PurchaseRequestId = requestId,
                    PurchaseQuotationId = quotation.Id,
                    OrderCode = await GenerateUniqueOrderCode(allPayments),
                    Amount = 0m,
                    PaymentMethod = "NONE",
                    Status = "PAID",
                    PaymentType = PaymentTypeFinal,
                    TransactionCode = "NO_ADDITIONAL_PAYMENT",
                    PaidAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                await paymentRepo.AddAsync(zeroPayment);

                purchaseRequest.Status = "COMPLETED";
                requestRepo.Update(purchaseRequest);
                await _unitOfWork.SaveChangesAsync();

                return new CreatePurchaseFinalPaymentResponseDto
                {
                    PurchaseRequestId = requestId,
                    PurchaseCode = purchaseCode,
                    RequestStatus = purchaseRequest.Status,
                    QuotationId = quotation.Id,
                    TotalBillAmount = newGrandTotal,
                    AlreadyPaid = totalPaid,
                    ExtraFeesTotal = extrasTotal,
                    FinalAmount = 0m,
                    PaymentId = zeroPayment.Id,
                    OrderCode = null,
                    PaymentStatus = "PAID",
                    PaymentMethod = zeroPayment.PaymentMethod,
                    CheckoutUrl = null,
                    PaymentLinkId = null
                };
            }

            // 8b. Có phát sinh -> tạo link thanh toán đợt cuối
            long orderCode = await GenerateUniqueOrderCode(allPayments);
            var (checkoutUrl, paymentLinkId, paymentMethodString) = await CreatePaymentLinkAsync(
                orderCode, finalAmount, purchaseCode, request.PaymentMethod, request.ReturnUrl);

            var finalPayment = new PurchasePayment
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                PurchaseQuotationId = quotation.Id,
                OrderCode = orderCode,
                Amount = finalAmount,
                PaymentMethod = paymentMethodString,
                Status = "PENDING",
                PaymentType = PaymentTypeFinal,
                PaymentLinkId = paymentLinkId,
                CheckoutUrl = checkoutUrl,
                CreatedAt = DateTime.UtcNow
            };
            await paymentRepo.AddAsync(finalPayment);

            purchaseRequest.Status = "WAITING_FINAL_PAYMENT";
            requestRepo.Update(purchaseRequest);
            await _unitOfWork.SaveChangesAsync();

            return new CreatePurchaseFinalPaymentResponseDto
            {
                PurchaseRequestId = requestId,
                PurchaseCode = purchaseCode,
                RequestStatus = purchaseRequest.Status,
                QuotationId = quotation.Id,
                TotalBillAmount = newGrandTotal,
                AlreadyPaid = totalPaid,
                ExtraFeesTotal = extrasTotal,
                FinalAmount = finalAmount,
                PaymentId = finalPayment.Id,
                OrderCode = finalPayment.OrderCode,
                PaymentStatus = finalPayment.Status,
                PaymentMethod = finalPayment.PaymentMethod,
                CheckoutUrl = finalPayment.CheckoutUrl,
                PaymentLinkId = finalPayment.PaymentLinkId
            };
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

            var deposit = quotation != null ? await BuildDepositBreakdownAsync(quotation) : null;

            // Cọc đã tạo thì lấy đúng số đã thu, KHÔNG tính lại theo bill hiện tại: chi phí phát sinh
            // làm tổng bill tăng lên, tính lại sẽ ra một con số khách chưa từng trả.
            var depositPayment = payments.FirstOrDefault(p =>
                IsPaymentType(p, PaymentTypeDeposit) || IsPaymentType(p, PaymentTypeFull));
            var depositAmount = depositPayment?.Amount ?? deposit?.DepositAmount ?? 0m;

            return new PurchasePaymentHistoryResponseDto
            {
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
                Payments = payments.Select(p => new PurchasePaymentHistoryItemDto
                {
                    PaymentId = p.Id,
                    OrderCode = p.OrderCode,
                    PaymentType = string.IsNullOrWhiteSpace(p.PaymentType) ? PaymentTypeFull : p.PaymentType,
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

        /// <summary>
        /// Cọc mua hộ = 100% tiền hàng + N% phần còn lại của bill (phí mua hộ, cước, thuế, phụ phí).
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

        /// <summary>
        /// Các trạng thái nằm SAU khi đã trả cọc trong vòng đời mua hộ (Sale bấm tay ở popup
        /// "Xác nhận mua hộ"). Webhook thanh toán không được ghi đè lên chúng.
        /// </summary>
        private static readonly string[] PurchaseProgressStatuses =
            { "PURCHASED", "SELLER_SHIPPED", "ARRIVED_ORIGIN_WAREHOUSE" };

        private static bool IsPurchaseProgressStatus(string? status) =>
            !string.IsNullOrWhiteSpace(status)
            && PurchaseProgressStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);

        private static bool IsPaid(PurchasePayment payment)
            => string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Dữ liệu cũ (trước khi có cột payment_type) để trống -> coi như đợt thanh toán đầu.
        /// </summary>
        private static bool IsPaymentType(PurchasePayment payment, string type)
        {
            var actual = string.IsNullOrWhiteSpace(payment.PaymentType) ? PaymentTypeFull : payment.PaymentType;
            return string.Equals(actual, type, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Tạo link thanh toán (payOS hoặc SePay) dùng chung cho cả đợt đầu lẫn đợt cuối.
        /// </summary>
        private async Task<(string CheckoutUrl, string PaymentLinkId, string PaymentMethod)> CreatePaymentLinkAsync(
            long orderCode, decimal amount, string purchaseCode, string? requestedMethod, string? requestedReturnUrl)
        {
            var amountInt = (int)Math.Round(amount, MidpointRounding.AwayFromZero);

            if (string.Equals(requestedMethod, "SEPAY", StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = _configuration["SePay:BaseUrl"] ?? string.Empty;
                return ($"{baseUrl}/api/payments/sepay/checkout/{orderCode}", $"SEPAY-{orderCode}", "SEPAY");
            }

            var description = BuildDescription(purchaseCode);

            var inputUrl = FirstNonEmpty(requestedReturnUrl, _configuration["PayOS:ReturnUrl"], string.Empty);
            var isLocal = inputUrl.Contains("localhost") || inputUrl.Contains("127.0.0.1") || inputUrl.Contains("5173") || inputUrl.Contains("3000");
            var baseFrontendUrl = isLocal ? "http://localhost:5173" : "https://logictic.site";

            var returnUrl = $"{baseFrontendUrl}/history/buy-on-behalf";
            var items = new List<PayOsItem> { new PayOsItem(BuildItemName(purchaseCode), 1, amountInt) };

            PayOsPaymentLink link = await _payOsService.CreatePaymentLinkAsync(
                orderCode, amountInt, description, returnUrl, returnUrl, items);

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

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(payment.PurchaseRequestId);
            if (purchaseRequest != null)
            {
                // Đợt cuối trả xong -> kết thúc luồng mua hộ; đợt đầu -> chờ mua hàng & vận chuyển
                var paidStatus = IsPaymentType(payment, PaymentTypeFinal) ? "COMPLETED" : "PAID";

                // Không kéo lùi tiến độ: Sale có thể đã bấm PURCHASED/SELLER_SHIPPED/ARRIVED trước khi
                // tiền về (webhook có thể tới muộn). Ghi đè lúc đó sẽ làm đơn tụt trạng thái.
                // COMPLETED vẫn được đặt vì đó là điểm kết thúc của cả luồng.
                if (paidStatus == "COMPLETED" || !IsPurchaseProgressStatus(purchaseRequest.Status))
                {
                    purchaseRequest.Status = paidStatus;
                    requestRepo.Update(purchaseRequest);
                }
            }

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

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
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
