using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
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

            var queryable = allPayments.AsQueryable();

            if (query.From.HasValue)
            {
                queryable = queryable.Where(p => (p.PaidAt ?? p.CreatedAt) >= query.From.Value);
            }

            if (query.To.HasValue)
            {
                queryable = queryable.Where(p => (p.PaidAt ?? p.CreatedAt) <= query.To.Value);
            }

            var totalCount = queryable.Count();

            var paginated = queryable
                .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var items = new List<AdminFinanceTransactionDto>();
            foreach (var payment in paginated)
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

                items.Add(new AdminFinanceTransactionDto
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

            return new PaginatedResponse<AdminFinanceTransactionDto>
            {
                Items = items,
                TotalCount = totalCount,
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
    }
}
