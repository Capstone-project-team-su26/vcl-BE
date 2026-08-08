using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class AdminFinanceSummaryDto
    {
        public decimal TotalBillAmount { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Remaining { get; set; }
        public int OrderCount { get; set; }
        public int PaidCount { get; set; }
        public int PartialCount { get; set; }
        public int UnpaidCount { get; set; }
    }

    public class AdminFinanceOrderQueryDto
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? PaymentStatus { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Search { get; set; }
    }

    public class AdminFinanceOrderDto
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
    }

    public class AdminFinanceTransactionQueryDto
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public class AdminFinanceTransactionDto
    {
        public Guid PaymentId { get; set; }
        public Guid? OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }
    }
}
