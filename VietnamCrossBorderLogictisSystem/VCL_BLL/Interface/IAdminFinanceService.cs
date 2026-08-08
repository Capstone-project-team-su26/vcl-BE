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
    }
}
