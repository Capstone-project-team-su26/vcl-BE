using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Helpers;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>Bảng vận hành: tồn kho, luồng kiện, lô, sự cố, thời gian xử lý. Không có số tiền.</summary>
    public partial class StaffDashboardService
    {
        /// <summary>Nhìn lại bao nhiêu ngày khi tính thời gian xử lý trung bình.</summary>
        private const int ProcessingWindowDays = 30;

        /// <summary>Dòng tồn còn nằm trên kệ — cùng tập với báo cáo lưu kho của InventoryService.</summary>
        private static readonly HashSet<string> InStorageStatuses = new(StringComparer.OrdinalIgnoreCase) { "AVAILABLE", "RESERVED" };

        private static readonly (string Code, string Label)[] ShipmentStatusOrder =
        {
            (ExportShipmentStatuses.Draft, "Nháp"),
            (ExportShipmentStatuses.HandedOver, "Đã bàn giao"),
            (ExportShipmentStatuses.Departed, "Đã khởi hành"),
            (ExportShipmentStatuses.InTransit, "Đang vận chuyển"),
            (ExportShipmentStatuses.Delayed, "Trễ"),
            (ExportShipmentStatuses.OnHold, "Tạm giữ"),
            (ExportShipmentStatuses.CustomsCleared, "Đã thông quan"),
            (ExportShipmentStatuses.ArrivedVn, "Đã về Việt Nam"),
            (ExportShipmentStatuses.ArrivedDestination, "Đã tới kho đích"),
            (ExportShipmentStatuses.Cancelled, "Đã huỷ"),
        };

        private static readonly (string Code, string Label)[] IncidentStatusOrder =
        {
            (IncidentStatuses.Open, "Chờ khách chọn"),
            (IncidentStatuses.CustomerResponded, "Khách đã chọn"),
            (IncidentStatuses.Resolved, "Đã xử lý"),
        };

        private static readonly (string Code, string Label)[] IncidentTypeOrder =
        {
            (IncidentTypes.Damaged, "Hư hỏng"),
            (IncidentTypes.WrongItem, "Sai hàng"),
            (IncidentTypes.MissingItems, "Thiếu hàng"),
            (IncidentTypes.WeightDeviation, "Lệch cân > 10%"),
            (IncidentTypes.Complaint, "Khiếu nại sau giao"),
        };

        public async Task<OperationsDashboardResponse> GetOperationsDashboardAsync()
        {
            var (warehouses, totals) = await BuildStockAsync();
            var (shipmentsByStatus, inProgress) = await BuildShipmentStatusAsync();
            var incidents = (await _unitOfWork.Repository<ParcelIncident>().GetAllAsync()).ToList();

            return new OperationsDashboardResponse
            {
                GeneratedAt = DateTime.UtcNow,
                Warehouses = warehouses,
                StockTotals = totals,
                Flow7Days = await BuildParcelFlowAsync(),
                ShipmentsByStatus = shipmentsByStatus,
                ShipmentsInProgress = inProgress,
                IncidentsByStatus = CountByOrderedKeys(incidents.Select(i => i.Status), IncidentStatusOrder),
                IncidentsByType = BuildIncidentTypes(incidents),
                OpenIncidents = incidents.Count(IsOpenIncident),
                ProcessingWindowDays = ProcessingWindowDays,
                ProcessingTimes = await BuildProcessingTimesAsync(incidents),
            };
        }

        /* ── Tồn kho ─────────────────────────────────────────────────────────────────── */

        /// <summary>
        /// Tồn theo kho, dựng từ dòng tồn (INVENTORIES) → ô kệ (BINS) → kho. Kiện đếm một lần dù
        /// có nhiều dòng tồn; cân lấy cân thực của kiện. "Lấp đầy" = ô đang có hàng / ô đang bật —
        /// dung tích từng ô (MaxVolume) phần lớn để trống nên không dùng làm mẫu số được.
        /// </summary>
        private async Task<(List<WarehouseStockDto> PerWarehouse, WarehouseStockDto Totals)> BuildStockAsync()
        {
            var warehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync()).ToList();
            var bins = (await _unitOfWork.Repository<Bin>().GetAllAsync()).ToList();
            var binWarehouse = bins.ToDictionary(b => b.Id, b => b.WarehouseId);

            var stock = (await _unitOfWork.Repository<Inventory>().GetAllAsync())
                .Where(i => InStorageStatuses.Contains(i.Status ?? string.Empty)
                            && !(Is(i.Status, "AVAILABLE") && i.Quantity <= 0))
                .ToList();

            var parcelIds = stock.Select(i => i.ParcelId).ToHashSet();
            var weights = (await _unitOfWork.Repository<Parcel>().GetAllAsync())
                .Where(p => parcelIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.ActualWeight ?? 0m);

            var stockByWarehouse = stock
                .Where(i => binWarehouse.TryGetValue(i.BinId, out var w) && w.HasValue)
                .GroupBy(i => binWarehouse[i.BinId]!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var rows = new List<WarehouseStockDto>();
            foreach (var warehouse in warehouses
                         .Where(w => w.IsActive || stockByWarehouse.ContainsKey(w.Id))
                         .OrderBy(w => Is(w.WarehouseType, "DESTINATION") ? 1 : 0)
                         .ThenBy(w => w.WarehouseName))
            {
                stockByWarehouse.TryGetValue(warehouse.Id, out var lines);
                lines ??= new List<Inventory>();

                var parcels = lines.Select(i => i.ParcelId).Distinct().ToList();
                var activeBins = bins.Count(b => b.WarehouseId == warehouse.Id && b.IsActive);
                var occupied = lines.Select(i => i.BinId).Distinct().Count();

                rows.Add(new WarehouseStockDto
                {
                    WarehouseId = warehouse.Id,
                    WarehouseName = warehouse.WarehouseName ?? string.Empty,
                    WarehouseCode = warehouse.WarehouseCode ?? string.Empty,
                    WarehouseType = Norm(warehouse.WarehouseType),
                    Region = warehouse.Region?.ToString() ?? string.Empty,
                    StoredParcels = parcels.Count,
                    StoredWeightKg = Math.Round(parcels.Sum(id => weights.TryGetValue(id, out var kg) ? kg : 0m), 2),
                    ActiveBins = activeBins,
                    OccupiedBins = occupied,
                    OccupancyPercent = Ratio(occupied, activeBins),
                });
            }

            var totals = new WarehouseStockDto
            {
                WarehouseName = "Tất cả kho",
                StoredParcels = rows.Sum(r => r.StoredParcels),
                StoredWeightKg = rows.Sum(r => r.StoredWeightKg),
                ActiveBins = rows.Sum(r => r.ActiveBins),
                OccupiedBins = rows.Sum(r => r.OccupiedBins),
            };
            totals.OccupancyPercent = Ratio(totals.OccupiedBins, totals.ActiveBins);

            return (rows, totals);
        }

        /* ── Luồng kiện 7 ngày ───────────────────────────────────────────────────────── */

        /// <summary>
        /// Mỗi cột đọc đúng chứng từ ghi mốc thời gian của chặng đó. Không dùng Parcel.CheckInTime
        /// vì kho VN ghi đè cột này lúc tiếp nhận, và không có cột "giao xong" đáng tin (hãng vận
        /// chuyển báo về không kèm giờ) — nên chặng cuối là "giao cho hãng" (DispatchedAt).
        /// </summary>
        private async Task<List<DailyParcelFlowDto>> BuildParcelFlowAsync()
        {
            var days = LastDays(7);
            var startUtc = WindowStartUtc(7);
            var byDay = days.ToDictionary(d => d, d => new DailyParcelFlowDto { Date = d.ToString("yyyy-MM-dd") });

            DailyParcelFlowDto? Row(DateTime? utc) =>
                utc.HasValue && byDay.TryGetValue(VietnamDate(utc.Value), out var row) ? row : null;

            // ① Xếp kệ ở kho nguồn: nhật ký INBOUND vào một ô thuộc kho không phải kho đích.
            var originBins = await OriginBinIdsAsync();
            var inbound = (await _unitOfWork.Repository<InventoryMovement>().FindAsync(m => m.MovedAt >= startUtc))
                .Where(m => Is(m.MovementType, "INBOUND") && originBins.Contains(m.ToBinId))
                .GroupBy(m => (Day: VietnamDate(m.MovedAt), m.ParcelId))
                .Select(g => g.Key.Day);
            foreach (var day in inbound)
            {
                if (byDay.TryGetValue(day, out var row)) row.OriginInbound++;
            }

            // ② Xuất kho: kiện trong lô quốc tế được bàn giao hôm đó.
            var shipments = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .Where(s => IsInternationalShipment(s) && !Is(s.Status, ExportShipmentStatuses.Cancelled)
                            && (s.HandedOverAt ?? s.ShippedAt) >= startUtc)
                .ToList();
            if (shipments.Count > 0)
            {
                var ids = shipments.Select(s => s.Id).ToHashSet();
                var parcelsPerShipment = (await _unitOfWork.Repository<ShipmentParcel>().GetAllAsync())
                    .Where(sp => ids.Contains(sp.ShipmentId))
                    .GroupBy(sp => sp.ShipmentId)
                    .ToDictionary(g => g.Key, g => g.Select(sp => sp.ParcelId).Distinct().Count());

                foreach (var shipment in shipments)
                {
                    var row = Row(shipment.HandedOverAt ?? shipment.ShippedAt);
                    if (row == null) continue;
                    row.Exported += parcelsPerShipment.TryGetValue(shipment.Id, out var n) ? n : shipment.TotalPackages ?? 0;
                }
            }

            // ③ Về kho VN: biên bản kiểm tra tiếp nhận (một kiện một biên bản).
            foreach (var inspection in await _unitOfWork.Repository<ParcelInspection>().FindAsync(i => i.InspectedAt >= startUtc))
            {
                var row = Row(inspection.InspectedAt);
                if (row != null) row.ArrivedVn++;
            }

            // ④ Giao cho hãng nội địa.
            var dispatched = (await _unitOfWork.Repository<DeliveryRequest>().FindAsync(d => d.DispatchedAt >= startUtc)).ToList();
            if (dispatched.Count > 0)
            {
                var ids = dispatched.Select(d => d.Id).ToHashSet();
                var parcelsPerDelivery = (await _unitOfWork.Repository<DeliveryRequestItem>().GetAllAsync())
                    .Where(i => ids.Contains(i.DeliveryRequestId))
                    .GroupBy(i => i.DeliveryRequestId)
                    .ToDictionary(g => g.Key, g => g.Select(i => i.ParcelId).Distinct().Count());

                foreach (var delivery in dispatched)
                {
                    var row = Row(delivery.DispatchedAt);
                    if (row != null && parcelsPerDelivery.TryGetValue(delivery.Id, out var n)) row.DispatchedDelivery += n;
                }
            }

            return days.Select(d => byDay[d]).ToList();
        }

        private async Task<HashSet<Guid>> OriginBinIdsAsync()
        {
            var originWarehouses = (await _unitOfWork.Repository<Warehouse>().GetAllAsync())
                .Where(w => !Is(w.WarehouseType, "DESTINATION"))
                .Select(w => w.Id)
                .ToHashSet();

            return (await _unitOfWork.Repository<Bin>().GetAllAsync())
                .Where(b => b.WarehouseId.HasValue && originWarehouses.Contains(b.WarehouseId.Value))
                .Select(b => b.Id)
                .ToHashSet();
        }

        /* ── Lô ──────────────────────────────────────────────────────────────────────── */

        /// <summary>Lô quốc tế. Lô đời cũ không ghi loại cũng tính, lô nội địa thì không.</summary>
        private static bool IsInternationalShipment(Shipment shipment) =>
            string.IsNullOrWhiteSpace(shipment.ShipmentType) || Is(shipment.ShipmentType, "INTERNATIONAL");

        private async Task<(List<DashboardCountDto> ByStatus, int InProgress)> BuildShipmentStatusAsync()
        {
            var shipments = (await _unitOfWork.Repository<Shipment>().GetAllAsync())
                .Where(IsInternationalShipment)
                .ToList();

            return (
                CountByOrderedKeys(shipments.Select(s => s.Status), ShipmentStatusOrder),
                shipments.Count(s => ShipmentMilestones.InProgressStatuses.Contains(s.Status ?? string.Empty)));
        }

        /* ── Sự cố ───────────────────────────────────────────────────────────────────── */

        private static bool IsOpenIncident(ParcelIncident incident) =>
            Is(incident.Status, IncidentStatuses.Open) || Is(incident.Status, IncidentStatuses.CustomerResponded);

        private static List<IncidentTypeCountDto> BuildIncidentTypes(List<ParcelIncident> incidents)
        {
            var rows = IncidentTypeOrder
                .Select(t => new IncidentTypeCountDto { Type = t.Code, Label = t.Label })
                .ToList();

            foreach (var group in incidents.GroupBy(i => Norm(i.IncidentType)))
            {
                var row = rows.FirstOrDefault(r => r.Type == group.Key);
                if (row == null)
                {
                    row = new IncidentTypeCountDto { Type = group.Key, Label = group.Key.Length == 0 ? "Chưa phân loại" : group.Key };
                    rows.Add(row);
                }
                row.Total = group.Count();
                row.Open = group.Count(IsOpenIncident);
            }

            return rows;
        }

        /* ── Thời gian xử lý ─────────────────────────────────────────────────────────── */

        /// <summary>
        /// Trung bình từ lúc chứng từ tới tay người duyệt đến lúc có quyết định, trên các chứng từ
        /// được quyết định trong <see cref="ProcessingWindowDays"/> ngày gần nhất.
        /// </summary>
        private async Task<List<ProcessingTimeDto>> BuildProcessingTimesAsync(List<ParcelIncident> incidents)
        {
            var since = DateTime.UtcNow.AddDays(-ProcessingWindowDays);

            var wro = (await _unitOfWork.Repository<WroRequest>().GetAllAsync())
                .Select(w => (w.SubmittedAt, (DateTime?)w.DecidedAt));

            var receiving = (await _unitOfWork.Repository<WarehouseReceivingNote>().GetAllAsync())
                .Select(n => ((DateTime?)n.CreatedAt, n.ReceiveApprovedAt));

            var inbound = (await _unitOfWork.Repository<WarehouseInboundRequest>().GetAllAsync())
                .Select(r => (r.CreatedAt, r.ApprovedAt));

            var delivery = (await _unitOfWork.Repository<DeliveryRequest>().GetAllAsync())
                .Select(d => (d.CreatedAt, d.ApprovedAt));

            var incident = incidents.Select(i => ((DateTime?)i.ReportedAt, i.ResolvedAt));

            return new List<ProcessingTimeDto>
            {
                Average("WRO", "Phiếu xuất kho", "Gửi duyệt → quyết định", wro, since),
                Average("RECEIVING_NOTE", "Phiếu tiếp nhận kho gốc", "Lập phiếu → duyệt nhận hàng", receiving, since),
                Average("INBOUND_VN", "Phiếu nhập kho VN", "Lập phiếu → duyệt", inbound, since),
                Average("DELIVERY", "Yêu cầu giao", "Tạo yêu cầu → duyệt", delivery, since),
                Average("INCIDENT", "Sự cố hàng hoá", "Mở sự cố → xử lý xong", incident, since),
            };
        }

        private static ProcessingTimeDto Average(
            string key, string label, string measure,
            IEnumerable<(DateTime? Start, DateTime? End)> pairs, DateTime sinceUtc)
        {
            var minutes = pairs
                .Where(p => p.Start.HasValue && p.End.HasValue && p.End.Value >= sinceUtc && p.End.Value >= p.Start.Value)
                .Select(p => (decimal)(p.End!.Value - p.Start!.Value).TotalMinutes)
                .ToList();

            return new ProcessingTimeDto
            {
                Key = key,
                Label = label,
                Measure = measure,
                SampleCount = minutes.Count,
                AverageMinutes = minutes.Count == 0 ? null : Math.Round(minutes.Average(), 1),
            };
        }

        /* ── Dùng chung ──────────────────────────────────────────────────────────────── */

        /// <summary>Đếm theo trạng thái: các mã đã biết theo đúng thứ tự nghiệp vụ, mã lạ xếp sau.</summary>
        private static List<DashboardCountDto> CountByOrderedKeys(
            IEnumerable<string?> values, (string Code, string Label)[] order)
        {
            var counts = values
                .GroupBy(Norm)
                .ToDictionary(g => g.Key, g => g.Count());

            var rows = order
                .Select(o => new DashboardCountDto
                {
                    Key = o.Code,
                    Label = o.Label,
                    Count = counts.TryGetValue(o.Code, out var n) ? n : 0,
                })
                .ToList();

            rows.AddRange(counts
                .Where(c => order.All(o => o.Code != c.Key))
                .OrderByDescending(c => c.Value)
                .Select(c => new DashboardCountDto
                {
                    Key = c.Key,
                    Label = c.Key.Length == 0 ? "Chưa ghi trạng thái" : c.Key,
                    Count = c.Value,
                }));

            var percents = SplitPercent(rows.Select(r => r.Count).ToList());
            for (var i = 0; i < rows.Count; i++) rows[i].Percent = percents[i];

            return rows;
        }

        private static decimal Ratio(int part, int whole) =>
            whole <= 0 ? 0m : Math.Round(part * 100m / whole, 1);
    }
}
