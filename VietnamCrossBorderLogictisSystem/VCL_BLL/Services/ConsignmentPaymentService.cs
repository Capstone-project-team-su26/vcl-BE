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

        public ConsignmentPaymentService(IUnitOfWork unitOfWork, IPayOsService payOsService, IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _payOsService = payOsService;
            _configuration = configuration;
        }

        public async Task<ConfirmQuotationPaymentResponse> ConfirmQuotationAndPayAsync(
            Guid quotationId, Guid userId, ConfirmQuotationPaymentRequest request)
        {
            request ??= new ConfirmQuotationPaymentRequest();

            // 1. Tìm báo giá (404 nếu không có)
            var quotationRepo = _unitOfWork.Repository<Quotation>();
            var quotation = await quotationRepo.GetByIdAsync(quotationId);
            if (quotation == null)
            {
                throw new NotFoundException($"Không tìm thấy báo giá với ID: {quotationId}");
            }

            // 2. Chỉ cho xác nhận báo giá còn hợp lệ (DRAFT/PENDING/WAITING_CUSTOMER_CONFIRMATION)
            var status = quotation.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var confirmableStatuses = new[] { "DRAFT", "PENDING", "WAITING_CUSTOMER_CONFIRMATION" };
            if (status == "ACCEPTED")
                throw new ArgumentException("Báo giá này đã được xác nhận trước đó.");
            if (status == "REJECTED" || status == "CANCELLED" || status == "EXPIRED")
                throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {quotation.Status}.");
            if (!confirmableStatuses.Contains(status))
                throw new ArgumentException($"Không thể xác nhận báo giá ở trạng thái: {quotation.Status}.");

            // 3. Kiểm tra hết hạn
            if (quotation.ExpiredAt.HasValue && quotation.ExpiredAt.Value < DateTime.UtcNow)
                throw new ArgumentException("Báo giá đã hết hạn. Vui lòng tạo báo giá mới.");

            // 4. Kiểm tra quyền sở hữu: Order.CustomerId -> Customer.UserId == userId
            var orderRepo = _unitOfWork.Repository<Order>();
            var order = await orderRepo.GetByIdAsync(quotation.OrderId);
            if (order == null)
                throw new NotFoundException("Không tìm thấy đơn hàng liên quan đến báo giá.");

            var allCustomers = await _unitOfWork.Repository<Customer>().GetAllAsync();
            var customer = allCustomers.FirstOrDefault(c => c.Id == order.CustomerId);
            if (customer == null || customer.UserId != userId)
                throw new UnauthorizedAccessException("Bạn không có quyền xác nhận báo giá này.");

            // 5. Tổng tiền = cước dự kiến + phí dịch vụ (đã gồm phụ phí) + thuế
            var totalAmount = (quotation.EstimatedFreightCharge ?? 0m)
                              + (quotation.ServiceFee ?? 0m)
                              + (quotation.TaxAndDuty ?? 0m);
            if (totalAmount <= 0m)
                throw new ArgumentException("Tổng tiền báo giá không hợp lệ để tạo thanh toán.");

            // 6. Cập nhật trạng thái báo giá + đơn hàng
            quotation.Status = "ACCEPTED";
            quotationRepo.Update(quotation);

            order.Status = "WAITING_PAYMENT";
            orderRepo.Update(order);

            // 7. Tạo Invoice + các dòng phí (giữ nguyên breakdown)
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

            // 8. Sinh mã đơn hàng payOS duy nhất (đối chiếu ở cả PAYMENTS lẫn PURCHASE_PAYMENTS)
            long orderCode = await GenerateUniqueOrderCodeAsync();

            // 9. Tạo link thanh toán payOS
            var amountInt = (int)Math.Round(totalAmount, MidpointRounding.AwayFromZero);
            var consignmentCode = order.ConsignmentCode ?? string.Empty;
            var description = BuildDescription(consignmentCode);
            var returnUrl = FirstNonEmpty(request.ReturnUrl, _configuration["PayOS:ReturnUrl"], "https://localhost:3000/payment/success");
            var cancelUrl = FirstNonEmpty(request.CancelUrl, _configuration["PayOS:CancelUrl"], "https://localhost:3000/payment/cancel");

            var items = new List<PayOsItem>
            {
                new PayOsItem(BuildItemName(consignmentCode), 1, amountInt)
            };

            PayOsPaymentLink link = await _payOsService.CreatePaymentLinkAsync(
                orderCode, amountInt, description, returnUrl, cancelUrl, items);

            // 10. Tạo Payment ở trạng thái PENDING
            var paymentRepo = _unitOfWork.Repository<Payment>();
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = totalAmount,
                PaymentMethod = "PAYOS",
                PaymentStatus = "PENDING",
                OrderCode = orderCode,
                CheckoutUrl = link.CheckoutUrl,
                PaymentLinkId = link.PaymentLinkId
            };
            await paymentRepo.AddAsync(payment);

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
                PaymentId = payment.Id,
                OrderCode = orderCode,
                Amount = totalAmount,
                PaymentMethod = payment.PaymentMethod,
                PaymentStatus = payment.PaymentStatus,
                CheckoutUrl = link.CheckoutUrl,
                PaymentLinkId = link.PaymentLinkId
            };
        }

        public async Task<bool> HandlePayOsWebhookAsync(string rawJsonBody)
        {
            // 1. Xác thực chữ ký + parse dữ liệu (ném lỗi nếu chữ ký sai)
            var result = _payOsService.VerifyWebhook(rawJsonBody);

            // 2. Tìm khoản thanh toán ký gửi theo orderCode
            var paymentRepo = _unitOfWork.Repository<Payment>();
            var allPayments = await paymentRepo.GetAllAsync();
            var payment = allPayments.FirstOrDefault(p => p.OrderCode.HasValue && p.OrderCode.Value == result.OrderCode);
            if (payment == null)
            {
                // Không thuộc bảng PAYMENTS (có thể là thanh toán mua hộ hoặc webhook test) -> bỏ qua
                return false;
            }

            // 3. Idempotent: đã xử lý xong thì thôi
            if (string.Equals(payment.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!result.Success)
                return false;

            // 4. Cập nhật thanh toán thành công
            payment.PaymentStatus = "PAID";
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionCode = result.Reference;
            paymentRepo.Update(payment);

            // 5. Cập nhật hóa đơn + đơn hàng
            if (payment.InvoiceId.HasValue)
            {
                var invoiceRepo = _unitOfWork.Repository<Invoice>();
                var invoice = await invoiceRepo.GetByIdAsync(payment.InvoiceId.Value);
                if (invoice != null)
                {
                    invoice.InvoiceStatus = "PAID";
                    invoiceRepo.Update(invoice);

                    if (invoice.QuotationId.HasValue)
                    {
                        var quotation = await _unitOfWork.Repository<Quotation>().GetByIdAsync(invoice.QuotationId.Value);
                        if (quotation?.OrderId != null)
                        {
                            var orderRepo = _unitOfWork.Repository<Order>();
                            var order = await orderRepo.GetByIdAsync(quotation.OrderId.Value);
                            if (order != null)
                            {
                                order.Status = "PAID";
                                orderRepo.Update(order);
                            }
                        }
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

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
            // Đối chiếu ở cả PAYMENTS và PURCHASE_PAYMENTS để orderCode không trùng toàn hệ thống payOS
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

        private static string BuildDescription(string consignmentCode)
        {
            var desc = string.IsNullOrWhiteSpace(consignmentCode) ? "Thanh toan ky gui" : $"TT {consignmentCode}";
            return desc.Length > 25 ? desc.Substring(0, 25) : desc;
        }

        private static string BuildItemName(string consignmentCode)
        {
            return string.IsNullOrWhiteSpace(consignmentCode) ? "Đơn ký gửi" : $"Ký gửi {consignmentCode}";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }
    }
}
