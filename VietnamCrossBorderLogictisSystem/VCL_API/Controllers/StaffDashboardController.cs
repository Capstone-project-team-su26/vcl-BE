using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Interface;

namespace VCL_API.Controllers
{
    /// <summary>
    /// Bảng tổng quan của nhân viên — mỗi vai trò một endpoint, một lời gọi trả hết số liệu.
    ///
    /// Phân quyền theo đúng quyền đang áp ở các màn nguồn:
    /// <list type="bullet">
    /// <item>Sale: Sale / Operations Manager / Admin — cùng tập đang xem được danh sách đơn và báo giá.</item>
    /// <item>Vận hành: Operations Manager / Admin — người duyệt chứng từ kho. KHÔNG có số tiền.</item>
    /// <item>Quản trị: chỉ Admin — cùng quyền với <c>/api/admin/finance</c>, vì bảng này có doanh thu.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Authorize]
    public class StaffDashboardController : ControllerBase
    {
        private const string SaleRoles =
            "Admin,ADMIN,Administrator,ADMINISTRATOR,Sale,SALE,Sales,SALES,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS";

        private const string OperationsRoles =
            "Admin,ADMIN,Administrator,ADMINISTRATOR,OperationsManager,OPERATIONSMANAGER,Operations,OPERATIONS";

        private const string AdminRoles = "Admin,ADMIN,Administrator,ADMINISTRATOR";

        private readonly IStaffDashboardService _service;

        public StaffDashboardController(IStaffDashboardService service)
        {
            _service = service;
        }

        /// <summary>Tổng quan của Sale: tỷ giá, tỷ lệ tuyến, trạng thái đơn, xu hướng 7 ngày, việc chờ Sale.</summary>
        /// <response code="200">Số liệu tổng quan (kể cả khi chưa có đơn nào)</response>
        [HttpGet("api/staff/dashboard")]
        [Authorize(Roles = SaleRoles)]
        [ProducesResponseType(typeof(SaleDashboardResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetSaleDashboard()
        {
            var data = await _service.GetSaleDashboardAsync();
            return Ok(new { message = "Lấy tổng quan Sale thành công.", data });
        }

        /// <summary>Tổng quan vận hành: tồn kho theo kho, luồng kiện 7 ngày, lô, sự cố, thời gian xử lý chứng từ.</summary>
        [HttpGet("api/operations/dashboard")]
        [Authorize(Roles = OperationsRoles)]
        [ProducesResponseType(typeof(OperationsDashboardResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetOperationsDashboard()
        {
            var data = await _service.GetOperationsDashboardAsync();
            return Ok(new { message = "Lấy tổng quan vận hành thành công.", data });
        }

        /// <summary>Tổng quan quản trị: người dùng, đơn, dòng tiền, tồn kho.</summary>
        [HttpGet("api/admin/dashboard")]
        [Authorize(Roles = AdminRoles)]
        [ProducesResponseType(typeof(AdminDashboardResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> GetAdminDashboard()
        {
            var data = await _service.GetAdminDashboardAsync();
            return Ok(new { message = "Lấy tổng quan quản trị thành công.", data });
        }
    }
}
