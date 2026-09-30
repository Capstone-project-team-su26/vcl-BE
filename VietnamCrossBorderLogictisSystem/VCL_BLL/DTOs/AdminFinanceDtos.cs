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

    public class AdminPendingTransactionQueryDto
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        /// <summary>Lọc theo nguồn: CONSIGNMENT (ký gửi) hoặc PURCHASE (mua hộ). Bỏ trống lấy cả hai.</summary>
        public string? Source { get; set; }

        /// <summary>Tìm theo mã đơn ký gửi.</summary>
        public string? Search { get; set; }
    }

    /// <summary>
    /// Một khoản tiền đang chờ Admin đối soát tay: khách chọn chuyển khoản tay (OFFLINE nên nằm ở
    /// PENDING_RECONCILIATION), hoặc đã phát hành link cổng thanh toán mà webhook chưa về (PENDING).
    /// </summary>
    public class AdminPendingTransactionDto
    {
        public Guid PaymentId { get; set; }

        /// <summary>CONSIGNMENT (bảng PAYMENTS) hoặc PURCHASE (bảng PURCHASE_PAYMENTS).</summary>
        public string Source { get; set; } = string.Empty;

        public Guid? OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;

        /// <summary>Trạng thái thô trong DB: PENDING hoặc PENDING_RECONCILIATION.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>DEPOSIT · FINAL_PAYMENT · STORAGE_FEE (ký gửi) hoặc DEPOSIT · FINAL (mua hộ).</summary>
        public string InstallmentType { get; set; } = string.Empty;

        /// <summary>Trạng thái đơn tại thời điểm xem, để Admin biết duyệt xong đơn sẽ nhảy đi đâu.</summary>
        public string OrderStatus { get; set; } = string.Empty;

        public long? OrderCode { get; set; }
        public DateTime? CreatedAt { get; set; }

        /// <summary>Số ngày khoản này đã treo, để bảng xếp cái cũ nhất lên đầu.</summary>
        public int WaitingDays { get; set; }
    }

    public class ApproveManualTransactionRequest
    {
        /// <summary>
        /// Mã giao dịch ngân hàng Admin đọc từ sao kê. Ghi vào TransactionCode giống hệt webhook,
        /// để sau này dò ngược từ hệ thống ra sao kê được.
        /// </summary>
        public string? TransactionCode { get; set; }

        public string? Note { get; set; }

        /// <summary>
        /// Số tiền thực nhận theo sao kê. Bỏ trống thì coi bằng số phải thu; nhỏ hơn số phải thu
        /// thì không xác nhận được.
        /// </summary>
        public decimal? ReceivedAmount { get; set; }
    }

    public class RejectManualTransactionRequest
    {
        /// <summary>Bắt buộc — không cho huỷ một khoản tiền mà không để lại lý do.</summary>
        public string Reason { get; set; } = string.Empty;
    }

    public class ManualTransactionResultDto
    {
        public Guid PaymentId { get; set; }
        public string Source { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;

        /// <summary>Trạng thái đơn trước và sau khi duyệt — FE hiện luôn cho Admin thấy hệ quả.</summary>
        public string OrderStatusBefore { get; set; } = string.Empty;
        public string OrderStatusAfter { get; set; } = string.Empty;

        public decimal Amount { get; set; }
        public string TransactionCode { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }
    }
}
