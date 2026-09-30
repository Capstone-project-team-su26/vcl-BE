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

        /// <summary>
        /// Được XEM XÉT (đổi trạng thái IN_REVIEW / NEED_MORE_INFO / REJECTED), LẬP BÁO GIÁ và từ chối
        /// báo giá thay khách của yêu cầu mua hộ. Chỉ Sale và Admin: báo giá là cam kết giá với khách.
        /// Kho, giao vận, Operations Manager và STAFF chung vẫn XEM được yêu cầu (<see cref="ViewAllRoles"/>)
        /// nhưng không ghi được — trước đây hai thao tác này chỉ kiểm "nhân viên nội bộ" nên ai cũng làm được.
        /// </summary>
        private static readonly HashSet<string> PurchaseQuoteRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN", "ADMINISTRATOR", "SALE", "SALES"
        };

        /// <summary>Được xem xét / lập báo giá yêu cầu mua hộ hay không (chỉ Sale, Admin).</summary>
        public static bool CanQuotePurchase(string? role) => Matches(PurchaseQuoteRoles, role);

        /// <summary>Nhân viên nội bộ (xem được dữ liệu mọi khách) hay không.</summary>
        public static bool IsInternalStaff(string? role) =>
            Matches(ViewAllRoles, role) || IsWarehouseStaffRole(role);

        /// <summary>Được cập nhật tiến độ mua hàng của đơn mua hộ hay không.</summary>
        public static bool CanUpdatePurchaseProgress(string? role) => Matches(PurchaseProgressRoles, role);

        /// <summary>Được duyệt kiểm kê nhập kho hay không.</summary>
        public static bool CanApproveWarehouseStore(string? role) => Matches(WarehouseApprovalRoles, role);

        /// <summary>Được thao tác/xem dữ liệu thanh toán hay không.</summary>
        public static bool CanHandlePayments(string? role) => Matches(PaymentRoles, role);

        /// <summary>
        /// Nhân viên kho — người thao tác hàng thật: lập phiếu xuất, bốc hàng, lập lô, bàn giao.
        /// Cố ý KHÔNG có Operations Manager: người duyệt phiếu không được tự làm thao tác kho.
        /// </summary>
        private static readonly HashSet<string> WarehouseOperationRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN", "ADMINISTRATOR", "WAREHOUSESTAFF", "WAREHOUSE STAFF", "STAFF"
        };

        /// <summary>Được thao tác kho ở luồng xuất (lập phiếu, bốc hàng, lập lô, bàn giao).</summary>
        public static bool CanOperateWarehouse(string? role) =>
            Matches(WarehouseOperationRoles, role) || IsWarehouseStaffRole(role);

        /// <summary>
        /// Được cập nhật hành trình lô sau khi hàng rời kho nguồn và đính chứng từ thông quan.
        /// Đây là việc của Sale; kho không theo dõi chặng quốc tế.
        /// </summary>
        private static readonly HashSet<string> ShipmentTrackingRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ADMIN", "ADMINISTRATOR", "SALE", "SALES", "STAFF", "OPERATIONSMANAGER", "OPERATIONS"
        };

        public static bool CanUpdateShipmentTracking(string? role) => Matches(ShipmentTrackingRoles, role);

        /// <summary>
        /// Tài khoản có vai trò NHÂN VIÊN KHO hay không — dùng cho việc gán nhân viên vào kho.
        /// DB có nhiều biến thể tên (<c>Warehouse</c>, <c>Warehouse Staff</c>, <c>WarehouseStaff</c>,
        /// <c>WarehouseTQ</c>, <c>WarehouseVN</c>, <c>Warehouse Staff VN</c>…), nên so theo tiền tố
        /// sau khi bỏ khoảng trắng/gạch dưới. Quản lý kho (<c>WarehouseManager</c>) không tính.
        /// </summary>
        public static bool IsWarehouseStaffRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            var compact = new string(role.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray())
                .ToUpperInvariant();
            return compact.StartsWith("WAREHOUSE", StringComparison.Ordinal)
                && !compact.StartsWith("WAREHOUSEMANAGER", StringComparison.Ordinal);
        }

        /// <summary>
        /// <paramref name="role"/> có thuộc tập <paramref name="allowedRoles"/> không (bỏ qua hoa/thường,
        /// khoảng trắng hai đầu). Tập có nhân viên kho (<c>WAREHOUSESTAFF</c>…) thì mọi biến thể tên vai
        /// trò nhân viên kho (<see cref="IsWarehouseStaffRole"/>) cũng khớp — thay cho các mảng
        /// <c>"WAREHOUSESTAFF"</c> so chính xác rải ở từng service. Không nới quyền cho vai trò khác.
        /// </summary>
        public static bool IsRoleAllowed(IEnumerable<string> allowedRoles, string? role)
        {
            if (string.IsNullOrWhiteSpace(role) || allowedRoles == null) return false;
            var trimmed = role.Trim();
            var list = allowedRoles as IReadOnlyCollection<string> ?? allowedRoles.ToList();
            if (list.Any(r => string.Equals(r?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))) return true;
            return IsWarehouseStaffRole(trimmed) && list.Any(IsWarehouseStaffRole);
        }

        private static bool Matches(HashSet<string> roles, string? role) =>
            !string.IsNullOrWhiteSpace(role) && roles.Contains(role.Trim());
    }
}
