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
    public class ConsignmentPaymentService : IConsignmentPaymentService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPayOsService _payOsService;
        private readonly IConfiguration _configuration;

        // Role nội bộ được tạo thanh toán cuối / xem lịch sử thanh toán.
        private static readonly string[] StaffRoles =
            { "ADMIN", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS" };

        // Tỷ lệ cọc mặc định nếu Admin chưa cấu hình DEPOSIT_RATE.
        private const decimal DefaultDepositRate = 50m;

        // Trạng thái kiện đã về kho đích - điều kiện cho phép tạo thanh toán cuối.
        private const string ParcelReceivedAtDestination = "RECEIVED_AT_DESTINATION";

        public ConsignmentPaymentService(IUnitOfWork unitOfWork, IPayOsService payOsService, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _payOsService = payOsService;
            _configuration = configuration;
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

            var status = quotation.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            if (status == "ACCEPTED")
                throw new ArgumentException("Báo giá này đã được xác nhận trước đó.");
            if (status == "REJECTED" || status == "CANCELLED" || status == "EXPIRED")
                throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {quotation.Status}.");
            var confirmableStatuses = new[] { "DRAFT", "PENDING", "WAITING_CUSTOMER_CONFIRMATION" };
            if (!confirmableStatuses.Contains(status))
                throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {quotation.Status}.");

            if (quotation.ExpiredAt.HasValue && quotation.ExpiredAt.Value < DateTime.UtcNow)
                throw new ArgumentException("Báo giá đã hết hạn. Vui lòng tạo báo giá mới.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);
            if (order == null)
                throw new NotFoundException("Không tìm thấy đơn hàng liên quan đến báo giá.");

            var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.Id == order.CustomerId);
            if (customer == null || customer.UserId != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận báo giá này.");

            // Tổng bill = cước + phí dịch vụ (đã gồm phụ phí) + thuế + các phụ phí lưu riêng trong QuotationFee
            var feeRepo = _unitOfWork.Repository<QuotationFee>();
            var allFees = await feeRepo.GetAllAsync();
            var surchargesAmount = allFees
                .Where(f => f.QuotationId == quotation.Id && string.Equals(f.FeeType, "SURCHARGE", StringComparison.OrdinalIgnoreCase))
                .Sum(f => f.Amount);

            var totalAmount = (quotation.EstimatedFreightCharge ?? 0m)
                              + (quotation.ServiceFee ?? 0m)
                              + (quotation.TaxAndDuty ?? 0m)
                              + surchargesAmount;
            if (totalAmount <= 0m)
                throw new ArgumentException("Tổng tiền báo giá không hợp lệ để tạo thanh toán.");

            // Tính tiền cọc theo tỷ lệ Admin cấu hình
            var depositRate = await GetDepositRateAsync();
            var depositAmount = Math.Round(totalAmount * depositRate / 100m, MidpointRounding.AwayFromZero);

            // Cập nhật báo giá -> ACCEPTED
            quotation.Status = "ACCEPTED";
            quotationRepo.Update(quotation);

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

            // Edge: tỷ lệ cọc = 0% -> không cần cọc, cho phép vận chuyển ngay
            if (depositAmount <= 0m)
            {
                order.Status = "DEPOSIT_PAID";
                orderRepo.Update(order);

                var noDeposit = new Payment
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    Amount = 0m,
                    PaymentMethod = "PAYOS",
                    PaymentStatus = "PAID",
                    InstallmentType = "DEPOSIT",
                    PaidAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                await paymentRepo.AddAsync(noDeposit);
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

            // Tạo payOS link cho tiền cọc
            long orderCode = await GenerateUniqueOrderCodeAsync();
            var amountInt = (int)Math.Round(depositAmount, MidpointRounding.AwayFromZero);
            var description = BuildDescription("Coc", consignmentCode);
            var returnUrl = FirstNonEmpty(request.ReturnUrl, _configuration["PayOS:ReturnUrl"], "https://localhost:3000/payment/success");
            var cancelUrl = FirstNonEmpty(request.CancelUrl, _configuration["PayOS:CancelUrl"], "https://localhost:3000/payment/cancel");
            var items = new List<PayOsItem> { new PayOsItem($"Cọc {consignmentCode}", 1, amountInt) };

            PayOsPaymentLink linkResult = await _payOsService.CreatePaymentLinkAsync(
                orderCode, amountInt, description, returnUrl, cancelUrl, items);

            var deposit = new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = depositAmount,
                PaymentMethod = "PAYOS",
                PaymentStatus = "PENDING",
                InstallmentType = "DEPOSIT",
                OrderCode = orderCode,
                CheckoutUrl = linkResult.CheckoutUrl,
                PaymentLinkId = linkResult.PaymentLinkId,
                CreatedAt = DateTime.UtcNow
            };
            await paymentRepo.AddAsync(deposit);

            order.Status = "WAITING_DEPOSIT";
            orderRepo.Update(order);

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
                PaymentMethod = "PAYOS",
                PaymentStatus = "PENDING",
                CheckoutUrl = linkResult.CheckoutUrl,
                PaymentLinkId = linkResult.PaymentLinkId
            };
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

            // Điều kiện trigger: đơn phải có kiện đã về kho đích (RECEIVED_AT_DESTINATION)
            var allParcels = await _unitOfWork.Repository<Parcel>().GetAllAsync();
            var orderParcels = allParcels.Where(p => p.ConsignmentId == orderId).ToList();
            bool anyReceivedAtDestination = orderParcels.Any(p =>
                string.Equals(p.PackageStatus, ParcelReceivedAtDestination, StringComparison.OrdinalIgnoreCase));
            if (!anyReceivedAtDestination)
                throw new ArgumentException("Chưa thể tạo thanh toán cuối: đơn chưa có kiện hàng về đến kho đích.");

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
            if (allOrderPayments.Any(p => IsInstallment(p, "FINAL_PAYMENT") &&
                    string.Equals(p.PaymentStatus, "PENDING", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Đơn hàng đã có một đợt thanh toán cuối đang chờ thanh toán.");

            // Ghi nhận chi phí phát sinh (InvoiceItem) + cập nhật tổng bill
            var invoiceItemRepo = _unitOfWork.Repository<InvoiceItem>();
            decimal extrasTotal = 0m;
            foreach (var fee in request.ExtraFees ?? new List<ExtraFeeLineDto>())
            {
                if (fee.Amount <= 0m) continue;
                extrasTotal += fee.Amount;
                var desc = string.IsNullOrWhiteSpace(fee.Note) ? fee.Name : $"{fee.Name} - {fee.Note}";
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
                    FinalAmount = 0m,
                    PaymentId = zeroFinal.Id,
                    OrderCode = null,
                    PaymentStatus = "PAID",
                    CheckoutUrl = null,
                    PaymentLinkId = null
                };
            }

            // Tạo payOS link cho đợt cuối
            long orderCode = await GenerateUniqueOrderCodeAsync();
            var amountInt = (int)Math.Round(finalAmount, MidpointRounding.AwayFromZero);
            var description = BuildDescription("Cuoi", consignmentCode);
            var returnUrl = FirstNonEmpty(request.ReturnUrl, _configuration["PayOS:ReturnUrl"], "https://localhost:3000/payment/success");
            var cancelUrl = FirstNonEmpty(request.CancelUrl, _configuration["PayOS:CancelUrl"], "https://localhost:3000/payment/cancel");
            var items = new List<PayOsItem> { new PayOsItem($"TT cuoi {consignmentCode}", 1, amountInt) };

            PayOsPaymentLink linkResult = await _payOsService.CreatePaymentLinkAsync(
                orderCode, amountInt, description, returnUrl, cancelUrl, items);

            var finalPayment = new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = finalAmount,
                PaymentMethod = "PAYOS",
                PaymentStatus = "PENDING",
                InstallmentType = "FINAL_PAYMENT",
                OrderCode = orderCode,
                CheckoutUrl = linkResult.CheckoutUrl,
                PaymentLinkId = linkResult.PaymentLinkId,
                CreatedAt = DateTime.UtcNow
            };
            await paymentRepo.AddAsync(finalPayment);

            invoiceRepo.Update(invoice);
            order.Status = "WAITING_FINAL_PAYMENT";
            orderRepo.Update(order);

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
                FinalAmount = finalAmount,
                PaymentId = finalPayment.Id,
                OrderCode = orderCode,
                PaymentStatus = "PENDING",
                CheckoutUrl = linkResult.CheckoutUrl,
                PaymentLinkId = linkResult.PaymentLinkId
            };
        }

        // ---------------------------------------------------------------------
        // Lịch sử thanh toán của đơn
        // ---------------------------------------------------------------------
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
        // VCL-209: Sale xác nhận thanh toán đơn ký gửi
        // ---------------------------------------------------------------------
        public async Task<ConfirmOrderPaymentResponse> ConfirmOrderPaymentAsync(
            Guid orderId, Guid userId, string userRole, ConfirmOrderPaymentRequest request)
        {
            if (request == null)
                throw new ArgumentException("Thiếu dữ liệu xác nhận thanh toán.");

            if (!IsSales(userRole))
                throw new UnauthorizedAccessException("Chỉ Sale được xác nhận thanh toán đơn ký gửi.");

            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundException($"Không tìm thấy đơn hàng với ID: {orderId}");

            // Không cho xác nhận trùng
            if (order.PaymentConfirmedAt.HasValue)
                throw new ArgumentException("Đơn hàng đã được xác nhận thanh toán trước đó.");

            // Payment phải thuộc đơn này
            var (_, orderPayments) = await GetOrderInvoiceAndPaymentsAsync(orderId);
            var payment = orderPayments.FirstOrDefault(p => p.Id == request.PaymentId);
            if (payment == null)
                throw new NotFoundException("Không tìm thấy khoản thanh toán thuộc đơn hàng này.");

            // Chỉ xác nhận nếu payment đã SUCCESS
            if (!IsPaid(payment))
                throw new ArgumentException(
                    "Không thể xác nhận: khoản thanh toán chưa ở trạng thái SUCCESS (đang PENDING/FAILED/CANCELED).");

            order.PaymentConfirmedById = userId;
            order.PaymentConfirmedAt = DateTime.UtcNow;
            order.PaymentConfirmationNote = request.ConfirmationNote;
            order.Status = "PAYMENT_CONFIRMED";
            orderRepo.Update(order);

            await _unitOfWork.SaveChangesAsync();

            return new ConfirmOrderPaymentResponse
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                OrderStatus = order.Status,
                PaymentId = payment.Id,
                PaymentStatus = NormalizePaymentStatus(payment.PaymentStatus),
                ConfirmedById = userId,
                ConfirmedAt = order.PaymentConfirmedAt.Value,
                ConfirmationNote = order.PaymentConfirmationNote
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
                return false;

            payment.PaymentStatus = "PAID";
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionCode = result.Reference;
            paymentRepo.Update(payment);

            if (payment.InvoiceId.HasValue)
            {
                var invoiceRepo = _unitOfWork.Repository<Invoice>();
                var invoice = await invoiceRepo.GetByIdAsync(payment.InvoiceId.Value);
                if (invoice != null && invoice.QuotationId.HasValue)
                {
                    var quotation = await _unitOfWork.Repository<Quotation>().GetByIdAsync(invoice.QuotationId.Value);
                    if (quotation?.OrderId != null)
                    {
                        var orderRepo = _unitOfWork.Repository<Order>();
                        var order = await orderRepo.GetByIdAsync(quotation.OrderId.Value);
                        if (order != null)
                        {
                            // Cọc trả xong -> cho phép vận chuyển; đợt cuối trả xong -> hoàn tất
                            var installment = payment.InstallmentType?.Trim().ToUpperInvariant();
                            if (installment == "DEPOSIT")
                            {
                                order.Status = "DEPOSIT_PAID";
                                // Hóa đơn vẫn PENDING vì mới trả cọc
                            }
                            else
                            {
                                // FINAL_PAYMENT hoặc thanh toán 1 lần cũ (null) -> hoàn tất
                                order.Status = "PAID";
                                invoice.InvoiceStatus = "PAID";
                                invoiceRepo.Update(invoice);
                            }
                            orderRepo.Update(order);
                        }
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>Lấy hóa đơn (mới nhất) của đơn + toàn bộ payment thuộc hóa đơn đó.</summary>
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
            !string.IsNullOrWhiteSpace(userRole) && StaffRoles.Contains(userRole.Trim().ToUpperInvariant());

        // Chỉ Sale (và Admin) được xác nhận thanh toán.
        private static readonly string[] SalesRoles = { "ADMIN", "SALE", "SALES" };

        private static bool IsSales(string userRole) =>
            !string.IsNullOrWhiteSpace(userRole) && SalesRoles.Contains(userRole.Trim().ToUpperInvariant());

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

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }
    }
}
