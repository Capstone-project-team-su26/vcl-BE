using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    // =====================================================================================
    // Bảng tổng quan của NHÂN VIÊN — ba vai trò, ba endpoint, một nguyên tắc:
    // backend tự đếm/cộng từ bảng gốc, màn hình chỉ vẽ.
    //
    // Trước đây màn Sale kéo danh sách đơn về trình duyệt rồi tự cộng (lại chỉ được trang đầu
    // của danh sách có phân trang), tỷ giá và đơn ký gửi là dữ liệu mẫu, biểu đồ 7 ngày là
    // đường vẽ cứng. Màn vận hành và màn quản trị thì không có số liệu nào ngoài vài thẻ đếm.
    //
    // Mọi mốc "ngày" / "tháng" tính theo giờ Việt Nam (UTC+7); dữ liệu gốc lưu UTC.
    // =====================================================================================

    /// <summary>Một nhóm đếm dùng chung: mã, nhãn tiếng Việt, số lượng, tỷ lệ phần trăm.</summary>
    public class DashboardCountDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }

        /// <summary>Phần trăm làm tròn sao cho cả nhóm cộng lại đúng 100 (0 khi tổng bằng 0).</summary>
        public int Percent { get; set; }
    }

    // ───────────────────────────── SALE ─────────────────────────────

    /// <summary>GET /api/staff/dashboard — màn "Tổng quan" của Sale.</summary>
    public class SaleDashboardResponse
    {
        public DateTime GeneratedAt { get; set; }

        /// <summary>Tỷ giá công ty đang bật (bảng EXCHANGE_RATES), sắp theo mã tiền.</summary>
        public List<DashboardExchangeRateDto> ExchangeRates { get; set; } = new();

        /// <summary>Lần sửa tỷ giá gần nhất (UpdatedAt, không có thì CreatedAt). Null nếu chưa có tỷ giá.</summary>
        public DateTime? ExchangeRatesUpdatedAt { get; set; }

        /// <summary>Tổng số đơn = đơn ký gửi + yêu cầu mua hộ (không đếm đơn kho sinh ra từ mua hộ).</summary>
        public int TotalOrders { get; set; }
        public int ConsignmentTotal { get; set; }
        public int PurchaseTotal { get; set; }

        /// <summary>Phân bổ đơn theo quốc gia xuất xứ của tuyến, gộp hai luồng. Luôn có KR, JP, CN, US.</summary>
        public List<SaleRouteShareDto> Routes { get; set; } = new();

        /// <summary>Bốn nhóm trạng thái cho biểu đồ tròn: PENDING, QUOTED, APPROVED, REJECTED.</summary>
        public List<SaleStatusGroupDto> StatusGroups { get; set; } = new();

        /// <summary>Số đơn tạo mới theo ngày, 7 ngày gần nhất tính cả hôm nay, cũ → mới, đủ 7 ngày.</summary>
        public List<DailyOrderCountDto> Last7Days { get; set; } = new();

        public SaleWorkQueueDto WorkQueue { get; set; } = new();

        /// <summary>5 yêu cầu mua hộ mới nhất.</summary>
        public List<SaleRecentPurchaseDto> RecentPurchaseRequests { get; set; } = new();

        /// <summary>5 đơn ký gửi mới nhất.</summary>
        public List<SaleRecentConsignmentDto> RecentConsignments { get; set; } = new();
    }

    public class DashboardExchangeRateDto
    {
        public string CurrencyCode { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public decimal RateToVnd { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SaleRouteShareDto
    {
        /// <summary>KR, JP, CN, US, mã ISO khác, OTHER (không nhận ra) hoặc UNKNOWN (đơn không ghi tuyến).</summary>
        public string CountryCode { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public int ConsignmentCount { get; set; }
        public int PurchaseCount { get; set; }
        public int Total { get; set; }
        public int Percent { get; set; }
    }

    public class SaleStatusGroupDto
    {
        /// <summary>PENDING · QUOTED · APPROVED · REJECTED.</summary>
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public int ConsignmentCount { get; set; }
        public int PurchaseCount { get; set; }
        public int Percent { get; set; }

        /// <summary>Trạng thái gốc nằm trong nhóm (để giải thích con số khi rê chuột).</summary>
        public List<DashboardCountDto> Statuses { get; set; } = new();
    }

    public class DailyOrderCountDto
    {
        /// <summary>yyyy-MM-dd theo giờ Việt Nam.</summary>
        public string Date { get; set; } = string.Empty;
        public int ConsignmentCount { get; set; }
        public int PurchaseCount { get; set; }
        public int Total { get; set; }
    }

    /// <summary>Việc đang chờ phía Sale.</summary>
    public class SaleWorkQueueDto
    {
        /// <summary>Đơn ký gửi PENDING_REVIEW — chờ Sale xem và báo giá.</summary>
        public int ConsignmentsToReview { get; set; }

        /// <summary>Yêu cầu mua hộ chờ xem xét (NEW / PENDING_REVIEW / IN_REVIEW) chưa có báo giá đang chờ khách.</summary>
        public int PurchasesToQuote { get; set; }

        /// <summary>Báo giá đã gửi, khách chưa trả lời: đơn QUOTATION_SENT + báo giá mua hộ PENDING_CUSTOMER_CONFIRMATION.</summary>
        public int QuotationsAwaitingCustomer { get; set; }

        /// <summary>Đơn mua NCC đã được Admin duyệt (APPROVED), chờ Sale đặt hàng với NCC.</summary>
        public int PurchaseOrdersToPlace { get; set; }
    }

    public class SaleRecentPurchaseDto
    {
        public Guid PurchaseRequestId { get; set; }
        public string PurchaseCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public int TotalQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;

        /// <summary>Chặng thật đầu–cuối (xem PurchaseRequestListItemDto.OverallStage).</summary>
        public string OverallStage { get; set; } = string.Empty;
        public string OverallStageText { get; set; } = string.Empty;
        public bool IsFullyCompleted { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class SaleRecentConsignmentDto
    {
        public Guid OrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
    }

    // ───────────────────────────── VẬN HÀNH (OPERATIONS MANAGER) ─────────────────────────────

    /// <summary>GET /api/operations/dashboard — thống kê vận hành kho và luồng hàng. Không có số tiền.</summary>
    public class OperationsDashboardResponse
    {
        public DateTime GeneratedAt { get; set; }

        /// <summary>Tồn kho theo từng kho đang hoạt động.</summary>
        public List<WarehouseStockDto> Warehouses { get; set; } = new();

        /// <summary>Cộng dồn các kho.</summary>
        public WarehouseStockDto StockTotals { get; set; } = new();

        /// <summary>Luồng kiện theo ngày, 7 ngày gần nhất tính cả hôm nay, cũ → mới.</summary>
        public List<DailyParcelFlowDto> Flow7Days { get; set; } = new();

        /// <summary>Lô vận chuyển quốc tế theo trạng thái (bỏ lô nháp / đã huỷ khỏi "đang chạy").</summary>
        public List<DashboardCountDto> ShipmentsByStatus { get; set; } = new();
        public int ShipmentsInProgress { get; set; }

        public List<DashboardCountDto> IncidentsByStatus { get; set; } = new();
        public List<IncidentTypeCountDto> IncidentsByType { get; set; } = new();

        /// <summary>Sự cố chưa đóng (OPEN + CUSTOMER_RESPONDED).</summary>
        public int OpenIncidents { get; set; }

        /// <summary>Số ngày nhìn lại khi tính thời gian xử lý trung bình.</summary>
        public int ProcessingWindowDays { get; set; }

        public List<ProcessingTimeDto> ProcessingTimes { get; set; } = new();
    }

    public class WarehouseStockDto
    {
        public Guid? WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string WarehouseCode { get; set; } = string.Empty;

        /// <summary>ORIGIN (kho nguồn) · DESTINATION (kho Việt Nam).</summary>
        public string WarehouseType { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        /// <summary>Kiện đang nằm trên ô kệ: dòng tồn AVAILABLE / RESERVED còn số lượng.</summary>
        public int StoredParcels { get; set; }

        /// <summary>Tổng cân thực (ActualWeight) của các kiện đó, kg.</summary>
        public decimal StoredWeightKg { get; set; }

        /// <summary>Ô kệ đang bật của kho.</summary>
        public int ActiveBins { get; set; }

        /// <summary>Ô kệ đang có ít nhất một kiện.</summary>
        public int OccupiedBins { get; set; }

        /// <summary>Tỷ lệ ô đang có hàng / ô đang bật, %, một chữ số thập phân.</summary>
        public decimal OccupancyPercent { get; set; }
    }

    public class DailyParcelFlowDto
    {
        /// <summary>yyyy-MM-dd theo giờ Việt Nam.</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>Kiện được xếp lên kệ ở kho nguồn (nhật ký INBOUND vào ô của kho ORIGIN).</summary>
        public int OriginInbound { get; set; }

        /// <summary>Kiện rời kho nguồn: kiện trong lô quốc tế bàn giao hôm đó (HandedOverAt).</summary>
        public int Exported { get; set; }

        /// <summary>Kiện kho Việt Nam tiếp nhận (biên bản kiểm tra InspectedAt).</summary>
        public int ArrivedVn { get; set; }

        /// <summary>Kiện giao cho hãng vận chuyển nội địa (yêu cầu giao DispatchedAt).</summary>
        public int DispatchedDelivery { get; set; }
    }

    public class IncidentTypeCountDto
    {
        public string Type { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Open { get; set; }
        public int Total { get; set; }
    }

    public class ProcessingTimeDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// <summary>Mô tả hai mốc dùng để đo, ví dụ "Gửi duyệt → quyết định".</summary>
        public string Measure { get; set; } = string.Empty;

        /// <summary>Số PHÚT trung bình, một chữ số thập phân (màn hình tự đổi ra giờ/ngày). Null khi chưa có chứng từ nào được quyết định trong kỳ.</summary>
        public decimal? AverageMinutes { get; set; }

        /// <summary>Số chứng từ được tính.</summary>
        public int SampleCount { get; set; }
    }

    // ───────────────────────────── ADMIN ─────────────────────────────

    /// <summary>GET /api/admin/dashboard — bức tranh tổng thể cho Admin, gồm cả tiền.</summary>
    public class AdminDashboardResponse
    {
        public DateTime GeneratedAt { get; set; }
        public AdminUserStatsDto Users { get; set; } = new();
        public AdminOrderStatsDto Orders { get; set; } = new();
        public AdminFinanceStatsDto Finance { get; set; } = new();

        /// <summary>Tồn kho cộng dồn mọi kho (cùng cách tính với bảng vận hành).</summary>
        public WarehouseStockDto Stock { get; set; } = new();
        public List<WarehouseStockDto> Warehouses { get; set; } = new();

        public int OpenIncidents { get; set; }
        public int ShipmentsInProgress { get; set; }
    }

    public class AdminUserStatsDto
    {
        public int Total { get; set; }

        /// <summary>Status = Active.</summary>
        public int Active { get; set; }
        public int Locked { get; set; }
        public int PendingVerification { get; set; }

        /// <summary>Tài khoản tạo trong 30 ngày gần nhất.</summary>
        public int NewLast30Days { get; set; }

        /// <summary>Theo nhóm vai trò (gộp các biến thể chính tả: "Warehouse Staff VN" → kho).</summary>
        public List<AdminRoleCountDto> ByRole { get; set; } = new();
    }

    public class AdminRoleCountDto
    {
        /// <summary>ADMIN · SALE · OPERATIONS_MANAGER · WAREHOUSE_STAFF · DELIVERY · CUSTOMER · OTHER.</summary>
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public int Active { get; set; }
    }

    public class AdminOrderStatsDto
    {
        public int ConsignmentTotal { get; set; }
        public int PurchaseTotal { get; set; }

        /// <summary>Chưa kết thúc (không phải hoàn tất / huỷ / từ chối).</summary>
        public int ConsignmentInProgress { get; set; }
        public int PurchaseInProgress { get; set; }
        public int ConsignmentCompleted { get; set; }
        public int PurchaseCompleted { get; set; }

        /// <summary>Theo trạng thái gốc, nhiều → ít.</summary>
        public List<DashboardCountDto> ConsignmentByStatus { get; set; } = new();
        public List<DashboardCountDto> PurchaseByStatus { get; set; } = new();

        /// <summary>Đơn tạo mới theo tháng, 6 tháng gần nhất kể cả tháng rỗng, cũ → mới.</summary>
        public List<MonthlyOrderCountDto> Last6Months { get; set; } = new();
    }

    public class MonthlyOrderCountDto
    {
        /// <summary>yyyy-MM theo giờ Việt Nam.</summary>
        public string Month { get; set; } = string.Empty;
        public int ConsignmentCount { get; set; }
        public int PurchaseCount { get; set; }
    }

    public class AdminFinanceStatsDto
    {
        /// <summary>
        /// Ba số dưới đây lấy NGUYÊN từ tổng quan dòng tiền (GET /api/admin/finance/summary)
        /// để trang tổng quan và trang Dòng tiền không bao giờ lệch nhau.
        /// </summary>
        public decimal TotalBillAmount { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal Remaining { get; set; }
        public int BilledOrderCount { get; set; }
        public int PaidOrderCount { get; set; }
        public int PartialOrderCount { get; set; }
        public int UnpaidOrderCount { get; set; }

        /// <summary>Tiền đã thực thu, cộng thẳng từ bảng khoản thu: PAYMENTS (PAID/SUCCESS) + PURCHASE_PAYMENTS (PAID, bỏ dòng hoàn).</summary>
        public decimal CollectedTotal { get; set; }
        public decimal ConsignmentCollected { get; set; }
        public decimal PurchaseCollected { get; set; }

        /// <summary>Tiền mua hộ công ty đang phải hoàn khách (dòng hoàn PENDING).</summary>
        public decimal PurchaseRefundPending { get; set; }

        /// <summary>Khoản chờ Admin đối soát tay (cùng nguồn với danh sách chờ duyệt ở trang Dòng tiền).</summary>
        public int PendingApprovalCount { get; set; }

        /// <summary>Tiền thực thu theo tháng (theo ngày thu PaidAt), 6 tháng gần nhất, cũ → mới.</summary>
        public List<MonthlyCollectedDto> CollectedLast6Months { get; set; } = new();
    }

    public class MonthlyCollectedDto
    {
        public string Month { get; set; } = string.Empty;
        public decimal Consignment { get; set; }
        public decimal Purchase { get; set; }
        public decimal Total { get; set; }
    }
}
