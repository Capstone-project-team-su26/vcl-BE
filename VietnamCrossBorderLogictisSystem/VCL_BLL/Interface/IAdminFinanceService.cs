using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    public interface IAdminFinanceService
    {
        Task<AdminFinanceSummaryDto> GetFinanceSummaryAsync(DateTime? from, DateTime? to);
        Task<PaginatedResponse<AdminFinanceOrderDto>> GetFinanceOrdersAsync(AdminFinanceOrderQueryDto query);
        Task<PaginatedResponse<AdminFinanceTransactionDto>> GetFinanceTransactionsAsync(AdminFinanceTransactionQueryDto query);

        /// <summary>Các khoản tiền đang treo, chờ Admin đối soát tay.</summary>
        Task<PaginatedResponse<AdminPendingTransactionDto>> GetPendingManualTransactionsAsync(AdminPendingTransactionQueryDto query);

        /// <summary>Admin xác nhận đã nhận được tiền: đánh dấu đã thu và đẩy đơn đi tiếp.</summary>
        Task<ManualTransactionResultDto> ApproveManualTransactionAsync(Guid paymentId, ApproveManualTransactionRequest request);

        /// <summary>Admin từ chối khoản treo (không thấy tiền về, khách khai sai...).</summary>
        Task<ManualTransactionResultDto> RejectManualTransactionAsync(Guid paymentId, RejectManualTransactionRequest request);
    }
}
