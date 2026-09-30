using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Helpers;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>Bảng quản trị: người dùng, đơn, dòng tiền, tồn kho. Chỉ Admin gọi được (xem controller).</summary>
    public partial class StaffDashboardService
    {
        private static readonly (string Key, string Label)[] RoleGroups =
        {
            ("ADMIN", "Quản trị"),
            ("SALE", "Sale"),
            ("OPERATIONS_MANAGER", "Quản lý vận hành"),
            ("WAREHOUSE_STAFF", "Nhân viên kho"),
            ("DELIVERY", "Giao hàng"),
            ("CUSTOMER", "Khách hàng"),
            ("OTHER", "Khác"),
        };

        public async Task<AdminDashboardResponse> GetAdminDashboardAsync()
        {
            var (warehouses, totals) = await BuildStockAsync();
            var (_, shipmentsInProgress) = await BuildShipmentStatusAsync();
            var openIncidents = (await _unitOfWork.Repository<ParcelIncident>().GetAllAsync()).Count(IsOpenIncident);

            return new AdminDashboardResponse
            {
                GeneratedAt = DateTime.UtcNow,
                Users = await BuildUserStatsAsync(),
                Orders = await BuildOrderStatsAsync(),
                Finance = await BuildFinanceStatsAsync(),
                Stock = totals,
                Warehouses = warehouses,
                OpenIncidents = openIncidents,
                ShipmentsInProgress = shipmentsInProgress,
            };
        }

        /* ── Người dùng ──────────────────────────────────────────────────────────────── */

        private async Task<AdminUserStatsDto> BuildUserStatsAsync()
        {
            var users = (await _unitOfWork.Repository<User>().GetAllAsync()).ToList();
            var since = DateTime.UtcNow.AddDays(-30);

            var byRole = RoleGroups.ToDictionary(r => r.Key, r => new AdminRoleCountDto { Key = r.Key, Label = r.Label });
            foreach (var user in users)
            {
                var row = byRole[RoleGroupOf(user.Role)];
                row.Count++;
                if (IsActiveUser(user)) row.Active++;
            }

            return new AdminUserStatsDto
            {
                Total = users.Count,
                Active = users.Count(IsActiveUser),
                Locked = users.Count(u => Is(u.Status, "Locked")),
                PendingVerification = users.Count(u => Is(u.Status, "PendingVerification")),
                NewLast30Days = users.Count(u => u.CreatedAt >= since),
                ByRole = RoleGroups.Select(r => byRole[r.Key]).Where(r => r.Count > 0 || r.Key != "OTHER").ToList(),
            };
        }

        private static bool IsActiveUser(User user) => Is(user.Status, "Active");

        /// <summary>
        /// Vai trò trong DB có nhiều biến thể chính tả ("Warehouse Staff VN", "WarehouseTQ",
        /// "Operations"...). Gộp về nhóm để biểu đồ không vỡ thành chục cột một người.
        /// </summary>
        private static string RoleGroupOf(string? role)
        {
            var r = Norm(role).Replace(" ", string.Empty).Replace("_", string.Empty);
            if (r is "ADMIN" or "ADMINISTRATOR") return "ADMIN";
            if (r is "SALE" or "SALES") return "SALE";
            if (r.StartsWith("OPERATION", StringComparison.Ordinal) || r == "MANAGER") return "OPERATIONS_MANAGER";
            if (r.StartsWith("WAREHOUSE", StringComparison.Ordinal) || r == "STAFF") return "WAREHOUSE_STAFF";
            if (r == "DELIVERY") return "DELIVERY";
            if (r == "CUSTOMER") return "CUSTOMER";
            return "OTHER";
        }

        /* ── Đơn ─────────────────────────────────────────────────────────────────────── */

        private async Task<AdminOrderStatsDto> BuildOrderStatsAsync()
        {
            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync()).Where(IsConsignmentOrder).ToList();
            var requests = (await _unitOfWork.Repository<PurchaseRequest>().GetAllAsync()).ToList();
            var consignmentLabels = ConsignmentStatusLabels();

            var months = LastMonths(6);
            var byMonth = months.ToDictionary(m => m, m => new MonthlyOrderCountDto { Month = m.ToString("yyyy-MM") });
            foreach (var order in orders)
            {
                if (order.CreatedAt.HasValue && byMonth.TryGetValue(VietnamMonth(order.CreatedAt.Value), out var row))
                    row.ConsignmentCount++;
            }
            foreach (var request in requests)
            {
                if (request.CreatedAt.HasValue && byMonth.TryGetValue(VietnamMonth(request.CreatedAt.Value), out var row))
                    row.PurchaseCount++;
            }

            return new AdminOrderStatsDto
            {
                ConsignmentTotal = orders.Count,
                PurchaseTotal = requests.Count,
                ConsignmentCompleted = orders.Count(o => Is(o.Status, "COMPLETED")),
                PurchaseCompleted = requests.Count(r => Is(r.Status, "COMPLETED")),
                ConsignmentInProgress = orders.Count(o => !IsFinished(o.Status)),
                PurchaseInProgress = requests.Count(r => !IsFinished(r.Status)),
                ConsignmentByStatus = CountByStatus(orders.Select(o => o.Status),
                    s => consignmentLabels.TryGetValue(s, out var label) ? label : s),
                PurchaseByStatus = CountByStatus(requests.Select(r => r.Status),
                    s => PurchaseRequestService.GetStatusDisplayName(s)),
                Last6Months = months.Select(m => byMonth[m]).ToList(),
            };
        }

        private static bool IsFinished(string? status)
        {
            var s = Norm(status);
            return s == "COMPLETED" || IsTerminalRejected(s);
        }

        private static List<DashboardCountDto> CountByStatus(IEnumerable<string?> statuses, Func<string, string> label)
        {
            var rows = statuses
                .GroupBy(Norm)
                .Select(g => new DashboardCountDto
                {
                    Key = g.Key,
                    Label = g.Key.Length == 0 ? "Chưa ghi trạng thái" : label(g.Key),
                    Count = g.Count(),
                })
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Key)
                .ToList();

            var percents = SplitPercent(rows.Select(r => r.Count).ToList());
            for (var i = 0; i < rows.Count; i++) rows[i].Percent = percents[i];
            return rows;
        }

        /* ── Tiền ────────────────────────────────────────────────────────────────────── */

        /// <summary>
        /// Hai lớp số:
        /// <list type="number">
        /// <item>Phải thu / đã thu / còn nợ theo đơn — gọi thẳng tổng quan dòng tiền của
        /// AdminFinanceService, nên luôn trùng với trang Dòng tiền.</item>
        /// <item>Tiền thực thu — cộng thẳng từng khoản thu đã vào tài khoản, theo ngày thu. Mỗi
        /// dòng tiền được đếm đúng một lần dù thuộc đơn ký gửi hay mua hộ.</item>
        /// </list>
        /// </summary>
        private async Task<AdminFinanceStatsDto> BuildFinanceStatsAsync()
        {
            var summary = await _financeService.GetFinanceSummaryAsync(null, null);
            var pending = await _financeService.GetPendingManualTransactionsAsync(
                new AdminPendingTransactionQueryDto { PageNumber = 1, PageSize = 1 });

            var consignmentPaid = (await _unitOfWork.Repository<Payment>().GetAllAsync())
                .Where(p => Norm(p.PaymentStatus) is "PAID" or "SUCCESS")
                .ToList();

            var purchasePayments = (await _unitOfWork.Repository<PurchasePayment>().GetAllAsync()).ToList();
            var purchasePaid = purchasePayments
                .Where(p => !PurchasePaymentTypes.IsRefund(p) && PurchasePaymentTypes.IsPaid(p))
                .ToList();

            var months = LastMonths(6);
            var byMonth = months.ToDictionary(m => m, m => new MonthlyCollectedDto { Month = m.ToString("yyyy-MM") });
            foreach (var payment in consignmentPaid)
            {
                var at = payment.PaidAt ?? payment.CreatedAt;
                if (at.HasValue && byMonth.TryGetValue(VietnamMonth(at.Value), out var row)) row.Consignment += payment.Amount ?? 0m;
            }
            foreach (var payment in purchasePaid)
            {
                var at = payment.PaidAt ?? payment.CreatedAt;
                if (at.HasValue && byMonth.TryGetValue(VietnamMonth(at.Value), out var row)) row.Purchase += payment.Amount;
            }
            foreach (var row in byMonth.Values) row.Total = row.Consignment + row.Purchase;

            var consignmentCollected = consignmentPaid.Sum(p => p.Amount ?? 0m);
            var purchaseCollected = purchasePaid.Sum(p => p.Amount);

            return new AdminFinanceStatsDto
            {
                TotalBillAmount = summary.TotalBillAmount,
                TotalPaid = summary.TotalPaid,
                Remaining = summary.Remaining,
                BilledOrderCount = summary.OrderCount,
                PaidOrderCount = summary.PaidCount,
                PartialOrderCount = summary.PartialCount,
                UnpaidOrderCount = summary.UnpaidCount,
                ConsignmentCollected = consignmentCollected,
                PurchaseCollected = purchaseCollected,
                CollectedTotal = consignmentCollected + purchaseCollected,
                PurchaseRefundPending = purchasePayments
                    .Where(p => PurchasePaymentTypes.IsRefund(p) && Is(p.Status, PurchaseRefundStatuses.Pending))
                    .Sum(p => p.Amount),
                PendingApprovalCount = pending.TotalCount,
                CollectedLast6Months = months.Select(m => byMonth[m]).ToList(),
            };
        }
    }
}
