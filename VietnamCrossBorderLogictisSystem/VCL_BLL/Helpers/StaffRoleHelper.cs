using System;
using System.Collections.Generic;
using System.Linq;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Nơi duy nhất khai báo vai trò nội bộ cho mọi luồng nghiệp vụ.
    /// <para>
    /// Trước đây mỗi service tự khai một mảng <c>StaffRoles</c> riêng, và bốn mảng đó khác nhau:
    /// <c>OrderService</c> có <c>WAREHOUSESTAFF</c> và <c>DELIVERY</c>, ba service còn lại thì không.
    /// Hậu quả: nhân viên kho xem được đơn ký gửi nhưng gọi <c>GET /api/purchase-requests</c> lại ra
    /// danh sách rỗng, và xem chi tiết đơn mua hộ thì bị 403 — dù chính họ là người nhận hàng.
    /// Không endpoint nào báo lỗi, nên mỗi lần sửa một chỗ là chỗ khác lệch tiếp.
    /// </para>
    /// <para>
    /// Quan trọng hơn: các mảng cũ bị dùng lẫn cho HAI mục đích khác hẳn nhau — "ai được xem tất cả"
    /// và "ai được thao tác". Thêm một vai trò để sửa lỗi hiển thị là vô tình cấp luôn quyền thao tác.
    /// Vì vậy ở đây tách thành các tập có tên theo đúng ý định; muốn nới quyền thì sửa đúng một tập,
    /// không ảnh hưởng tập còn lại.
    /// </para>
    /// Vai trò trong DB đang có nhiều biến thể chính tả, nên mọi so sánh đều bỏ qua hoa/thường và
    /// khoảng trắng thừa thay vì yêu cầu khớp tuyệt đối.
    /// </summary>
    public static class StaffRoleHelper
    {
        /// <summary>
        /// Nhân viên nội bộ — được xem dữ liệu của MỌI khách, thay vì chỉ đơn của chính mình.
        /// Đây thuần tuý là phạm vi nhìn thấy, không cấp quyền thao tác nào.
        /// </summary>
        private static readonly HashSet<string> ViewAllRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN", "ADMINISTRATOR", "SALE", "SALES", "STAFF",
            "WAREHOUSESTAFF", "OPERATIONSMANAGER", "OPERATIONS", "DELIVERY"
        };

        /// <summary>
        /// Được cập nhật tiến độ mua hàng (đã đặt, người bán đã giao...).
        /// Hẹp hơn <see cref="ViewAllRoles"/>: kho và giao vận nhìn được đơn nhưng không đẩy tiến độ mua.
        /// </summary>
        private static readonly HashSet<string> PurchaseProgressRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN", "ADMINISTRATOR", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS"
        };

        /// <summary>
        /// Được duyệt kiểm kê nhập kho — bước sinh đơn kho và phát hành phiếu.
        /// Sale không nằm trong đây: khớp thiết kế FE, sang bước duyệt kho thì nút phía Sale ẩn đi.
        /// </summary>
        private static readonly HashSet<string> WarehouseApprovalRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN", "ADMINISTRATOR", "OPERATIONSMANAGER", "OPERATIONS", "MANAGER"
        };

        /// <summary>
        /// Được thao tác tiền: tạo đợt thanh toán, chốt chi phí phát sinh, xem lịch sử thanh toán.
        /// Cố ý KHÔNG có kho và giao vận — hai vai trò đó không đụng tới tiền.
        /// </summary>
        private static readonly HashSet<string> PaymentRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN", "ADMINISTRATOR", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS"
        };

        /// <summary>Nhân viên nội bộ (xem được dữ liệu mọi khách) hay không.</summary>
        public static bool IsInternalStaff(string? role) => Matches(ViewAllRoles, role);

        /// <summary>Được cập nhật tiến độ mua hàng của đơn mua hộ hay không.</summary>
        public static bool CanUpdatePurchaseProgress(string? role) => Matches(PurchaseProgressRoles, role);

        /// <summary>Được duyệt kiểm kê nhập kho hay không.</summary>
        public static bool CanApproveWarehouseStore(string? role) => Matches(WarehouseApprovalRoles, role);

        /// <summary>Được thao tác/xem dữ liệu thanh toán hay không.</summary>
        public static bool CanHandlePayments(string? role) => Matches(PaymentRoles, role);

        private static bool Matches(HashSet<string> roles, string? role) =>
            !string.IsNullOrWhiteSpace(role) && roles.Contains(role.Trim());
    }
}
