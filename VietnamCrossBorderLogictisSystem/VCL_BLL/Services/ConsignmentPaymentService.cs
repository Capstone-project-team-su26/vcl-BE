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
    public class ConsignmentPaymentService : IConsignmentPaymentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPayOsService _payOsService;
        private readonly ISePayService _sePayService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ConsignmentPaymentService> _logger;
        private readonly IStorageFeeService _storageFeeService;

        /// <summary>Mọi đường ghi nhận tiền thực nhận đi qua một chỗ.</summary>
        private readonly IPaymentConfirmationService _paymentConfirmation;

        // Role nội bộ được tạo thanh toán cuối / xem lịch sử thanh toán.
        private static readonly string[] StaffRoles =
            { "ADMIN", "ADMINISTRATOR", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS" };

        // Tỷ lệ cọc mặc định nếu Admin chưa cấu hình DEPOSIT_RATE.
        private const decimal DefaultDepositRate = 50m;

        // Trạng thái kiện đã về kho đích - điều kiện cho phép tạo thanh toán cuối.
        private const string ParcelReceivedAtDestination = "RECEIVED_AT_DESTINATION";

        // Sale xác nhận lô đã về kho VN (PUT /international-shipments/{id}/arrive) thì kiện dừng ở
        // ARRIVED_DESTINATION, chưa qua bước kho mở lô nhận kiện. Anh Hoàng chốt 11/08: chính mốc
        // sale bấm là mốc gọi khách tất toán phần còn lại, nên mốc này phải mở được thanh toán cuối.
        private const string ParcelArrivedAtDestination = "ARRIVED_DESTINATION";

        // Thanh toán offline (chuyển khoản tay): chờ Admin đối soát sao kê rồi mới xác nhận.
        private const string PaymentMethodOffline = "OFFLINE";
        private const string PendingReconciliation = "PENDING_RECONCILIATION";

        private readonly IVnSettlementService _vnSettlementService;

        public ConsignmentPaymentService(
            IUnitOfWork unitOfWork,
            IPayOsService payOsService,
            ISePayService sePayService,
            IConfiguration configuration,
            ILogger<ConsignmentPaymentService> logger,
            IStorageFeeService storageFeeService,
            IPaymentConfirmationService paymentConfirmation,
            IVnSettlementService vnSettlementService)
        {
            _vnSettlementService = vnSettlementService;
            _unitOfWork = unitOfWork;
            _payOsService = payOsService;
            _sePayService = sePayService;
            _configuration = configuration;
            _logger = logger;
            _storageFeeService = storageFeeService;
            _paymentConfirmation = paymentConfirmation;
        }

        // ---------------------------------------------------------------------
        // ĐỢT 1: Customer xác nhận báo giá -> tạo khoản CỌC (DEPOSIT)
        // ---------------------------------------------------------------------
        public async Task<ConfirmQuotationPaymentResponse> ConfirmQuotationAndPayAsync(
            Guid quotationId, Guid userId, ConfirmQuotationPaymentRequest request)
        {
            request ??= new ConfirmQuotationPaymentRequest();

            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);
            if (quotation == null)
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");

            // Khách chỉ xác nhận báo giá Sale đã gửi, còn hiệu lực và không chờ duyệt giá.
            QuotationAcceptanceRules.EnsureCustomerCanAccept(quotation);

            // Không cho tạo trùng khoản cọc offline đang chờ Admin đối soát cho cùng báo giá
            if (await HasPendingOfflineDepositAsync(quotationId))
            {
                throw new ArgumentException(
                    "Báo giá này đã có khoản cọc chuyển khoản đang chờ Admin đối soát.");
            }

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);
            if (order == null)
                throw new NotFoundException("Không tìm thấy đơn hàng liên quan đến báo giá.");

            var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.Id == order.CustomerId);
            if (customer == null || customer.UserId != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận báo giá này.");

            // Tổng bill = cước + phí dịch vụ (đã gồm phụ phí) + thuế
            var totalAmount = (quotation.EstimatedFreightCharge ?? 0m)
                              + (quotation.ServiceFee ?? 0m)
                              + (quotation.TaxAndDuty ?? 0m);
            if (totalAmount <= 0m)
                throw new ArgumentException("Tổng tiền báo giá không hợp lệ để tạo thanh toán.");

            // Tính tiền cọc theo tỷ lệ Admin cấu hình
            var depositRate = await GetDepositRateAsync();
            var depositAmount = Math.Round(totalAmount * depositRate / 100m, MidpointRounding.AwayFromZero);

            // Cập nhật báo giá -> ACCEPTED, lưu người và thời điểm khách xác nhận.
            var acceptedAt = DateTime.UtcNow;
            quotation.Status = "ACCEPTED";
            quotation.AcceptedById = userId;
            quotation.AcceptedAt = acceptedAt;
            quotationRepo.Update(quotation);

            await OrderHistoryWriter.RecordAsync(_unitOfWork, order.Id, order.Status, "WAITING_DEPOSIT",
                "QUOTATION_ACCEPTED",
                $"Khách xác nhận báo giá {quotation.Id} ({StatusNames.Quote(quotation.QuoteType)}), tổng {totalAmount:#,##0}đ.",
                userId);

            // Hóa đơn cho TOÀN BỘ bill (cọc là thanh toán một phần của hóa đơn này)
            var invoiceRepo = _unitOfWork.Repository<Invoice>();
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                InvoiceNo = $"INV-{DateTime.UtcNow:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                TotalAmount = totalAmount,
                Currency = "VND",
                InvoiceStatus = "PENDING",
                IssuedAt = DateTime.UtcNow
            };
            await invoiceRepo.AddAsync(invoice);

            var invoiceItemRepo = _unitOfWork.Repository<InvoiceItem>();
            await AddInvoiceItemAsync(invoiceItemRepo, invoice.Id, "FREIGHT", quotation.EstimatedFreightCharge, "Cước vận chuyển dự kiến");
            await AddInvoiceItemAsync(invoiceItemRepo, invoice.Id, "SERVICE_FEE", quotation.ServiceFee, "Phí dịch vụ (đã gồm phụ phí)");
            await AddInvoiceItemAsync(invoiceItemRepo, invoice.Id, "TAX_AND_DUTY", quotation.TaxAndDuty, "Thuế và phí hải quan");

            var consignmentCode = order.ConsignmentCode ?? string.Empty;
            var paymentRepo = _unitOfWork.Repository<Payment>();

            // Edge: tỷ lệ cọc Admin cấu hình = 0% -> khoản cọc 0đ được hệ thống xác nhận ngay.
            // Vẫn đi qua luồng xác nhận chung để đơn sang DEPOSIT_PAID và Sale được báo lập phiếu như mọi đơn.
            if (depositAmount <= 0m)
            {
                order.Status = "WAITING_DEPOSIT";
                orderRepo.Update(order);

                var noDeposit = new Payment
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Amount = 0m,
                    PaymentMethod = "PAYOS",
                    PaymentStatus = "PENDING",
                    InstallmentType = "DEPOSIT",
                    CreatedAt = DateTime.UtcNow
                };
                await paymentRepo.AddAsync(noDeposit);
                await _unitOfWork.SaveChangesAsync();

                await _paymentConfirmation.ConfirmAsync(noDeposit, new PaymentConfirmationInput
                {
                    Source = PaymentConfirmationSources.SystemZero,
                    ReceivedAmount = 0m,
                    Note = "Tỷ lệ cọc cấu hình 0%."
                });

                return new ConfirmQuotationPaymentResponse
                {
                    QuotationId = quotation.Id,
                    QuotationStatus = quotation.Status,
                    OrderId = order.Id,
                    OrderStatus = order.Status,
                    ConsignmentCode = consignmentCode,
                    InvoiceId = invoice.Id,
                    InvoiceNo = invoice.InvoiceNo,
                    PaymentId = noDeposit.Id,
                    OrderCode = 0,
                    Amount = 0m,
                    InstallmentType = "DEPOSIT",
                    TotalBillAmount = totalAmount,
                    DepositRate = depositRate,
                    PaymentMethod = "PAYOS",
                    PaymentStatus = "PAID",
                    CheckoutUrl = null,
                    PaymentLinkId = null
                };
            }

            // Tạo link thanh toán theo phương thức đã chọn
            long orderCode = await GenerateUniqueOrderCodeAsync();
            var amountInt = (int)Math.Round(depositAmount, MidpointRounding.AwayFromZero);
            var isOffline = string.Equals(request.PaymentMethod, PaymentMethodOffline, StringComparison.OrdinalIgnoreCase);
            var isSePay = string.Equals(request.PaymentMethod, "SEPAY", StringComparison.OrdinalIgnoreCase);

            string checkoutUrl;
            string paymentLinkId;
            string paymentMethodString;

            if (isOffline)
            {
                // Chuyển khoản tay: KHÔNG tạo link cổng thanh toán, chờ Admin đối soát sao kê
                paymentMethodString = PaymentMethodOffline;
                paymentLinkId = null;
                checkoutUrl = null;
            }
            else if (isSePay)
            {
                paymentMethodString = "SEPAY";
                paymentLinkId = $"SEPAY-{orderCode}";
                checkoutUrl = PaymentReturnUrls.SePayCheckoutUrl(_configuration, orderCode, request.ReturnUrl, request.CancelUrl);
            }
            else
            {
                paymentMethodString = "PAYOS";
                var description = BuildDescription("Coc", consignmentCode);

                // URL web gửi (đã lọc origin) hoặc URL cũ; luôn kèm orderCode + status.
                var (returnUrl, cancelUrl) = PaymentReturnUrls.Build(
                    _configuration, request.ReturnUrl, request.CancelUrl, PaymentReturnUrls.ConsignmentHistoryPath, orderCode);
                var items = new List<PayOsItem> { new PayOsItem($"Cọc {consignmentCode}", 1, amountInt) };

                PayOsPaymentLink linkResult = await _payOsService.CreatePaymentLinkAsync(
                    orderCode, amountInt, description, returnUrl, cancelUrl, items);

                checkoutUrl = linkResult.CheckoutUrl;
                paymentLinkId = linkResult.PaymentLinkId;
            }

            // Offline chờ Admin đối soát; cổng online chờ webhook/sync báo về
            var depositStatus = isOffline ? PendingReconciliation : "PENDING";

            var deposit = new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = depositAmount,
                PaymentMethod = paymentMethodString,
                PaymentStatus = depositStatus,
                InstallmentType = "DEPOSIT",
                OrderCode = orderCode,
                CheckoutUrl = checkoutUrl,
                PaymentLinkId = paymentLinkId,
                CreatedAt = DateTime.UtcNow
            };
            await paymentRepo.AddAsync(deposit);

            order.Status = "WAITING_DEPOSIT";
            orderRepo.Update(order);

            try
            {
                await _unitOfWork.Repository<Notification>().AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = "Khởi tạo thanh toán cọc",
                    Content = $"Khởi tạo khoản cọc {depositAmount:#,##0}đ cho đơn ký gửi {consignmentCode} thành công.",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tạo thông báo cọc cho đơn {ConsignmentCode}", consignmentCode);
            }

            await _unitOfWork.SaveChangesAsync();

            return new ConfirmQuotationPaymentResponse
            {
                QuotationId = quotation.Id,
                QuotationStatus = quotation.Status,
                OrderId = order.Id,
                OrderStatus = order.Status,
                ConsignmentCode = consignmentCode,
                InvoiceId = invoice.Id,
                InvoiceNo = invoice.InvoiceNo,
                PaymentId = deposit.Id,
                OrderCode = orderCode,
                Amount = depositAmount,
                InstallmentType = "DEPOSIT",
                TotalBillAmount = totalAmount,
                DepositRate = depositRate,
                PaymentMethod = paymentMethodString,
                PaymentStatus = depositStatus,
                CheckoutUrl = checkoutUrl,
                PaymentLinkId = paymentLinkId
            };
        }

        /// <summary>
        /// Kiểm tra báo giá đã có khoản cọc offline đang chờ Admin đối soát hay chưa
        /// (tránh khách bấm xác nhận nhiều lần tạo ra nhiều khoản cọc treo).
        /// </summary>
        private async Task<bool> HasPendingOfflineDepositAsync(Guid quotationId)
        {
            var invoiceIds = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && i.QuotationId.Value == quotationId)
                .Select(i => i.Id)
                .ToHashSet();

            if (invoiceIds.Count == 0)
                return false;

            return (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Any(p => p.InvoiceId.HasValue
                          && invoiceIds.Contains(p.InvoiceId.Value)
                          && IsInstallment(p, "DEPOSIT")
                          && string.Equals(p.PaymentStatus, PendingReconciliation, StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------------
        // ĐỢT 2: Sale tạo thanh toán cuối (phần còn lại + phí phát sinh)
        // ---------------------------------------------------------------------
        public async Task<CreateFinalPaymentResponse> CreateFinalPaymentAsync(
            Guid orderId, Guid userId, string userRole, CreateFinalPaymentRequest request)
        {
            request ??= new CreateFinalPaymentRequest();

            if (!IsStaff(userRole))
                throw new UnauthorizedAccessException("Chỉ Sale/Staff được tạo thanh toán cuối cho đơn ký gửi.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            // Điều kiện trigger: đơn phải có kiện đã về kho đích. Kiện đi qua ba mốc và mốc nào
            // cũng là "đã về tới VN", nên phải nhận cả ba, nếu không thì đi đúng quy trình lại
            // không tạo được thanh toán cuối:
            //   ARRIVED_DESTINATION  - sale xác nhận lô về kho VN (mốc gọi khách tất toán)
            //   RECEIVED_AT_DESTINATION - kho mở lô, nhận từng kiện
            //   nằm trong ô kệ kho đích - kho quét xếp kệ xong (luồng có nhập kho)
            var allParcels = await _unitOfWork.Repository<Parcel>().GetAllAsync();
            var orderParcels = allParcels.Where(p => p.ConsignmentId == orderId).ToList();
            var destinationBinIds = await GetDestinationBinIdsAsync();

            bool anyAtDestination = orderParcels.Any(p =>
                string.Equals(p.PackageStatus, ParcelArrivedAtDestination, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.PackageStatus, ParcelReceivedAtDestination, StringComparison.OrdinalIgnoreCase)
                || (p.BinId.HasValue && destinationBinIds.Contains(p.BinId.Value)));
            if (!anyAtDestination)
                throw new ArgumentException("Chưa thể tạo thanh toán cuối: đơn chưa có kiện hàng về đến kho đích.");

            // Tất toán sau khi kho VN kiểm xong: tiền tính theo cân đo thật, và khách không trả
            // tiền cho kiện đang có sự cố chưa rõ kết quả.
            await _vnSettlementService.EnsureCanIssueAsync(orderId);

            // Lấy hóa đơn của đơn (qua báo giá của đơn)
            var (invoice, allOrderPayments) = await GetOrderInvoiceAndPaymentsAsync(orderId);
            if (invoice == null)
                throw new ArgumentException("Đơn hàng chưa có hóa đơn/thanh toán cọc.");

            // Phải đã thanh toán cọc thành công
            var depositPaid = allOrderPayments
                .Where(p => IsInstallment(p, "DEPOSIT") && IsPaid(p))
                .Sum(p => p.Amount ?? 0m);
            bool hasPaidDeposit = allOrderPayments.Any(p => IsInstallment(p, "DEPOSIT") && IsPaid(p));
            if (!hasPaidDeposit)
                throw new ArgumentException("Chưa thể tạo thanh toán cuối: đơn chưa thanh toán cọc thành công.");

            // Không cho trùng đợt cuối
            if (allOrderPayments.Any(p => IsInstallment(p, "FINAL_PAYMENT") && IsPaid(p)))
                throw new ArgumentException("Đơn hàng đã hoàn tất thanh toán cuối.");
            // Phí lưu kho chạy theo ngày nên số tiền thay đổi theo thời điểm chốt. Còn đợt cuối
            // nào đang chờ thì huỷ đi rồi phát hành lại, đúng như quy tắc "huỷ giao dịch cũ
            // tính lại" — khách không bao giờ phải trả một con số đã lỗi thời.
            var paymentRepoForReissue = _unitOfWork.Repository<Payment>();
            var stalePending = allOrderPayments
                .Where(p => IsInstallment(p, "FINAL_PAYMENT")
                            && string.Equals(p.PaymentStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var stale in stalePending)
            {
                stale.PaymentStatus = "CANCELLED";
                paymentRepoForReissue.Update(stale);
                _logger.LogInformation(
                    "Huỷ đợt thanh toán cuối cũ {PaymentId} của đơn {OrderId} để phát hành lại với số tiền mới.",
                    stale.Id, orderId);
            }

            // Ghi nhận chi phí phát sinh (InvoiceItem) + cập nhật tổng bill
            var invoiceItemRepo = _unitOfWork.Repository<InvoiceItem>();
            var existingItems = (await invoiceItemRepo.GetAllAsync())
                .Where(i => i.InvoiceId == invoice.Id)
                .ToList();

            decimal extrasTotal = 0m;
            foreach (var fee in request.ExtraFees ?? new List<ExtraFeeLineDto>())
            {
                if (fee.Amount <= 0m) continue;
                var desc = string.IsNullOrWhiteSpace(fee.Note) ? fee.Name : $"{fee.Name} - {fee.Note}";

                // Phát hành lại đợt cuối mà gửi kèm đúng khoản phí cũ thì không cộng lần hai.
                if (existingItems.Any(i => string.Equals(i.FeeType, "EXTRA", StringComparison.OrdinalIgnoreCase)
                                           && string.Equals(i.Description, desc, StringComparison.OrdinalIgnoreCase)
                                           && i.Amount == fee.Amount))
                {
                    continue;
                }

                extrasTotal += fee.Amount;
                await invoiceItemRepo.AddAsync(new InvoiceItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    FeeType = "EXTRA",
                    ReferenceType = "ORDER",
                    ReferenceId = orderId,
                    Amount = fee.Amount,
                    Currency = "VND",
                    Description = desc
                });
            }

            // Cước tính lại theo cân đo tại kho VN (có thể âm nếu hàng nhẹ hơn khai báo).
            var freightAdjustmentDelta = await _vnSettlementService.ApplyFreightAdjustmentAsync(
                orderId, invoice, existingItems, invoiceItemRepo);
            extrasTotal += freightAdjustmentDelta;

            // Phí lưu kho tại kho đích: hệ thống tự tính, không bắt nhân viên nhập tay.
            var storageDelta = await ApplyStorageFeeAsync(orderId, invoice, existingItems, invoiceItemRepo);
            extrasTotal += storageDelta;

            var originalTotal = invoice.TotalAmount ?? 0m;
            var newGrandTotal = originalTotal + extrasTotal;
            invoice.TotalAmount = newGrandTotal;

            // final = (tổng bill mới - đã trả cọc); clamp >= 0
            var finalAmount = newGrandTotal - depositPaid;
            if (finalAmount < 0m) finalAmount = 0m;

            var invoiceRepo = _unitOfWork.Repository<Invoice>();
            var paymentRepo = _unitOfWork.Repository<Payment>();
            var consignmentCode = order.ConsignmentCode ?? string.Empty;

            // Edge: final = 0 -> không cần thanh toán thêm, đơn PAID luôn
            if (finalAmount <= 0m)
            {
                invoice.InvoiceStatus = "PAID";
                invoiceRepo.Update(invoice);
                order.Status = "PAID";
                orderRepo.Update(order);

                var zeroFinal = new Payment
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Amount = 0m,
                    PaymentMethod = "PAYOS",
                    PaymentStatus = "PAID",
                    InstallmentType = "FINAL_PAYMENT",
                    PaidAt = DateTime.UtcNow,
                    TransactionCode = "NO_ADDITIONAL_PAYMENT",
                    CreatedAt = DateTime.UtcNow
                };
                await paymentRepo.AddAsync(zeroFinal);
                await _unitOfWork.SaveChangesAsync();

                return new CreateFinalPaymentResponse
                {
                    OrderId = order.Id,
                    ConsignmentCode = consignmentCode,
                    OrderStatus = order.Status,
                    InvoiceId = invoice.Id,
                    TotalBillAmount = newGrandTotal,
                    DepositPaid = depositPaid,
                    ExtraFeesTotal = extrasTotal,
                    FreightAdjustmentDelta = freightAdjustmentDelta,
                    FinalAmount = 0m,
                    PaymentId = zeroFinal.Id,
                    OrderCode = null,
                    PaymentStatus = "PAID",
                    CheckoutUrl = null,
                    PaymentLinkId = null
                };
            }

            // Tạo link thanh toán theo phương thức đã chọn
            long orderCode = await GenerateUniqueOrderCodeAsync();
            var amountInt = (int)Math.Round(finalAmount, MidpointRounding.AwayFromZero);
            var isSePay = string.Equals(request.PaymentMethod, "SEPAY", StringComparison.OrdinalIgnoreCase);

            string checkoutUrl;
            string paymentLinkId;
            string paymentMethodString;

            if (isSePay)
            {
                paymentMethodString = "SEPAY";
                paymentLinkId = $"SEPAY-{orderCode}";
                checkoutUrl = PaymentReturnUrls.SePayCheckoutUrl(_configuration, orderCode, request.ReturnUrl, request.CancelUrl);
            }
            else
            {
                paymentMethodString = "PAYOS";
                var description = BuildDescription("Cuoi", consignmentCode);

                var (returnUrl, cancelUrl) = PaymentReturnUrls.Build(
                    _configuration, request.ReturnUrl, request.CancelUrl, PaymentReturnUrls.ConsignmentHistoryPath, orderCode);
                var items = new List<PayOsItem> { new PayOsItem($"TT cuoi {consignmentCode}", 1, amountInt) };

                PayOsPaymentLink linkResult = await _payOsService.CreatePaymentLinkAsync(
                    orderCode, amountInt, description, returnUrl, cancelUrl, items);

                checkoutUrl = linkResult.CheckoutUrl;
                paymentLinkId = linkResult.PaymentLinkId;
            }

            var finalPayment = new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = finalAmount,
                PaymentMethod = paymentMethodString,
                PaymentStatus = "PENDING",
                InstallmentType = "FINAL_PAYMENT",
                OrderCode = orderCode,
                CheckoutUrl = checkoutUrl,
                PaymentLinkId = paymentLinkId,
                CreatedAt = DateTime.UtcNow
            };
            await paymentRepo.AddAsync(finalPayment);

            invoiceRepo.Update(invoice);
            order.Status = "WAITING_FINAL_PAYMENT";
            orderRepo.Update(order);

            if (order.CustomerId.HasValue)
            {
                try
                {
                    var cust = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                        .FirstOrDefault(c => c.Id == order.CustomerId.Value);
                    if (cust != null && cust.UserId != Guid.Empty)
                    {
                        await _unitOfWork.Repository<Notification>().AddAsync(new Notification
                        {
                            Id = Guid.NewGuid(),
                            UserId = cust.UserId,
                            Title = "Thông báo thanh toán đợt cuối",
                            Content = $"Đơn ký gửi {consignmentCode} đã sẵn sàng cho thanh toán đợt cuối ({finalAmount:#,##0}đ). Vui lòng hoàn tất thanh toán.",
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi tạo thông báo thanh toán đợt cuối cho đơn {ConsignmentCode}", consignmentCode);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return new CreateFinalPaymentResponse
            {
                OrderId = order.Id,
                ConsignmentCode = consignmentCode,
                OrderStatus = order.Status,
                InvoiceId = invoice.Id,
                TotalBillAmount = newGrandTotal,
                DepositPaid = depositPaid,
                ExtraFeesTotal = extrasTotal,
                FreightAdjustmentDelta = freightAdjustmentDelta,
                FinalAmount = finalAmount,
                PaymentId = finalPayment.Id,
                OrderCode = orderCode,
                PaymentStatus = "PENDING",
                CheckoutUrl = checkoutUrl,
                PaymentLinkId = paymentLinkId
            };
        }

        // ---------------------------------------------------------------------
        // Lịch sử thanh toán của đơn
        // ---------------------------------------------------------------------
        /// <summary>
        /// Đưa phí lưu kho vào hoá đơn dưới dạng một dòng duy nhất và trả về phần chênh so với
        /// lần chốt trước. Dùng upsert thay vì cộng thêm dòng mới để phát hành lại đợt cuối
        /// nhiều lần cũng không nhân đôi tiền của khách.
        /// </summary>
        /// <summary>Nhãn đợt thu phí lưu kho tại quầy, tách khỏi cọc và tất toán.</summary>
        public const string StorageFeeInstallment = "STORAGE_FEE";

        public async Task<CreateStorageFeePaymentResponse> CreateStorageFeePaymentAsync(
            Guid orderId, Guid userId, string userRole, CreateStorageFeePaymentRequest request)
        {
            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {orderId}");

            var (invoice, payments) = await GetOrderInvoiceAndPaymentsAsync(orderId);
            if (invoice == null)
            {
                throw new ArgumentException(
                    $"Đơn {order.ConsignmentCode} chưa có hoá đơn nên chưa chốt được phí lưu kho.");
            }

            var storage = await _storageFeeService.CalculateForOrderAsync(orderId);
            var consignmentCode = order.ConsignmentCode ?? string.Empty;

            var invoiceItemRepo = _unitOfWork.Repository<InvoiceItem>();
            var existingItems = (await invoiceItemRepo.GetAllAsync())
                .Where(i => i.InvoiceId == invoice.Id)
                .ToList();

            // Phần phí đã nằm trên hoá đơn và đã thu qua đợt tất toán. Chỉ thu phần chênh ra,
            // nếu không khách bị tính tiền hai lần cho cùng những ngày lưu kho.
            var storageItem = existingItems.FirstOrDefault(i =>
                string.Equals(i.FeeType, "STORAGE", StringComparison.OrdinalIgnoreCase));
            var alreadyBilled = storageItem?.Amount ?? 0m;
            var payable = storage.TotalAmount - alreadyBilled;

            var response = new CreateStorageFeePaymentResponse
            {
                OrderId = order.Id,
                ConsignmentCode = consignmentCode,
                FreeDays = storage.FreeDays,
                UnitPrice = storage.UnitPrice,
                Currency = storage.Currency,
                StorageFeeAmount = payable > 0m ? payable : 0m,
                Note = storage.Note,
                Parcels = storage.Parcels,
            };

            var paymentRepo = _unitOfWork.Repository<Payment>();

            // Khoản cũ chưa trả thì bỏ đi: số ngày đã khác, giữ lại là để khách quét nhầm mã cũ
            // rồi trả thiếu.
            foreach (var stale in payments.Where(p =>
                string.Equals(p.InstallmentType, StorageFeeInstallment, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase)))
            {
                stale.PaymentStatus = "CANCELLED";
                stale.FailureReason = "Phát hành lại theo số ngày lưu kho mới.";
                paymentRepo.Update(stale);
            }

            if (payable <= 0m)
            {
                await _unitOfWork.SaveChangesAsync();
                response.IsFreeOfCharge = true;
                response.PaymentStatus = "PAID";
                return response;
            }

            // Ghi phần chênh lên hoá đơn để sổ sách khớp với số tiền vừa phát hành.
            if (storageItem == null)
            {
                await invoiceItemRepo.AddAsync(new InvoiceItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    FeeType = "STORAGE",
                    ReferenceType = "ORDER",
                    ReferenceId = orderId,
                    Amount = storage.TotalAmount,
                    Currency = storage.Currency,
                    Description = BuildStorageDescription(storage),
                });
            }
            else
            {
                storageItem.Amount = storage.TotalAmount;
                storageItem.Description = BuildStorageDescription(storage);
                invoiceItemRepo.Update(storageItem);
            }

            invoice.TotalAmount = (invoice.TotalAmount ?? 0m) + payable;
            _unitOfWork.Repository<Invoice>().Update(invoice);

            var payment = await CreatePendingPaymentAsync(
                invoice, payable, StorageFeeInstallment, $"Phi luu kho {consignmentCode}",
                BuildDescription("LuuKho", consignmentCode), request?.PaymentMethod, request?.ReturnUrl, request?.CancelUrl);
            var orderCode = payment.OrderCode ?? 0L;
            var checkoutUrl = payment.CheckoutUrl;

            await _unitOfWork.SaveChangesAsync();

            response.PaymentId = payment.Id;
            response.PaymentStatus = payment.PaymentStatus;
            response.CheckoutUrl = checkoutUrl;
            response.OrderCode = orderCode;
            return response;
        }

        /// <summary>
        /// Phát hành một khoản thu PENDING kèm link thanh toán (SePay mặc định, PayOS khi khách
        /// chọn). Dùng chung cho các khoản phát sinh sau tất toán: phí lưu kho, phí giao lại —
        /// cùng một cách sinh mã đơn thanh toán và cùng webhook xác nhận.
        /// </summary>
        private async Task<Payment> CreatePendingPaymentAsync(
            Invoice invoice, decimal amount, string installmentType, string payOsItemName, string payOsDescription,
            string? paymentMethodRequested, string? returnUrlRequested, string? cancelUrlRequested)
        {
            var orderCode = await GenerateUniqueOrderCodeAsync();
            var isSePay = !string.Equals(paymentMethodRequested, "PAYOS", StringComparison.OrdinalIgnoreCase);

            string checkoutUrl;
            string paymentLinkId;
            string paymentMethod;

            if (isSePay)
            {
                paymentMethod = "SEPAY";
                paymentLinkId = $"SEPAY-{orderCode}";
                checkoutUrl = PaymentReturnUrls.SePayCheckoutUrl(_configuration, orderCode, returnUrlRequested, cancelUrlRequested);
            }
            else
            {
                paymentMethod = "PAYOS";
                var amountInt = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
                // Trước đây nhận thẳng URL web gửi (không lọc) — giờ chỉ nhận origin cho phép.
                var (returnUrl, cancelUrl) = PaymentReturnUrls.Build(
                    _configuration, returnUrlRequested, cancelUrlRequested, PaymentReturnUrls.ConsignmentHistoryPath, orderCode);
                var items = new List<PayOsItem> { new PayOsItem(payOsItemName, 1, amountInt) };

                var link = await _payOsService.CreatePaymentLinkAsync(
                    orderCode, amountInt, payOsDescription, returnUrl, cancelUrl, items);
                checkoutUrl = link.CheckoutUrl;
                paymentLinkId = link.PaymentLinkId;
            }

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = amount,
                PaymentMethod = paymentMethod,
                PaymentStatus = "PENDING",
                InstallmentType = installmentType,
                OrderCode = orderCode,
                CheckoutUrl = checkoutUrl,
                PaymentLinkId = paymentLinkId,
                CreatedAt = DateTime.UtcNow,
            };
            await _unitOfWork.Repository<Payment>().AddAsync(payment);
            return payment;
        }

        /// <summary>Phí giao lại sau khi giao thất bại: ghi lên hoá đơn và phát hành khoản thu riêng.</summary>
        public async Task<Payment> IssueRedeliveryFeePaymentAsync(Guid orderId, Guid deliveryRequestId, string deliveryCode, decimal amount)
        {
            if (amount <= 0m) throw new ArgumentException("Phí giao lại phải lớn hơn 0.");

            var order = await _unitOfWork.Repository<Order>().GetByIdAsync(orderId)
                ?? throw new NotFoundException($"Không tìm thấy đơn hàng với ID {orderId}");
            var (invoice, _) = await GetOrderInvoiceAndPaymentsAsync(orderId);
            if (invoice == null)
                throw new ArgumentException($"Đơn {order.ConsignmentCode} chưa có hoá đơn nên chưa thu được phí giao lại.");

            var consignmentCode = order.ConsignmentCode ?? string.Empty;
            await _unitOfWork.Repository<InvoiceItem>().AddAsync(new InvoiceItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                FeeType = RedeliveryFeeInstallment,
                ReferenceType = "DELIVERY_REQUEST",
                ReferenceId = deliveryRequestId,
                Amount = amount,
                Currency = "VND",
                Description = $"Phí giao lại — yêu cầu giao {deliveryCode}"
            });
            invoice.TotalAmount = (invoice.TotalAmount ?? 0m) + amount;
            _unitOfWork.Repository<Invoice>().Update(invoice);

            return await CreatePendingPaymentAsync(
                invoice, amount, RedeliveryFeeInstallment, $"Phi giao lai {consignmentCode}",
                BuildDescription("GiaoLai", consignmentCode), null, null, null);
        }

        public const string RedeliveryFeeInstallment = "REDELIVERY_FEE";

        private static string BuildStorageDescription(StorageFeeDto storage)
        {
            var chargeableDays = storage.Parcels.Sum(p => p.ChargeableDays);
            var perWarehouse = storage.Parcels
                .SelectMany(p => p.Legs)
                .Where(l => !string.IsNullOrWhiteSpace(l.WarehouseName))
                .GroupBy(l => l.WarehouseName)
                .Select(g => $"{g.Key}: {g.Sum(l => l.Days)} ngày")
                .ToList();

            var allowance = storage.GraceDays > 0
                ? $"miễn {storage.FreeDays} ngày kể từ khi hàng về kho VN + {storage.GraceDays} ngày ân hạn"
                : $"miễn {storage.FreeDays} ngày kể từ khi hàng về kho VN";

            var description =
                $"Phí lưu kho — {chargeableDays} ngày tính phí "
                + $"({storage.UnitPrice:#,##0}đ/kiện/ngày, {allowance})";

            return perWarehouse.Count > 0
                ? description + $". {string.Join(" · ", perWarehouse)}"
                : description;
        }

        private async Task<decimal> ApplyStorageFeeAsync(
            Guid orderId,
            Invoice invoice,
            List<InvoiceItem> existingItems,
            IGenericRepository<InvoiceItem> invoiceItemRepo)
        {
            var storage = await _storageFeeService.CalculateForOrderAsync(orderId);

            var existing = existingItems.FirstOrDefault(i =>
                string.Equals(i.FeeType, "STORAGE", StringComparison.OrdinalIgnoreCase));
            var previousAmount = existing?.Amount ?? 0m;

            if (storage.TotalAmount <= 0m)
            {
                return existing == null ? 0m : -previousAmount;
            }

            var chargeableDays = storage.Parcels.Sum(p => p.ChargeableDays);

            // Phí nay tính trên cả kho nguồn lẫn kho đích, nên nhãn cũ "tại kho đích" là sai —
            // khách thấy dòng đó mà phần lớn số ngày lại phát sinh ở kho nguồn thì không đối
            // chiếu được. Tách rõ từng kho để khách tự cộng lại ra đúng con số.
            var perWarehouse = storage.Parcels
                .SelectMany(p => p.Legs)
                .Where(l => !string.IsNullOrWhiteSpace(l.WarehouseName))
                .GroupBy(l => l.WarehouseName)
                .Select(g => $"{g.Key}: {g.Sum(l => l.Days)} ngày")
                .ToList();

            var description =
                $"Phí lưu kho — {chargeableDays} ngày tính phí "
                + $"({storage.UnitPrice:#,##0}đ/kiện/ngày, miễn {storage.FreeDays} ngày kể từ khi hàng về kho VN)";

            if (perWarehouse.Count > 0)
            {
                description += $". {string.Join(" · ", perWarehouse)}";
            }

            if (existing == null)
            {
                await invoiceItemRepo.AddAsync(new InvoiceItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    FeeType = "STORAGE",
                    ReferenceType = "ORDER",
                    ReferenceId = orderId,
                    Amount = storage.TotalAmount,
                    Currency = storage.Currency,
                    Description = description
                });
            }
            else
            {
                existing.Amount = storage.TotalAmount;
                existing.Description = description;
                invoiceItemRepo.Update(existing);
            }

            return storage.TotalAmount - previousAmount;
        }

        public async Task<OrderPaymentsResponse> GetOrderPaymentsAsync(Guid orderId, Guid userId, string userRole)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            // Phân quyền: Customer chỉ xem đơn của mình; Staff xem tất cả
            if (!IsStaff(userRole))
            {
                var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
                var customer = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (customer == null || order.CustomerId != customer.Id)
                    throw new UnauthorizedAccessException("Bạn không có quyền xem lịch sử thanh toán của đơn này.");
            }

            var (invoice, payments) = await GetOrderInvoiceAndPaymentsAsync(orderId);

            var items = payments
                .OrderBy(p => p.PaidAt ?? DateTime.MaxValue)
                .Select(p => new OrderPaymentItemDto
                {
                    PaymentId = p.Id,
                    InvoiceId = p.InvoiceId,
                    InstallmentType = p.InstallmentType,
                    Amount = p.Amount ?? 0m,
                    PaymentMethod = p.PaymentMethod ?? string.Empty,
                    PaymentStatus = p.PaymentStatus ?? string.Empty,
                    OrderCode = p.OrderCode,
                    TransactionCode = p.TransactionCode,
                    CheckoutUrl = p.CheckoutUrl,
                    PaidAt = p.PaidAt
                })
                .ToList();

            var totalBill = invoice?.TotalAmount ?? 0m;
            var totalPaid = payments.Where(IsPaid).Sum(p => p.Amount ?? 0m);

            return new OrderPaymentsResponse
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                OrderStatus = order.Status ?? string.Empty,
                TotalBillAmount = totalBill,
                TotalPaid = totalPaid,
                Remaining = Math.Max(0m, totalBill - totalPaid),
                Payments = items
            };
        }

        // ---------------------------------------------------------------------
        // VCL-207: Lịch sử thanh toán chi tiết (Customer + báo giá + các đợt)
        // ---------------------------------------------------------------------
        public async Task<PaymentHistoryResponse> GetPaymentHistoryAsync(Guid orderId, Guid userId, string userRole)
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            // Phân quyền: Customer chỉ xem đơn của mình; Staff/Sales/Admin xem tất cả
            var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var owner = order.CustomerId.HasValue
                ? allCustomers.FirstOrDefault(c => c.Id == order.CustomerId.Value)
                : null;

            if (!IsStaff(userRole))
            {
                var me = allCustomers.FirstOrDefault(c => c.UserId == userId);
                if (me == null || order.CustomerId != me.Id)
                    throw new UnauthorizedAccessException("Bạn không có quyền xem lịch sử thanh toán của đơn này.");
            }

            var (invoice, payments) = await GetOrderInvoiceAndPaymentsAsync(orderId);

            // Báo giá liên quan (qua hóa đơn, hoặc báo giá ACCEPTED mới nhất của đơn)
            PaymentHistoryQuotationDto quotationDto = null;
            Guid? quotationId = invoice?.QuotationId;
            if (!quotationId.HasValue)
            {
                var q = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                    .Where(x => x.OrderId == orderId)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefault();
                quotationId = q?.Id;
            }
            if (quotationId.HasValue)
            {
                var quotation = await _unitOfWork.Repository<Quotation>().GetByIdAsync(quotationId.Value);
                if (quotation != null)
                {
                    var quoteTotal = (quotation.EstimatedFreightCharge ?? 0m)
                                     + (quotation.ServiceFee ?? 0m)
                                     + (quotation.TaxAndDuty ?? 0m);
                    quotationDto = new PaymentHistoryQuotationDto
                    {
                        QuotationId = quotation.Id,
                        QuoteType = quotation.QuoteType,
                        Status = quotation.Status,
                        TotalAmount = quoteTotal
                    };
                }
            }

            var items = payments
                .OrderBy(p => p.CreatedAt ?? p.PaidAt ?? DateTime.MaxValue)
                .Select(p => new PaymentHistoryItemDto
                {
                    PaymentId = p.Id,
                    InvoiceId = p.InvoiceId,
                    InstallmentType = p.InstallmentType,
                    Amount = p.Amount ?? 0m,
                    PaymentMethod = p.PaymentMethod ?? string.Empty,
                    Status = NormalizePaymentStatus(p.PaymentStatus),
                    OrderCode = p.OrderCode,
                    TransactionCode = p.TransactionCode,
                    CheckoutUrl = p.CheckoutUrl,
                    CreatedAt = p.CreatedAt,
                    PaidAt = p.PaidAt,
                    FailureReason = p.FailureReason
                })
                .ToList();

            var totalBill = invoice?.TotalAmount ?? quotationDto?.TotalAmount ?? 0m;
            var totalPaid = payments.Where(IsPaid).Sum(p => p.Amount ?? 0m);

            return new PaymentHistoryResponse
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                OrderStatus = order.Status ?? string.Empty,
                Customer = new PaymentHistoryCustomerDto
                {
                    CustomerId = owner?.Id,
                    FullName = owner?.FullName ?? string.Empty,
                    CustomerCode = owner?.CustomerCode ?? string.Empty,
                    Email = owner?.Email,
                    Phone = owner?.Phone
                },
                Quotation = quotationDto,
                TotalBillAmount = totalBill,
                TotalPaid = totalPaid,
                Remaining = Math.Max(0m, totalBill - totalPaid),
                Payments = items
            };
        }

        /// <summary>Chuẩn hóa trạng thái thanh toán nội bộ về vốn từ FE: PENDING | SUCCESS | FAILED | CANCELED.</summary>
        private static string NormalizePaymentStatus(string raw)
        {
            var s = raw?.Trim().ToUpperInvariant() ?? string.Empty;
            return s switch
            {
                "PAID" or "SUCCESS" => "SUCCESS",
                "FAILED" => "FAILED",
                "CANCELED" or "CANCELLED" => "CANCELED",
                "PENDING" => "PENDING",
                _ => s
            };
        }

        // ---------------------------------------------------------------------
        // Webhook payOS
        // ---------------------------------------------------------------------
        public async Task<bool> HandlePayOsWebhookAsync(string rawJsonBody)
        {
            var result = _payOsService.VerifyWebhook(rawJsonBody);

            var paymentRepo = _unitOfWork.Repository<Payment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode.HasValue && p.OrderCode.Value == result.OrderCode);
            if (payment == null)
                return false;

            if (string.Equals(payment.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!result.Success)
            {
                _logger.LogWarning(
                    "PayOS webhook reports unsuccessful payment for OrderCode={OrderCode}. Code={Code} Desc={Desc}",
                    result.OrderCode, result.Code, result.Desc);
                return false;
            }

            // Webhook payOS không kèm số tiền: chữ ký đã xác thực đúng mã đơn, nên coi thực nhận bằng
            // số của link thanh toán.
            await MarkPaymentAsPaidAsync(payment, result.Reference, receivedAmount: null);
            return true;
        }

        public async Task<string> SyncPaymentStatusAsync(long orderCode)
        {
            var paymentRepo = _unitOfWork.Repository<Payment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode.HasValue && p.OrderCode.Value == orderCode);
            if (payment == null)
            {
                throw new NotFoundException($"Không tìm thấy khoản thanh toán ký gửi với mã đơn {orderCode}.");
            }

            if (string.Equals(payment.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return "PAID";
            }

            var status = await _payOsService.GetPaymentStatusAsync(orderCode);
            _logger.LogInformation(
                "Synced payOS status for OrderCode={OrderCode}: Status={Status} AmountPaid={AmountPaid}",
                orderCode, status.Status, status.AmountPaid);

            if (string.Equals(status.Status, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                await MarkPaymentAsPaidAsync(payment, status.Reference, status.AmountPaid);
                return "PAID";
            }

            return status.Status;
        }

        /// <summary>
        /// Ghi nhận tiền payOS đã báo về. Toàn bộ luật đổi trạng thái đơn, hoá đơn và sinh phiếu
        /// kho nằm ở <see cref="IPaymentConfirmationService"/>, dùng chung với SePay và Admin.
        /// </summary>
        /// <remarks>
        /// Trước đây nhánh này thiếu trường hợp phí lưu kho nên trả phí lưu kho qua payOS làm cả
        /// đơn và hoá đơn thành PAID.
        /// </remarks>
        private async Task MarkPaymentAsPaidAsync(Payment payment, string reference, decimal? receivedAmount)
        {
            var result = await _paymentConfirmation.ConfirmAsync(payment, new PaymentConfirmationInput
            {
                Source = PaymentConfirmationSources.PayOs,
                ReceivedAmount = receivedAmount,
                Reference = reference
            });

            if (result.Outcome == PaymentConfirmationOutcome.Underpaid)
            {
                _logger.LogWarning("payOS báo thiếu tiền cho khoản {PaymentId}: {Message}", payment.Id, result.Message);
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>Lấy hóa đơn (mới nhất) của đơn + toàn bộ payment thuộc hóa đơn đó.</summary>
        /// <summary>Các ô kệ thuộc kho đích — dùng để biết kiện đã thực sự về VN chưa.</summary>
        private async Task<HashSet<Guid>> GetDestinationBinIdsAsync()
        {
            var destinationWarehouseIds = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .Where(w => string.Equals(w.WarehouseType?.Trim(), "DESTINATION", StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Id)
                .ToHashSet();

            return (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.WarehouseId.HasValue && destinationWarehouseIds.Contains(b.WarehouseId.Value))
                .Select(b => b.Id)
                .ToHashSet();
        }

        private async Task<(Invoice invoice, List<Payment> payments)> GetOrderInvoiceAndPaymentsAsync(Guid orderId)
        {
            var quotations = (await _unitOfWork.Repository<Quotation>().GetAllAsync())
                .Where(q => q.OrderId == orderId)
                .Select(q => q.Id)
                .ToHashSet();

            var invoice = (await _unitOfWork.Repository<Invoice>().GetAllAsync())
                .Where(i => i.QuotationId.HasValue && quotations.Contains(i.QuotationId.Value))
                .OrderByDescending(i => i.IssuedAt)
                .FirstOrDefault();

            if (invoice == null)
                return (null, new List<Payment>());

            var payments = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Where(p => p.InvoiceId.HasValue && p.InvoiceId.Value == invoice.Id)
                .ToList();

            return (invoice, payments);
        }

        private async Task<decimal> GetDepositRateAsync()
        {
            var fees = await _unitOfWork.Repository<AdditionalServiceFee>().GetAllAsync();
            var rateFee = fees.FirstOrDefault(f =>
                f.IsActive && string.Equals(f.FeeCode, "DEPOSIT_RATE", StringComparison.OrdinalIgnoreCase));
            if (rateFee == null) return DefaultDepositRate;
            // Kẹp trong khoảng hợp lệ 0..100
            var value = rateFee.Value;
            if (value < 0m) value = 0m;
            if (value > 100m) value = 100m;
            return value;
        }

        private static bool IsStaff(string userRole) =>
            StaffRoleHelper.CanHandlePayments(userRole);

        private static bool IsInstallment(Payment p, string type) =>
            string.Equals(p.InstallmentType, type, StringComparison.OrdinalIgnoreCase);

        private static bool IsPaid(Payment p) =>
            string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase);

        private static async Task AddInvoiceItemAsync(
            IGenericRepository<InvoiceItem> repo, Guid invoiceId, string feeType, decimal? amount, string description)
        {
            if (!amount.HasValue || amount.Value <= 0m)
                return;

            await repo.AddAsync(new InvoiceItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoiceId,
                FeeType = feeType,
                ReferenceType = "QUOTATION",
                Amount = amount.Value,
                Currency = "VND",
                Description = description
            });
        }

        private async Task<long> GenerateUniqueOrderCodeAsync()
        {
            var paymentCodes = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Where(p => p.OrderCode.HasValue).Select(p => p.OrderCode!.Value);
            var purchaseCodes = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .Select(p => p.OrderCode);
            var used = new HashSet<long>(paymentCodes.Concat(purchaseCodes));

            var random = new Random();
            long code;
            do
            {
                var baseCode = long.Parse(DateTime.UtcNow.ToString("yyMMddHHmmss"));
                code = baseCode * 100 + random.Next(0, 100);
            } while (used.Contains(code));
            return code;
        }

        private static string BuildDescription(string prefix, string consignmentCode)
        {
            var desc = string.IsNullOrWhiteSpace(consignmentCode) ? $"{prefix} ky gui" : $"{prefix} {consignmentCode}";
            return desc.Length > 25 ? desc.Substring(0, 25) : desc;
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
                _logger.LogWarning("SePay Webhook: Không tìm thấy orderCode trong Code={Code} hoặc Content={Content}", dto.Code, dto.Content);
                return false;
            }

            var paymentRepo = _unitOfWork.Repository<Payment>();
            var payment = await paymentRepo.FirstOrDefaultAsync(p => p.OrderCode.HasValue && p.OrderCode.Value == orderCode.Value);
            if (payment == null)
            {
                _logger.LogWarning("SePay Webhook: Không tìm thấy khoản thanh toán với OrderCode={OrderCode}", orderCode.Value);
                return false;
            }

            if (string.Equals(payment.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // SePay báo đúng số tiền ngân hàng nhận: ghi lại cả phần chuyển dư, thiếu thì không xác nhận.
            var result = await _paymentConfirmation.ConfirmAsync(payment, new PaymentConfirmationInput
            {
                Source = PaymentConfirmationSources.SePay,
                ReceivedAmount = dto.TransferAmount,
                Reference = dto.ReferenceCode
            });

            if (result.Outcome == PaymentConfirmationOutcome.Underpaid)
            {
                _logger.LogWarning(
                    "SePay Webhook: Số tiền thanh toán ({TransferAmount}) nhỏ hơn số tiền yêu cầu ({PaymentAmount}) cho OrderCode={OrderCode}",
                    dto.TransferAmount, payment.Amount, orderCode.Value);
                return false;
            }

            return true;
        }
    }
}
