using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    public class AdminFinanceService : IAdminFinanceService
    {
        private readonly IUnitOfWork _unitOfWork;

        public AdminFinanceService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<AdminFinanceSummaryDto> GetFinanceSummaryAsync(DateTime? from, DateTime? to)
        {
            var calculatedOrders = await CalculateOrdersFinanceDataAsync(from, to);

            var totalBillAmount = calculatedOrders.Sum(o => o.TotalBillAmount);
            var totalPaid = calculatedOrders.Sum(o => o.TotalPaid);
            var remaining = calculatedOrders.Sum(o => o.Remaining);

            var paidCount = calculatedOrders.Count(o => string.Equals(o.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase));
            var partialCount = calculatedOrders.Count(o => string.Equals(o.PaymentStatus, "PARTIAL", StringComparison.OrdinalIgnoreCase));
            var unpaidCount = calculatedOrders.Count(o => string.Equals(o.PaymentStatus, "UNPAID", StringComparison.OrdinalIgnoreCase));

            return new AdminFinanceSummaryDto
            {
                TotalBillAmount = totalBillAmount,
                TotalPaid = totalPaid,
                Remaining = remaining,
                OrderCount = calculatedOrders.Count,
                PaidCount = paidCount,
                PartialCount = partialCount,
                UnpaidCount = unpaidCount
            };
        }

        public async Task<PaginatedResponse<AdminFinanceOrderDto>> GetFinanceOrdersAsync(AdminFinanceOrderQueryDto query)
        {
            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.PageSize < 1 ? 20 : query.PageSize;

            var calculatedOrders = await CalculateOrdersFinanceDataAsync(query.From, query.To);

            var filtered = calculatedOrders.AsQueryable();

            // Filter by PaymentStatus if provided
            if (!string.IsNullOrWhiteSpace(query.PaymentStatus))
            {
                var targetStatus = query.PaymentStatus.Trim();
                filtered = filtered.Where(o => string.Equals(o.PaymentStatus, targetStatus, StringComparison.OrdinalIgnoreCase));
            }

            // Filter by search string (consignment code, customer name, customer code)
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                filtered = filtered.Where(o =>
                    (!string.IsNullOrEmpty(o.ConsignmentCode) && o.ConsignmentCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(o.CustomerName) && o.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(o.CustomerCode) && o.CustomerCode.Contains(term, StringComparison.OrdinalIgnoreCase))
                );
            }

            var totalCount = filtered.Count();

            var items = filtered
                .OrderByDescending(o => o.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new AdminFinanceOrderDto
                {
                    OrderId = o.OrderId,
                    ConsignmentCode = o.ConsignmentCode,
                    CustomerName = o.CustomerName,
                    CustomerCode = o.CustomerCode,
                    OrderStatus = o.OrderStatus,
                    TotalBillAmount = o.TotalBillAmount,
                    TotalPaid = o.TotalPaid,
                    Remaining = o.Remaining,
                    PaymentStatus = o.PaymentStatus,
                    LastPaidAt = o.LastPaidAt
                })
                .ToList();

            return new PaginatedResponse<AdminFinanceOrderDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<PaginatedResponse<AdminFinanceTransactionDto>> GetFinanceTransactionsAsync(AdminFinanceTransactionQueryDto query)
        {
            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.PageSize < 1 ? 20 : query.PageSize;

            var allPayments = await _unitOfWork.Repository<Payment>().GetAllAsync();
            var allInvoices = await _unitOfWork.Repository<Invoice>().GetAllAsync();
            var allQuotations = await _unitOfWork.Repository<Quotation>().GetAllAsync();
            var allOrders = await _unitOfWork.Repository<Order>().GetAllAsync();

            var invoiceMap = allInvoices.ToDictionary(i => i.Id, i => i);
            var quotationMap = allQuotations.ToDictionary(q => q.Id, q => q);
            var orderMap = allOrders.ToDictionary(o => o.Id, o => o);

            var rows = new List<AdminFinanceTransactionDto>();

            foreach (var payment in allPayments)
            {
                Order? order = null;
                if (payment.InvoiceId.HasValue && invoiceMap.TryGetValue(payment.InvoiceId.Value, out var invoice))
                {
                    if (invoice.QuotationId.HasValue && quotationMap.TryGetValue(invoice.QuotationId.Value, out var quotation))
                    {
                        if (quotation.OrderId.HasValue && orderMap.TryGetValue(quotation.OrderId.Value, out var orderObj))
                        {
                            order = orderObj;
                        }
                    }
                }

                rows.Add(new AdminFinanceTransactionDto
                {
                    PaymentId = payment.Id,
                    OrderId = order?.Id,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    Amount = payment.Amount ?? 0m,
                    PaymentMethod = payment.PaymentMethod ?? string.Empty,
                    Status = NormalizePaymentStatus(payment.PaymentStatus),
                    PaidAt = payment.PaidAt ?? payment.CreatedAt
                });
            }

            // Tiền mua hộ nằm ở bộ bảng riêng (PURCHASE_PAYMENTS), nối với đơn qua
            // Order.PurchaseRequestId chứ không qua QUOTATIONS. Bỏ qua bảng này thì báo cáo dòng
            // tiền chỉ phản ánh nửa doanh thu — nhìn vào tưởng ít hơn thực tế.
            var purchasePayments = await _unitOfWork.Repository<PurchasePayment>().GetAllAsync();
            var orderByPurchaseRequest = allOrders
                .Where(o => o.PurchaseRequestId.HasValue)
                .GroupBy(o => o.PurchaseRequestId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var payment in purchasePayments)
            {
                orderByPurchaseRequest.TryGetValue(payment.PurchaseRequestId, out var order);

                rows.Add(new AdminFinanceTransactionDto
                {
                    PaymentId = payment.Id,
                    OrderId = order?.Id,
                    ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                    Amount = payment.Amount,
                    PaymentMethod = payment.PaymentMethod ?? string.Empty,
                    Status = NormalizePaymentStatus(payment.Status),
                    PaidAt = payment.PaidAt ?? payment.CreatedAt
                });
            }

            var filtered = rows.AsEnumerable();

            if (query.From.HasValue)
            {
                filtered = filtered.Where(r => r.PaidAt >= query.From.Value);
            }

            if (query.To.HasValue)
            {
                filtered = filtered.Where(r => r.PaidAt <= query.To.Value);
            }

            var ordered = filtered.OrderByDescending(r => r.PaidAt).ToList();

            return new PaginatedResponse<AdminFinanceTransactionDto>
            {
                Items = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = ordered.Count,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        private class TempOrderFinance
        {
            public Guid OrderId { get; set; }
            public string ConsignmentCode { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public string CustomerCode { get; set; } = string.Empty;
            public string OrderStatus { get; set; } = string.Empty;
            public decimal TotalBillAmount { get; set; }
            public decimal TotalPaid { get; set; }
            public decimal Remaining { get; set; }
            public string PaymentStatus { get; set; } = "UNPAID";
            public DateTime? LastPaidAt { get; set; }
            public DateTime? CreatedAt { get; set; }
        }

        private async Task<List<TempOrderFinance>> CalculateOrdersFinanceDataAsync(DateTime? from, DateTime? to)
        {
            var allOrders = await _unitOfWork.Repository<Order>().GetAllAsync();
            var query = allOrders.AsQueryable();

            if (from.HasValue)
            {
                query = query.Where(o => o.CreatedAt >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(o => o.CreatedAt <= to.Value);
            }

            var orders = query.ToList();

            var allCustomers = (await _unitOfWork.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id, c => c);
            var allQuotations = await _unitOfWork.Repository<Quotation>().GetAllAsync();
            var allQuotationFees = await _unitOfWork.Repository<QuotationFee>().GetAllAsync();
            var allInvoices = await _unitOfWork.Repository<Invoice>().GetAllAsync();
            var allPayments = await _unitOfWork.Repository<Payment>().GetAllAsync();

            var quotationsByOrder = allQuotations.Where(q => q.OrderId.HasValue)
                .GroupBy(q => q.OrderId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var feesByQuotation = allQuotationFees.Where(f => f.QuotationId.HasValue)
                .GroupBy(f => f.QuotationId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var invoicesByQuotation = allInvoices.Where(i => i.QuotationId.HasValue)
                .GroupBy(i => i.QuotationId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var paymentsByInvoice = allPayments.Where(p => p.InvoiceId.HasValue)
                .GroupBy(p => p.InvoiceId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Nguồn tiền của luồng mua hộ, gom sẵn theo yêu cầu mua để tra nhanh trong vòng lặp.
            var purchasePaymentsByRequest = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                .GroupBy(p => p.PurchaseRequestId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var purchaseQuotationTotals = (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                .GroupBy(q => q.PurchaseRequestId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(q => q.CreatedAt).First().TotalAmount);

            var result = new List<TempOrderFinance>();

            foreach (var order in orders)
            {
                allCustomers.TryGetValue(order.CustomerId ?? Guid.Empty, out var customer);

                quotationsByOrder.TryGetValue(order.Id, out var orderQuotations);
                orderQuotations ??= new List<Quotation>();

                var orderQuotationIds = orderQuotations.Select(q => q.Id).ToHashSet();

                var orderInvoices = new List<Invoice>();
                foreach (var qId in orderQuotationIds)
                {
                    if (invoicesByQuotation.TryGetValue(qId, out var invs))
                    {
                        orderInvoices.AddRange(invs);
                    }
                }

                var latestInvoice = orderInvoices.OrderByDescending(i => i.IssuedAt).FirstOrDefault();
                var orderInvoiceIds = orderInvoices.Select(i => i.Id).ToHashSet();

                var orderPayments = new List<Payment>();
                foreach (var invId in orderInvoiceIds)
                {
                    if (paymentsByInvoice.TryGetValue(invId, out var pmts))
                    {
                        orderPayments.AddRange(pmts);
                    }
                }

                decimal totalBillAmount = 0m;
                if (latestInvoice != null && latestInvoice.TotalAmount.HasValue && latestInvoice.TotalAmount.Value > 0)
                {
                    totalBillAmount = latestInvoice.TotalAmount.Value;
                }
                else
                {
                    var latestQuotation = orderQuotations.OrderByDescending(q => q.CreatedAt).FirstOrDefault();
                    if (latestQuotation != null)
                    {
                        var freight = latestQuotation.EstimatedFreightCharge ?? 0m;
                        var service = latestQuotation.ServiceFee ?? 0m;
                        var tax = latestQuotation.TaxAndDuty ?? 0m;

                        feesByQuotation.TryGetValue(latestQuotation.Id, out var qFees);
                        decimal feesSum = qFees?.Sum(f => f.Amount) ?? 0m;

                        totalBillAmount = freight + service + tax + feesSum;
                    }
                }

                var successfulPayments = orderPayments
                    .Where(p => IsSuccessfulPayment(p.PaymentStatus))
                    .ToList();

                var totalPaid = successfulPayments.Sum(p => p.Amount ?? 0m);

                // Đơn mua hộ chạy trên bộ bảng tiền riêng: báo giá và khoản thu đều không nằm ở
                // QUOTATIONS/PAYMENTS. Không cộng thêm ở đây thì mọi đơn mua hộ hiện 0đ và bị
                // xếp UNPAID dù khách đã trả — báo cáo dòng tiền hụt đúng phần doanh thu đó.
                if (order.PurchaseRequestId.HasValue
                    && purchasePaymentsByRequest.TryGetValue(order.PurchaseRequestId.Value, out var purchasePaid))
                {
                    var purchaseSuccessful = purchasePaid
                        .Where(p => IsSuccessfulPayment(p.Status))
                        .ToList();

                    totalPaid += purchaseSuccessful.Sum(p => p.Amount);

                    if (totalBillAmount <= 0m
                        && purchaseQuotationTotals.TryGetValue(order.PurchaseRequestId.Value, out var purchaseBill))
                    {
                        totalBillAmount = purchaseBill;
                    }

                    // Chưa chốt được báo giá thì lấy chính số đã thu làm mốc, còn hơn để 0đ rồi
                    // gắn nhãn "chưa trả" cho đơn khách đã thanh toán.
                    if (totalBillAmount <= 0m)
                    {
                        totalBillAmount = totalPaid;
                    }
                }

                var remaining = Math.Max(0m, totalBillAmount - totalPaid);

                string paymentStatus = "UNPAID";
                if (totalBillAmount > 0 && totalPaid >= totalBillAmount)
                {
                    paymentStatus = "PAID";
                }
                else if (totalPaid > 0)
                {
                    paymentStatus = "PARTIAL";
                }
                else
                {
                    paymentStatus = "UNPAID";
                }

                DateTime? lastPaidAt = successfulPayments
                    .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                    .Select(p => p.PaidAt ?? p.CreatedAt)
                    .FirstOrDefault();

                result.Add(new TempOrderFinance
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode ?? string.Empty,
                    CustomerName = customer?.FullName ?? string.Empty,
                    CustomerCode = customer?.CustomerCode ?? string.Empty,
                    OrderStatus = order.Status ?? string.Empty,
                    TotalBillAmount = totalBillAmount,
                    TotalPaid = totalPaid,
                    Remaining = remaining,
                    PaymentStatus = paymentStatus,
                    LastPaidAt = lastPaidAt,
                    CreatedAt = order.CreatedAt
                });
            }

            return result;
        }

        private static bool IsSuccessfulPayment(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            var s = status.Trim().ToUpperInvariant();
            return s == "PAID" || s == "SUCCESS";
        }

        private static string NormalizePaymentStatus(string? raw)
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

        // =====================================================================
        // DUYỆT GIAO DỊCH THỦ CÔNG
        //
        // Vì sao cần: khách chọn OFFLINE thì khoản tiền sinh ra ở PENDING_RECONCILIATION, mà trong
        // toàn hệ thống không có đường nào đưa nó sang PAID — chỉ webhook SePay/PayOS mới làm được.
        // Khoản PENDING của cổng thanh toán cũng kẹt y vậy khi webhook không bắn về. Hệ quả là
        // Sale không bấm được PUT /orders/{id}/payment-confirmation (nó đòi khoản đã SUCCESS),
        // đơn nằm mãi ở WAITING_DEPOSIT. Đây là chỗ Admin gỡ tay sau khi đối chiếu sao kê.
        //
        // Side-effect ở dưới cố ý chép đúng HandleSePayWebhookAsync (ConsignmentPaymentService) và
        // MarkPaymentAsPaidAsync (PurchasePaymentService). Lệch một nhánh là đơn duyệt tay và đơn
        // duyệt qua webhook rẽ hai hướng khác nhau.
        // =====================================================================

        private const string PendingReconciliation = "PENDING_RECONCILIATION";
        private const string StorageFeeInstallment = "STORAGE_FEE";

        /// <summary>Khoản đang treo chờ người thật xác nhận.</summary>
        private static bool IsAwaitingManualReview(string? status)
        {
            var s = status?.Trim().ToUpperInvariant() ?? string.Empty;
            return s == "PENDING" || s == PendingReconciliation;
        }

        public async Task<PaginatedResponse<AdminPendingTransactionDto>> GetPendingManualTransactionsAsync(
            AdminPendingTransactionQueryDto query)
        {
            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.PageSize < 1 ? 20 : query.PageSize;

            var source = query.Source?.Trim().ToUpperInvariant() ?? string.Empty;
            var wantConsignment = source.Length == 0 || source == "CONSIGNMENT";
            var wantPurchase = source.Length == 0 || source == "PURCHASE";

            var allOrders = (await _unitOfWork.Repository<Order>().GetAllAsync()).ToList();
            var orderMap = allOrders.ToDictionary(o => o.Id, o => o);
            var customerMap = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .ToDictionary(c => c.Id, c => c);

            var rows = new List<AdminPendingTransactionDto>();
            var now = DateTime.UtcNow;

            if (wantConsignment)
            {
                var invoiceMap = (await _unitOfWork.Repository<Invoice>().GetAllAsync()).ToDictionary(i => i.Id, i => i);
                var quotationMap = (await _unitOfWork.Repository<Quotation>().GetAllAsync()).ToDictionary(q => q.Id, q => q);

                foreach (var payment in await _unitOfWork.Repository<Payment>().GetAllAsync())
                {
                    if (!IsAwaitingManualReview(payment.PaymentStatus)) continue;

                    var order = ResolveOrderFromPayment(payment, invoiceMap, quotationMap, orderMap);
                    customerMap.TryGetValue(order?.CustomerId ?? Guid.Empty, out var customer);

                    rows.Add(new AdminPendingTransactionDto
                    {
                        PaymentId = payment.Id,
                        Source = "CONSIGNMENT",
                        OrderId = order?.Id,
                        ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                        CustomerName = customer?.FullName ?? string.Empty,
                        Amount = payment.Amount ?? 0m,
                        PaymentMethod = payment.PaymentMethod ?? string.Empty,
                        Status = payment.PaymentStatus?.Trim().ToUpperInvariant() ?? string.Empty,
                        InstallmentType = payment.InstallmentType ?? string.Empty,
                        OrderStatus = order?.Status ?? string.Empty,
                        OrderCode = payment.OrderCode,
                        CreatedAt = payment.CreatedAt,
                        WaitingDays = CountWaitingDays(payment.CreatedAt, now)
                    });
                }
            }

            if (wantPurchase)
            {
                var orderByPurchaseRequest = allOrders
                    .Where(o => o.PurchaseRequestId.HasValue)
                    .GroupBy(o => o.PurchaseRequestId!.Value)
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var payment in await _unitOfWork.Repository<PurchasePayment>().GetAllAsync())
                {
                    if (!IsAwaitingManualReview(payment.Status)) continue;

                    orderByPurchaseRequest.TryGetValue(payment.PurchaseRequestId, out var order);
                    customerMap.TryGetValue(order?.CustomerId ?? Guid.Empty, out var customer);

                    rows.Add(new AdminPendingTransactionDto
                    {
                        PaymentId = payment.Id,
                        Source = "PURCHASE",
                        OrderId = order?.Id,
                        ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                        CustomerName = customer?.FullName ?? string.Empty,
                        Amount = payment.Amount,
                        PaymentMethod = payment.PaymentMethod ?? string.Empty,
                        Status = payment.Status?.Trim().ToUpperInvariant() ?? string.Empty,
                        InstallmentType = payment.PaymentType ?? string.Empty,
                        OrderStatus = order?.Status ?? string.Empty,
                        OrderCode = payment.OrderCode,
                        CreatedAt = payment.CreatedAt,
                        WaitingDays = CountWaitingDays(payment.CreatedAt, now)
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                rows = rows.Where(r =>
                    (!string.IsNullOrEmpty(r.ConsignmentCode) && r.ConsignmentCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(r.CustomerName) && r.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase))
                ).ToList();
            }

            // Treo lâu nhất lên đầu — đó là khoản khách đã chuyển tiền mà chờ lâu nhất.
            var ordered = rows
                .OrderByDescending(r => r.WaitingDays)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();

            return new PaginatedResponse<AdminPendingTransactionDto>
            {
                Items = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = ordered.Count,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<ManualTransactionResultDto> ApproveManualTransactionAsync(
            Guid paymentId, ApproveManualTransactionRequest request)
        {
            request ??= new ApproveManualTransactionRequest();

            var consignmentPayment = await _unitOfWork.Repository<Payment>().GetByIdAsync(paymentId);
            if (consignmentPayment != null)
                return await ApproveConsignmentPaymentAsync(consignmentPayment, request);

            var purchasePayment = await _unitOfWork.Repository<PurchasePayment>().GetByIdAsync(paymentId);
            if (purchasePayment != null)
                return await ApprovePurchasePaymentAsync(purchasePayment, request);

            throw new NotFoundException($"Không tìm thấy giao dịch với ID: {paymentId}");
        }

        private async Task<ManualTransactionResultDto> ApproveConsignmentPaymentAsync(
            Payment payment, ApproveManualTransactionRequest request)
        {
            if (IsSuccessfulPayment(payment.PaymentStatus))
                throw new ArgumentException("Giao dịch này đã được ghi nhận thanh toán trước đó.");

            if (!IsAwaitingManualReview(payment.PaymentStatus))
                throw new ArgumentException(
                    $"Chỉ duyệt được khoản đang PENDING hoặc {PendingReconciliation}. Khoản này đang ở: {payment.PaymentStatus}.");

            var invoiceRepo = _unitOfWork.Repository<Invoice>();
            var orderRepo = _unitOfWork.Repository<Order>();

            Order? order = null;
            Invoice? invoice = null;
            if (payment.InvoiceId.HasValue)
            {
                invoice = await invoiceRepo.GetByIdAsync(payment.InvoiceId.Value);
                if (invoice?.QuotationId != null)
                {
                    var quotation = await _unitOfWork.Repository<Quotation>().GetByIdAsync(invoice.QuotationId.Value);
                    if (quotation?.OrderId != null)
                        order = await orderRepo.GetByIdAsync(quotation.OrderId.Value);
                }
            }

            var statusBefore = order?.Status ?? string.Empty;

            payment.PaymentStatus = "PAID";
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionCode = BuildManualTransactionCode(request.TransactionCode);
            _unitOfWork.Repository<Payment>().Update(payment);

            if (order != null)
            {
                var installment = payment.InstallmentType?.Trim().ToUpperInvariant();
                if (installment == "DEPOSIT")
                {
                    order.Status = "DEPOSIT_PAID";
                    orderRepo.Update(order);
                }
                else if (installment == StorageFeeInstallment)
                {
                    // Phí lưu kho thu tại quầy, không phải một đợt của hoá đơn gốc — đụng vào
                    // order.Status ở đây sẽ đánh dấu đơn tất toán xong trong khi thực tế còn dở.
                }
                else
                {
                    order.Status = "PAID";
                    orderRepo.Update(order);
                    if (invoice != null)
                    {
                        invoice.InvoiceStatus = "PAID";
                        invoiceRepo.Update(invoice);
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return new ManualTransactionResultDto
            {
                PaymentId = payment.Id,
                Source = "CONSIGNMENT",
                PaymentStatus = payment.PaymentStatus,
                OrderId = order?.Id,
                ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                OrderStatusBefore = statusBefore,
                OrderStatusAfter = order?.Status ?? string.Empty,
                Amount = payment.Amount ?? 0m,
                TransactionCode = payment.TransactionCode ?? string.Empty,
                PaidAt = payment.PaidAt
            };
        }

        private async Task<ManualTransactionResultDto> ApprovePurchasePaymentAsync(
            PurchasePayment payment, ApproveManualTransactionRequest request)
        {
            if (IsSuccessfulPayment(payment.Status))
                throw new ArgumentException("Giao dịch này đã được ghi nhận thanh toán trước đó.");

            if (!IsAwaitingManualReview(payment.Status))
                throw new ArgumentException(
                    $"Chỉ duyệt được khoản đang PENDING hoặc {PendingReconciliation}. Khoản này đang ở: {payment.Status}.");

            var requestRepo = _unitOfWork.Repository<PurchaseRequest>();
            var purchaseRequest = await requestRepo.GetByIdAsync(payment.PurchaseRequestId);
            var statusBefore = purchaseRequest?.Status ?? string.Empty;

            payment.Status = "PAID";
            payment.PaidAt = DateTime.UtcNow;
            payment.TransactionCode = BuildManualTransactionCode(request.TransactionCode);
            _unitOfWork.Repository<PurchasePayment>().Update(payment);

            if (purchaseRequest != null)
            {
                // Dữ liệu cũ để trống payment_type -> coi như đợt đầu, giống IsPaymentType bên
                // PurchasePaymentService (mặc định FULL_PAYMENT chứ không phải FINAL_PAYMENT).
                var isFinal = string.Equals(payment.PaymentType?.Trim(), "FINAL_PAYMENT", StringComparison.OrdinalIgnoreCase);
                var paidStatus = isFinal ? "COMPLETED" : "PAID";

                // Không kéo lùi tiến độ: Sale có thể đã bấm PURCHASED/SELLER_SHIPPED trước khi tiền
                // về. Giữ nguyên luật của MarkPaymentAsPaidAsync bên PurchasePaymentService.
                if (paidStatus == "COMPLETED" || !IsPurchaseProgressStatus(purchaseRequest.Status))
                {
                    purchaseRequest.Status = paidStatus;
                    requestRepo.Update(purchaseRequest);
                }
            }

            await _unitOfWork.SaveChangesAsync();

            var order = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .FirstOrDefault(o => o.PurchaseRequestId == payment.PurchaseRequestId);

            return new ManualTransactionResultDto
            {
                PaymentId = payment.Id,
                Source = "PURCHASE",
                PaymentStatus = payment.Status,
                OrderId = order?.Id,
                ConsignmentCode = order?.ConsignmentCode ?? string.Empty,
                OrderStatusBefore = statusBefore,
                OrderStatusAfter = purchaseRequest?.Status ?? string.Empty,
                Amount = payment.Amount,
                TransactionCode = payment.TransactionCode ?? string.Empty,
                PaidAt = payment.PaidAt
            };
        }

        public async Task<ManualTransactionResultDto> RejectManualTransactionAsync(
            Guid paymentId, RejectManualTransactionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Reason))
                throw new ArgumentException("Phải ghi lý do khi từ chối một khoản thanh toán.");

            var reason = request.Reason.Trim();

            var consignmentPayment = await _unitOfWork.Repository<Payment>().GetByIdAsync(paymentId);
            if (consignmentPayment != null)
            {
                if (IsSuccessfulPayment(consignmentPayment.PaymentStatus))
                    throw new ArgumentException("Khoản này đã ghi nhận thu tiền, không từ chối được nữa.");
                if (!IsAwaitingManualReview(consignmentPayment.PaymentStatus))
                    throw new ArgumentException($"Khoản này đang ở {consignmentPayment.PaymentStatus}, không còn chờ đối soát.");

                consignmentPayment.PaymentStatus = "CANCELLED";
                consignmentPayment.FailureReason = reason;
                _unitOfWork.Repository<Payment>().Update(consignmentPayment);
                await _unitOfWork.SaveChangesAsync();

                return new ManualTransactionResultDto
                {
                    PaymentId = consignmentPayment.Id,
                    Source = "CONSIGNMENT",
                    PaymentStatus = consignmentPayment.PaymentStatus,
                    Amount = consignmentPayment.Amount ?? 0m
                };
            }

            var purchasePayment = await _unitOfWork.Repository<PurchasePayment>().GetByIdAsync(paymentId);
            if (purchasePayment != null)
            {
                if (IsSuccessfulPayment(purchasePayment.Status))
                    throw new ArgumentException("Khoản này đã ghi nhận thu tiền, không từ chối được nữa.");
                if (!IsAwaitingManualReview(purchasePayment.Status))
                    throw new ArgumentException($"Khoản này đang ở {purchasePayment.Status}, không còn chờ đối soát.");

                purchasePayment.Status = "CANCELLED";
                // PURCHASE_PAYMENTS không có cột lý do như PAYMENTS.FailureReason. Ghi nhờ vào
                // TransactionCode (đang trống vì khoản chưa thu) để lý do không bị mất hẳn.
                purchasePayment.TransactionCode = Truncate($"MANUAL-REJECT: {reason}", 200);
                _unitOfWork.Repository<PurchasePayment>().Update(purchasePayment);
                await _unitOfWork.SaveChangesAsync();

                return new ManualTransactionResultDto
                {
                    PaymentId = purchasePayment.Id,
                    Source = "PURCHASE",
                    PaymentStatus = purchasePayment.Status,
                    Amount = purchasePayment.Amount
                };
            }

            throw new NotFoundException($"Không tìm thấy giao dịch với ID: {paymentId}");
        }

        private static Order? ResolveOrderFromPayment(
            Payment payment,
            Dictionary<Guid, Invoice> invoiceMap,
            Dictionary<Guid, Quotation> quotationMap,
            Dictionary<Guid, Order> orderMap)
        {
            if (!payment.InvoiceId.HasValue) return null;
            if (!invoiceMap.TryGetValue(payment.InvoiceId.Value, out var invoice)) return null;
            if (!invoice.QuotationId.HasValue) return null;
            if (!quotationMap.TryGetValue(invoice.QuotationId.Value, out var quotation)) return null;
            if (!quotation.OrderId.HasValue) return null;
            return orderMap.TryGetValue(quotation.OrderId.Value, out var order) ? order : null;
        }

        private static int CountWaitingDays(DateTime? createdAt, DateTime now)
        {
            if (!createdAt.HasValue) return 0;
            var days = (now - createdAt.Value).TotalDays;
            return days <= 0 ? 0 : (int)Math.Floor(days);
        }

        /// <summary>
        /// Ghi dấu "khoản này do người duyệt tay" ngay trong mã giao dịch. Admin nhập mã sao kê thì
        /// giữ nguyên mã đó kèm tiền tố, bỏ trống thì đóng dấu thời gian để vẫn phân biệt được với
        /// mã do cổng thanh toán trả về.
        /// </summary>
        private static string BuildManualTransactionCode(string? bankReference)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            return string.IsNullOrWhiteSpace(bankReference)
                ? $"MANUAL-{stamp}"
                : Truncate($"MANUAL-{bankReference.Trim()}", 200);
        }

        private static string Truncate(string value, int maxLength)
            => value.Length <= maxLength ? value : value.Substring(0, maxLength);

        /// <summary>
        /// Trạng thái cho thấy Sale đã đẩy yêu cầu mua hộ đi xa hơn mốc "đã trả tiền".
        /// Danh sách chép nguyên từ PurchasePaymentService.PurchaseProgressStatuses để duyệt tay
        /// không kéo lùi tiến độ đơn.
        /// </summary>
        private static readonly string[] PurchaseProgressStatuses =
            { "PURCHASED", "SELLER_SHIPPED", "ARRIVED_ORIGIN_WAREHOUSE" };

        private static bool IsPurchaseProgressStatus(string? status) =>
            !string.IsNullOrWhiteSpace(status)
            && PurchaseProgressStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
