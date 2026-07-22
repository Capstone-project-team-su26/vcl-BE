using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
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

            // 6. Tạo link thanh toán theo phương thức đã chọn
            var amountInt = (int)Math.Round(quotation.TotalAmount, MidpointRounding.AwayFromZero);
            var purchaseCode = purchaseRequest.PurchaseCode ?? string.Empty;
            var isSePay = string.Equals(request.PaymentMethod, "SEPAY", StringComparison.OrdinalIgnoreCase);

            string checkoutUrl;
            string paymentLinkId;
            string paymentMethodString;

            if (isSePay)
            {
                paymentMethodString = "SEPAY";
                paymentLinkId = $"SEPAY-{orderCode}";
                var baseUrl = _configuration["SePay:BaseUrl"] ?? string.Empty;
                checkoutUrl = $"{baseUrl}/api/payments/sepay/checkout/{orderCode}";
            }
            else
            {
                paymentMethodString = "PAYOS";
                var description = BuildDescription(purchaseCode);

                var inputUrl = FirstNonEmpty(request.ReturnUrl, _configuration["PayOS:ReturnUrl"], string.Empty);
                var isLocal = inputUrl.Contains("localhost") || inputUrl.Contains("127.0.0.1") || inputUrl.Contains("5173") || inputUrl.Contains("3000");
                var baseFrontendUrl = isLocal ? "http://localhost:5173" : "https://logictic.site";

                var returnUrl = $"{baseFrontendUrl}/history/buy-on-behalf";
                var cancelUrl = returnUrl;
                var items = new List<PayOsItem> { new PayOsItem(BuildItemName(purchaseCode), 1, amountInt) };

                PayOsPaymentLink link = await _payOsService.CreatePaymentLinkAsync(
                    orderCode, amountInt, description, returnUrl, cancelUrl, items);

                checkoutUrl = link.CheckoutUrl;
                paymentLinkId = link.PaymentLinkId;
            }

            // 7. Lưu khoản thanh toán ở trạng thái PENDING
            var payment = new PurchasePayment
            {
                Id = Guid.NewGuid(),
                PurchaseRequestId = requestId,
                PurchaseQuotationId = quotation.Id,
                OrderCode = orderCode,
                Amount = quotation.TotalAmount,
                PaymentMethod = paymentMethodString,
                Status = "PENDING",
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
                CheckoutUrl = payment.CheckoutUrl,
                PaymentLinkId = payment.PaymentLinkId
            };
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
                purchaseRequest.Status = "PAID";
                requestRepo.Update(purchaseRequest);
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

            payment.Status = "PAID";
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionCode = dto.ReferenceCode;
            paymentRepo.Update(payment);

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(payment.PurchaseRequestId);
            if (purchaseRequest != null)
            {
                purchaseRequest.Status = "PAID";
                requestRepo.Update(purchaseRequest);
            }

            await _unitOfWork.SaveChangesAsync();
            return true;
        }
    }
}
