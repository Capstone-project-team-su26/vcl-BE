using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>
    /// Bảng tổng quan của nhân viên theo vai trò. Mọi con số được đếm/cộng ở đây từ bảng gốc;
    /// phân quyền (ai gọi được bảng nào) đặt ở controller.
    /// </summary>
    public interface IStaffDashboardService
    {
        /// <summary>Sale: tỷ giá, tỷ lệ tuyến, trạng thái đơn, xu hướng 7 ngày, việc chờ Sale, đơn mới nhất.</summary>
        Task<SaleDashboardResponse> GetSaleDashboardAsync();

        /// <summary>Quản lý vận hành: tồn kho, luồng kiện 7 ngày, lô, sự cố, thời gian xử lý. Không có tiền.</summary>
        Task<OperationsDashboardResponse> GetOperationsDashboardAsync();

        /// <summary>Admin: người dùng, đơn, dòng tiền, tồn kho.</summary>
        Task<AdminDashboardResponse> GetAdminDashboardAsync();
    }
}
