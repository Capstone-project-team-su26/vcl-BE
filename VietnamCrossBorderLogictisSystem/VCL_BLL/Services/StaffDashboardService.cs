using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Bảng tổng quan của nhân viên (Sale / vận hành / Admin).
    ///
    /// Tách ba phần ra ba file: phần chung + Sale ở đây, <c>.Operations.cs</c> và <c>.Admin.cs</c>.
    /// Cùng một cách đếm tồn kho được dùng lại cho cả bảng vận hành lẫn bảng Admin để hai màn
    /// không bao giờ nói hai con số khác nhau về cùng một kho.
    /// </summary>
    public partial class StaffDashboardService : IStaffDashboardService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOrderService _orderService;
        private readonly IAdminFinanceService _financeService;

        /// <summary>Số dòng ở hai bảng "mới nhất" của màn Sale.</summary>
        private const int RecentSize = 5;

        /// <summary>Việt Nam không đổi giờ theo mùa: UTC+7 cố định, khỏi phụ thuộc tzdata của máy chủ.</summary>
        private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

        public StaffDashboardService(IUnitOfWork unitOfWork, IOrderService orderService, IAdminFinanceService financeService)
        {
            _unitOfWork = unitOfWork;
            _orderService = orderService;
            _financeService = financeService;
        }

        // =============================================================================
        // SALE
        // =============================================================================

        public async Task<SaleDashboardResponse> GetSaleDashboardAsync()
        {
            var orders = (await _unitOfWork.Repository<Order>().GetAllAsync())
                .Where(IsConsignmentOrder)
                .ToList();
            var requests = (await _unitOfWork.Repository<PurchaseRequest>().GetAllAsync()).ToList();

            /*
             * Báo giá mua hộ đang chờ khách: yêu cầu vẫn có thể nằm ở PENDING_REVIEW trong lúc báo
             * giá đã gửi, nên phải nhìn BÁO GIÁ MỚI NHẤT chứ không chỉ trạng thái yêu cầu.
             */
            var requestIds = requests.Select(r => r.Id).ToHashSet();
            var awaitingQuoteIds = (await _unitOfWork.Repository<PurchaseQuotation>().GetAllAsync())
                .Where(q => requestIds.Contains(q.PurchaseRequestId))
                .GroupBy(q => q.PurchaseRequestId)
                .Select(g => g.OrderByDescending(q => q.CreatedAt ?? DateTime.MinValue).First())
                .Where(q => Is(q.Status, "PENDING_CUSTOMER_CONFIRMATION"))
                .Select(q => q.PurchaseRequestId)
                .ToHashSet();

            var routeCodes = await LoadRouteOriginsAsync();

            var response = new SaleDashboardResponse
            {
                GeneratedAt = DateTime.UtcNow,
                ConsignmentTotal = orders.Count,
                PurchaseTotal = requests.Count,
                TotalOrders = orders.Count + requests.Count,
            };

            await FillExchangeRatesAsync(response);
            response.Routes = BuildRouteShares(orders, requests, routeCodes);
            response.StatusGroups = BuildSaleStatusGroups(orders, requests, awaitingQuoteIds);
            response.Last7Days = BuildLast7Days(orders, requests);
            response.WorkQueue = await BuildSaleWorkQueueAsync(orders, requests, awaitingQuoteIds);
            await FillRecentAsync(response, orders, requests);

            return response;
        }

        private async Task FillExchangeRatesAsync(SaleDashboardResponse response)
        {
            var rates = (await _unitOfWork.Repository<ExchangeRate>().GetAllAsync())
                .Where(r => r.IsActive)
                .OrderBy(r => r.CurrencyCode)
                .ToList();

            response.ExchangeRates = rates
                .Select(r => new DashboardExchangeRateDto
                {
                    CurrencyCode = (r.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant(),
                    CurrencyName = r.CurrencyName ?? string.Empty,
                    RateToVnd = r.RateToVnd,
                    UpdatedAt = r.UpdatedAt ?? r.CreatedAt,
                })
                .ToList();

            response.ExchangeRatesUpdatedAt = rates
                .Select(r => r.UpdatedAt ?? r.CreatedAt)
                .Where(d => d.HasValue)
                .Max();
        }

        /* ── Tỷ lệ tuyến ─────────────────────────────────────────────────────────────── */

        /// <summary>Bốn tuyến chính luôn có mặt (kể cả 0 đơn) để màn hình giữ nguyên bố cục.</summary>
        private static readonly (string Code, string Name, string[] Keys)[] KnownOrigins =
        {
            ("KR", "Hàn Quốc", new[] { "KR", "HQ", "KOR", "KOREA", "HAN QUOC", "HAN" }),
            ("JP", "Nhật Bản", new[] { "JP", "NB", "JPN", "JAPAN", "NHAT BAN", "NHAT" }),
            ("CN", "Trung Quốc", new[] { "CN", "TQ", "CHN", "CHINA", "TRUNG QUOC", "TRUNG" }),
            ("US", "Mỹ", new[] { "US", "USA", "MY", "HOA KY", "AMERICA", "UNITED STATES" }),
        };

        private static List<SaleRouteShareDto> BuildRouteShares(
            List<Order> orders, List<PurchaseRequest> requests, Dictionary<string, string> routeCodes)
        {
            var rows = KnownOrigins.ToDictionary(
                o => o.Code,
                o => new SaleRouteShareDto { CountryCode = o.Code, CountryName = o.Name });

            SaleRouteShareDto RowOf(string? route)
            {
                var code = ResolveOriginCountry(route, routeCodes);
                if (!rows.TryGetValue(code, out var row))
                {
                    row = new SaleRouteShareDto
                    {
                        CountryCode = code,
                        CountryName = code switch
                        {
                            "UNKNOWN" => "Chưa ghi tuyến",
                            "OTHER" => "Tuyến khác",
                            _ => code,
                        },
                    };
                    rows[code] = row;
                }
                return row;
            }

            foreach (var order in orders) RowOf(order.Route).ConsignmentCount++;
            foreach (var request in requests) RowOf(request.Route).PurchaseCount++;

            var list = rows.Values.ToList();
            foreach (var row in list) row.Total = row.ConsignmentCount + row.PurchaseCount;

            var percents = SplitPercent(list.Select(r => r.Total).ToList());
            for (var i = 0; i < list.Count; i++) list[i].Percent = percents[i];

            /* Bốn tuyến chính giữ thứ tự cố định, phần còn lại (nếu có đơn) xếp sau theo số đơn. */
            var knownOrder = KnownOrigins.Select(o => o.Code).ToList();
            return list
                .Where(r => knownOrder.Contains(r.CountryCode) || r.Total > 0)
                .OrderBy(r => knownOrder.Contains(r.CountryCode) ? knownOrder.IndexOf(r.CountryCode) : int.MaxValue)
                .ThenByDescending(r => r.Total)
                .ToList();
        }

        /// <summary>Mã tuyến / tên tuyến trong danh mục → quốc gia xuất xứ.</summary>
        private async Task<Dictionary<string, string>> LoadRouteOriginsAsync()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var route in await _unitOfWork.Repository<ShippingRoute>().GetAllAsync())
            {
                var origin = (route.OriginCountry ?? string.Empty).Trim().ToUpperInvariant();
                if (origin.Length == 0) continue;
                if (!string.IsNullOrWhiteSpace(route.RouteCode)) map[route.RouteCode.Trim()] = origin;
                if (!string.IsNullOrWhiteSpace(route.RouteName)) map[route.RouteName.Trim()] = origin;
            }
            return map;
        }

        /// <summary>
        /// Đơn ghi tuyến bằng chữ tự do ("Hàn Quốc --> Việt Nam", "KR-VN", "CN_VN_ROAD"...).
        /// Tra danh mục tuyến trước, không có thì đọc phần xuất xứ (trước "-->" hoặc "-" / "_").
        /// </summary>
        internal static string ResolveOriginCountry(string? route, IReadOnlyDictionary<string, string> routeCodes)
        {
            if (string.IsNullOrWhiteSpace(route)) return "UNKNOWN";

            var trimmed = route.Trim();
            if (routeCodes.TryGetValue(trimmed, out var fromCatalog))
                return Canonical(fromCatalog);

            string origin;
            var arrow = trimmed.IndexOf("-->", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                origin = trimmed[..arrow];
            }
            else
            {
                var cut = trimmed.IndexOfAny(new[] { '-', '_', '>', '/' });
                origin = cut > 0 ? trimmed[..cut] : trimmed;
            }

            return Canonical(origin);
        }

        private static string Canonical(string value)
        {
            var folded = FoldVietnamese(value);
            foreach (var (code, _, keys) in KnownOrigins)
            {
                if (keys.Contains(folded)) return code;
            }

            return folded.Length == 2 && folded.All(c => c is >= 'A' and <= 'Z') ? folded : "OTHER";
        }

        /// <summary>Bỏ dấu tiếng Việt, viết hoa, gộp khoảng trắng: "Trung quốc " → "TRUNG QUOC".</summary>
        private static string FoldVietnamese(string value)
        {
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch == 'đ' ? 'd' : ch == 'Đ' ? 'D' : ch);
            }

            return string.Join(' ', builder.ToString()
                    .Normalize(NormalizationForm.FormC)
                    .ToUpperInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /* ── Trạng thái ──────────────────────────────────────────────────────────────── */

        private static readonly (string Key, string Label)[] SaleGroups =
        {
            ("PENDING", "Chờ duyệt"),
            ("QUOTED", "Đã báo giá"),
            ("APPROVED", "Đã chốt / đang xử lý"),
            ("REJECTED", "Từ chối / huỷ"),
        };

        /// <summary>
        /// Gom trạng thái gốc về bốn nhóm của biểu đồ. Bản cũ trên trình duyệt dồn MỌI trạng thái
        /// lạ (đang vận chuyển, đã lưu kho, hoàn tất...) vào "Chờ duyệt", nên nhóm này phình to sai.
        /// </summary>
        private static string SaleGroupOfConsignment(string? status)
        {
            var s = Norm(status);
            if (s is "PENDING_REVIEW" or "NEED_MORE_INFO" or "PENDING" or "NEW" or "WAITING_QUOTATION") return "PENDING";
            if (s is "QUOTATION_SENT") return "QUOTED";
            if (IsTerminalRejected(s)) return "REJECTED";
            return "APPROVED";
        }

        private static string SaleGroupOfPurchase(string? status, bool hasQuoteAwaitingCustomer)
        {
            var s = Norm(status);
            if (IsTerminalRejected(s)) return "REJECTED";
            if (s is "QUOTED") return "QUOTED";
            if (s is "NEW" or "PENDING_REVIEW" or "IN_REVIEW" or "NEED_MORE_INFO")
                return hasQuoteAwaitingCustomer ? "QUOTED" : "PENDING";
            return "APPROVED";
        }

        private static bool IsTerminalRejected(string normalizedStatus) =>
            normalizedStatus is "REJECTED" or "QUOTATION_REJECTED" or "CANCELED"
            || normalizedStatus.StartsWith("CANCELLED", StringComparison.Ordinal);

        private List<SaleStatusGroupDto> BuildSaleStatusGroups(
            List<Order> orders, List<PurchaseRequest> requests, HashSet<Guid> awaitingQuoteIds)
        {
            var consignmentLabels = ConsignmentStatusLabels();

            var groups = SaleGroups.ToDictionary(
                g => g.Key,
                g => new SaleStatusGroupDto { Key = g.Key, Label = g.Label });
            var breakdown = SaleGroups.ToDictionary(g => g.Key, _ => new Dictionary<string, DashboardCountDto>());

            void Add(string group, string key, string label)
            {
                if (!breakdown[group].TryGetValue(key, out var row))
                {
                    row = new DashboardCountDto { Key = key, Label = label };
                    breakdown[group][key] = row;
                }
                row.Count++;
            }

            foreach (var order in orders)
            {
                var group = SaleGroupOfConsignment(order.Status);
                groups[group].ConsignmentCount++;
                var status = Norm(order.Status);
                Add(group, "CONSIGNMENT:" + status,
                    "Ký gửi · " + (consignmentLabels.TryGetValue(status, out var l) ? l : status));
            }

            foreach (var request in requests)
            {
                var group = SaleGroupOfPurchase(request.Status, awaitingQuoteIds.Contains(request.Id));
                groups[group].PurchaseCount++;
                var status = Norm(request.Status);
                Add(group, "PURCHASE:" + status, "Mua hộ · " + PurchaseRequestService.GetStatusDisplayName(status));
            }

            var list = SaleGroups.Select(g => groups[g.Key]).ToList();
            foreach (var group in list)
            {
                group.Count = group.ConsignmentCount + group.PurchaseCount;
                group.Statuses = breakdown[group.Key].Values.OrderByDescending(r => r.Count).ToList();
                var inner = SplitPercent(group.Statuses.Select(r => r.Count).ToList());
                for (var i = 0; i < group.Statuses.Count; i++) group.Statuses[i].Percent = inner[i];
            }

            var percents = SplitPercent(list.Select(g => g.Count).ToList());
            for (var i = 0; i < list.Count; i++) list[i].Percent = percents[i];

            return list;
        }

        private static List<DailyOrderCountDto> BuildLast7Days(List<Order> orders, List<PurchaseRequest> requests)
        {
            var days = LastDays(7);
            var byDay = days.ToDictionary(d => d, d => new DailyOrderCountDto { Date = d.ToString("yyyy-MM-dd") });

            foreach (var order in orders)
            {
                if (order.CreatedAt.HasValue && byDay.TryGetValue(VietnamDate(order.CreatedAt.Value), out var row))
                    row.ConsignmentCount++;
            }

            foreach (var request in requests)
            {
                if (request.CreatedAt.HasValue && byDay.TryGetValue(VietnamDate(request.CreatedAt.Value), out var row))
                    row.PurchaseCount++;
            }

            foreach (var row in byDay.Values) row.Total = row.ConsignmentCount + row.PurchaseCount;
            return days.Select(d => byDay[d]).ToList();
        }

        private async Task<SaleWorkQueueDto> BuildSaleWorkQueueAsync(
            List<Order> orders, List<PurchaseRequest> requests, HashSet<Guid> awaitingQuoteIds)
        {
            var toPlace = (await _unitOfWork.Repository<PurchaseOrder>().GetAllAsync())
                .Count(po => PurchaseOrderStatuses.Is(po.Status, PurchaseOrderStatuses.Approved));

            return new SaleWorkQueueDto
            {
                ConsignmentsToReview = orders.Count(o => Is(o.Status, "PENDING_REVIEW")),
                PurchasesToQuote = requests.Count(r =>
                    Norm(r.Status) is "NEW" or "PENDING_REVIEW" or "IN_REVIEW"
                    && !awaitingQuoteIds.Contains(r.Id)),
                QuotationsAwaitingCustomer =
                    orders.Count(o => Is(o.Status, "QUOTATION_SENT"))
                    + requests.Count(r => awaitingQuoteIds.Contains(r.Id) && !IsTerminalRejected(Norm(r.Status))),
                PurchaseOrdersToPlace = toPlace,
            };
        }

        private async Task FillRecentAsync(SaleDashboardResponse response, List<Order> orders, List<PurchaseRequest> requests)
        {
            var customers = (await _unitOfWork.Repository<Customer>().GetAllAsync())
                .ToDictionary(c => c.Id, c => c.FullName ?? string.Empty);
            string CustomerName(Guid? id) => id.HasValue && customers.TryGetValue(id.Value, out var name) ? name : string.Empty;

            var recentRequests = requests
                .OrderByDescending(r => r.CreatedAt ?? DateTime.MinValue)
                .Take(RecentSize)
                .ToList();
            var recentIds = recentRequests.Select(r => r.Id).ToHashSet();
            // Chặng thật cho bảng "mua hộ mới nhất" — nhãn theo status ("Hoàn tất nghiệp vụ") có thể chỉ là tất toán đời cũ.
            var recentStages = await PurchaseRequestStage.ComputeAsync(_unitOfWork, recentRequests);
            var itemsByRequest = (await _unitOfWork.Repository<PurchaseRequestItem>().GetAllAsync())
                .Where(i => recentIds.Contains(i.PurchaseRequestId))
                .GroupBy(i => i.PurchaseRequestId)
                .ToDictionary(g => g.Key, g => g.ToList());

            response.RecentPurchaseRequests = recentRequests
                .Select(r =>
                {
                    itemsByRequest.TryGetValue(r.Id, out var items);
                    return new SaleRecentPurchaseDto
                    {
                        PurchaseRequestId = r.Id,
                        PurchaseCode = r.PurchaseCode ?? string.Empty,
                        CustomerName = CustomerName(r.CustomerId),
                        ReceiverName = r.ReceiverName ?? string.Empty,
                        Route = OrderService.MapRouteToDisplayName(r.Route),
                        ItemCount = items?.Count ?? 0,
                        TotalQuantity = items?.Sum(i => i.Quantity) ?? 0,
                        Status = r.Status ?? string.Empty,
                        StatusText = PurchaseRequestService.GetStatusDisplayName(r.Status),
                        OverallStage = recentStages[r.Id].Stage,
                        OverallStageText = recentStages[r.Id].Text,
                        IsFullyCompleted = recentStages[r.Id].IsFullyCompleted,
                        CreatedAt = r.CreatedAt,
                    };
                })
                .ToList();

            var consignmentLabels = ConsignmentStatusLabels();
            response.RecentConsignments = orders
                .OrderByDescending(o => o.CreatedAt ?? DateTime.MinValue)
                .Take(RecentSize)
                .Select(o => new SaleRecentConsignmentDto
                {
                    OrderId = o.Id,
                    ConsignmentCode = o.ConsignmentCode ?? string.Empty,
                    CustomerName = CustomerName(o.CustomerId),
                    ReceiverName = o.ReceiverName ?? string.Empty,
                    Route = OrderService.MapRouteToDisplayName(o.Route),
                    Status = o.Status ?? string.Empty,
                    StatusText = consignmentLabels.TryGetValue(Norm(o.Status), out var label) ? label : o.Status ?? string.Empty,
                    CreatedAt = o.CreatedAt,
                })
                .ToList();
        }

        // =============================================================================
        // DÙNG CHUNG
        // =============================================================================

        /// <summary>
        /// Đơn ký gửi thật của khách. Bỏ đơn kho sinh ra từ mua hộ (OrderType PURCHASE / có
        /// PurchaseRequestId) — yêu cầu mua hộ đã được đếm riêng, đếm cả hai là trùng.
        /// </summary>
        private static bool IsConsignmentOrder(Order order) =>
            !order.PurchaseRequestId.HasValue && !Is(order.OrderType, "PURCHASE");

        /// <summary>
        /// Mã đơn có trong DB nhưng danh mục <c>GET /api/orders/consignments/statuses</c> chưa liệt kê.
        /// Nhãn chép từ bảng nhãn chung của FE (orderStatus.js) để hai phía nói cùng một chữ.
        /// </summary>
        private static readonly Dictionary<string, string> ExtraConsignmentLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NEED_MORE_INFO"] = "Cần bổ sung thông tin",
            ["WAITING_DEPOSIT"] = "Chờ đặt cọc",
            ["ARRIVED_VN"] = "Đã về Việt Nam",
            ["ARRIVED_DESTINATION"] = "Đã tới kho VN",
            ["WAITING_PAYMENT"] = "Chờ tất toán",
            ["PAID"] = "Đã tất toán",
            ["STORED_AT_VN"] = "Đang lưu kho VN",
            ["DELIVERING"] = "Đang giao hàng",
            ["COMPLETED"] = "Hoàn tất",
            ["CANCELLED"] = "Đã hủy",
        };

        private Dictionary<string, string> ConsignmentStatusLabels()
        {
            var map = new Dictionary<string, string>(ExtraConsignmentLabels, StringComparer.OrdinalIgnoreCase);
            foreach (var status in _orderService.GetConsignmentStatuses())
            {
                if (!string.IsNullOrWhiteSpace(status.Code) && !string.IsNullOrWhiteSpace(status.Label))
                    map[status.Code.Trim().ToUpperInvariant()] = status.Label;
            }
            return map;
        }

        private static DateTime VietnamNow => DateTime.UtcNow + VietnamOffset;

        private static DateTime VietnamDate(DateTime utc) => (utc + VietnamOffset).Date;

        /// <summary>N ngày gần nhất theo giờ Việt Nam, tính cả hôm nay, cũ → mới.</summary>
        private static List<DateTime> LastDays(int count)
        {
            var today = VietnamNow.Date;
            return Enumerable.Range(0, count).Select(i => today.AddDays(i - count + 1)).ToList();
        }

        /// <summary>Mốc UTC của 00:00 (giờ VN) ngày đầu tiên trong <see cref="LastDays"/>.</summary>
        private static DateTime WindowStartUtc(int days) => LastDays(days)[0] - VietnamOffset;

        /// <summary>N tháng gần nhất theo giờ Việt Nam, kể cả tháng này, cũ → mới.</summary>
        private static List<DateTime> LastMonths(int count)
        {
            var now = VietnamNow;
            var current = new DateTime(now.Year, now.Month, 1);
            return Enumerable.Range(0, count).Select(i => current.AddMonths(i - count + 1)).ToList();
        }

        private static DateTime VietnamMonth(DateTime utc)
        {
            var vn = utc + VietnamOffset;
            return new DateTime(vn.Year, vn.Month, 1);
        }

        /// <summary>
        /// Chia phần trăm theo phần dư lớn nhất: làm tròn xuống rồi cộng 1 cho các phần dư lớn nhất
        /// cho tới khi đủ 100. Tránh cảnh 33 + 33 + 33 = 99 hay 34 + 33 + 34 = 101 trên màn hình.
        /// </summary>
        internal static int[] SplitPercent(IReadOnlyList<int> counts)
        {
            var result = new int[counts.Count];
            var total = counts.Sum();
            if (total <= 0) return result;

            var raw = counts.Select(c => c * 100m / total).ToArray();
            for (var i = 0; i < raw.Length; i++) result[i] = (int)Math.Floor(raw[i]);

            var left = 100 - result.Sum();
            foreach (var i in Enumerable.Range(0, raw.Length)
                         .OrderByDescending(i => raw[i] - result[i])
                         .ThenByDescending(i => counts[i])
                         .Take(left))
            {
                result[i]++;
            }

            return result;
        }

        private static string Norm(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static bool Is(string? actual, string expected) =>
            string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
