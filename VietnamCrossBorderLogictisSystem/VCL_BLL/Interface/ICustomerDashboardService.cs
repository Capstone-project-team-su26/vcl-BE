using System;
using System.Threading.Tasks;
using VCL_BLL.DTOs;

namespace VCL_BLL.Interface
{
    /// <summary>Bảng "việc cần làm" của khách, gộp cả ký gửi lẫn mua hộ.</summary>
    public interface ICustomerDashboardService
    {
        /// <param name="userId">Tài khoản đang đăng nhập; hồ sơ khách tra ra từ đây.</param>
        Task<CustomerDashboardResponse> GetAsync(Guid userId);
    }
}
