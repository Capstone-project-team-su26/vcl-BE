using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCL_BLL.DTOs;
using VCL_BLL.Exceptions;
using VCL_BLL.Helpers;
using VCL_BLL.Interface;
using VCL_DLL.Interface;
using VCL_DLL.Models;

namespace VCL_BLL.Services
{
    /// <summary>
    /// Hành trình hàng nhìn từ hai phía.
    /// <para>
    /// Khách chỉ quan tâm "đơn của tôi đang ở đâu": gộp trạng thái kiện, phiếu xuất và lô về một
    /// thang chặng, và chỉ cho thấy lời nhắn dành cho khách — ghi chú nội bộ của Sale, tờ khai,
    /// biên bản không bao giờ đi ra phía khách.
    /// </para>
    /// <para>
    /// Sale cần biết "lô nào mình phải cập nhật": lô đã rời kho mà lâu không có mốc mới, hoặc đã
    /// quá ngày dự kiến mà chưa về, được đẩy lên đầu hàng đợi.
    /// </para>
    /// </summary>
    public class ShipmentTrackingService : IShipmentTrackingService
    {
        /// <summary>Quá số ngày này không có mốc mới thì coi là cần cập nhật.</summary>
        private const int StaleAfterDays = 3;

        private static readonly HashSet<string> RetiredParcelStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "SPLIT", "MERGED" };

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAttachmentService _attachmentService;

        public ShipmentTrackingService(IUnitOfWork unitOfWork, IAttachmentService attachmentService)
        {
            _unitOfWork = unitOfWork;
            _attachmentService = attachmentService;
        }

        // ── Khách ──────────────────────────────────────────────────────────────────────────

        public async Task<OrderTrackingDto> GetOrderTrackingAsync(Guid userId, string role, Guid orderId)
        {
            var data = await TrackingData.LoadAsync(_unitOfWork);
            var order = data.Orders.TryGetValue(orderId, out var o) ? o
                : throw new NotFoundException($"Không tìm thấy đơn {orderId}.");

            EnsureCanViewOrder(data, order, userId, role);
            return BuildOrderTracking(data, order);
        }

        public async Task<PagedTrackedOrdersDto> GetTrackedOrdersAsync(Guid userId, string role, TrackedOrderQuery query)
        {
            query ??= new TrackedOrderQuery();
            var data = await TrackingData.LoadAsync(_unitOfWork);
            var isStaff = StaffRoleHelper.IsInternalStaff(role);

            IEnumerable<Order> orders = data.Orders.Values;
            if (!isStaff)
            {
                var customerIds = data.Customers.Values.Where(c => c.UserId == userId).Select(c => c.Id).ToHashSet();
                orders = orders.Where(ord => ord.CustomerId.HasValue && customerIds.Contains(ord.CustomerId.Value));
            }

            // Chỉ những đơn đã có hàng ở kho — đơn còn đang báo giá thì chưa có gì để theo dõi.
            orders = orders.Where(ord => data.ParcelsOf(ord.Id).Count > 0);

            var wantedStages = string.IsNullOrWhiteSpace(query.Stage)
                ? null
                : query.Stage.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var items = new List<TrackedOrderDto>();
            foreach (var order in orders)
            {
                var tracking = BuildOrderTracking(data, order);
                var includeFinished = query.IncludeFinished || query.IncludeArrived;
                if (!includeFinished
                    && (CustomerTrackingStages.IsFinished(tracking.CurrentStage)
                        || string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (wantedStages != null && !wantedStages.Contains(tracking.CurrentStage ?? string.Empty)) continue;

                var last = tracking.Events.LastOrDefault();
                items.Add(new TrackedOrderDto
                {
                    OrderId = order.Id,
                    ConsignmentCode = order.ConsignmentCode,
                    CustomerName = data.CustomerNameOf(order),
                    CurrentStage = tracking.CurrentStage,
                    CurrentStageText = tracking.CurrentStageText,
                    ParcelCount = tracking.Parcels.Count,
                    ShipmentCodes = tracking.Shipments.Select(s => s.ShipmentCode).Where(c => c != null).Select(c => c!).ToList(),
                    LastEventAt = last?.Time,
                    LastEventTitle = last?.Title,
                    EstimatedArrivalDate = tracking.EstimatedArrivalDate
                });
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var keyword = query.Search.Trim();
                items = items.Where(i =>
                        (i.ConsignmentCode ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || (i.CustomerName ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || i.ShipmentCodes.Any(c => c.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 100);

            return new PagedTrackedOrdersDto
            {
                Items = items
                    .OrderByDescending(i => i.LastEventAt ?? DateTime.MinValue)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList(),
                TotalCount = items.Count,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(items.Count / (double)pageSize)
            };
        }

        private static void EnsureCanViewOrder(TrackingData data, Order order, Guid userId, string role)
        {
            if (StaffRoleHelper.IsInternalStaff(role)) return;

            var customer = order.CustomerId.HasValue && data.Customers.TryGetValue(order.CustomerId.Value, out var c) ? c : null;
            if (customer?.UserId != userId)
                throw new UnauthorizedAccessException("Bạn chỉ theo dõi được đơn của mình.");
        }

        private static OrderTrackingDto BuildOrderTracking(TrackingData data, Order order)
        {
            var parcels = data.ParcelsOf(order.Id);
            var dto = new OrderTrackingDto
            {
                OrderId = order.Id,
                ConsignmentCode = order.ConsignmentCode,
                OrderStatus = order.Status,
                ExportHold = order.ExportHold,
                ExportHoldReason = order.ExportHoldReason
            };

            var shipments = new Dictionary<Guid, (Shipment Shipment, List<string> Codes)>();
            string? originName = null;
            string? destinationName = null;
            Guid? routeId = null;

            foreach (var parcel in parcels)
            {
                var (stage, shipment, wro) = data.StageOf(parcel);

                if (shipment != null)
                {
                    if (!shipments.TryGetValue(shipment.Id, out var entry))
                        shipments[shipment.Id] = entry = (shipment, new List<string>());
                    entry.Codes.Add(parcel.PackageCode);
                }

                // Tuyến gắn ở phiếu xuất kho; các kiện cùng đơn thường cùng tuyến, lấy tuyến
                // đầu tiên gặp được là đủ để vẽ lộ trình.
                routeId ??= wro?.ShippingRouteId;

                originName ??= data.WarehouseName(wro?.OriginWarehouseId ?? shipment?.OriginWarehouseId)
                               ?? data.WarehouseName(data.WarehouseOfBin(parcel.BinId));
                destinationName ??= data.WarehouseName(wro?.DestinationWarehouseId ?? shipment?.DestinationWarehouseId);

                dto.Parcels.Add(new OrderTrackingParcelDto
                {
                    ParcelId = parcel.Id,
                    PackageCode = parcel.PackageCode,
                    ProductName = parcel.OrderItemId.HasValue && data.OrderItems.TryGetValue(parcel.OrderItemId.Value, out var item)
                        ? item.ProductName
                        : null,
                    Stage = stage,
                    PackageStatus = parcel.PackageStatus,
                    ShipmentCode = shipment?.ShipmentCode
                });
            }

            dto.OriginWarehouseName = originName;
            dto.DestinationWarehouseName = destinationName;

            foreach (var p in dto.Parcels)
                p.StageText = CustomerTrackingStages.Text(p.Stage, originName);

            if (dto.Parcels.Count == 0)
            {
                dto.CurrentStage = CustomerTrackingStages.NotReceived;
            }
            else
            {
                // Đơn đi chậm theo kiện chậm nhất: khách chỉ nhận đủ hàng khi kiện cuối cùng về.
                var slowest = dto.Parcels.OrderBy(p => CustomerTrackingStages.Rank(p.Stage)).First();
                dto.CurrentStage = slowest.Stage;
                dto.IsSplitAcrossStages = dto.Parcels.Select(p => CustomerTrackingStages.Rank(p.Stage)).Distinct().Count() > 1;
            }
            dto.CurrentStageText = CustomerTrackingStages.Text(dto.CurrentStage, originName);

            foreach (var (shipment, codes) in shipments.Values)
            {
                dto.Shipments.Add(new OrderTrackingShipmentDto
                {
                    ShipmentCode = shipment.ShipmentCode,
                    Status = shipment.Status,
                    StatusText = ShipmentMilestones.CustomerTitle(shipment.Status),
                    CarrierName = data.CarrierName(shipment.CarrierId),
                    CarrierTrackingCode = shipment.CarrierTrackingCode,
                    HandedOverAt = shipment.HandedOverAt ?? shipment.ShippedAt,
                    EstimatedArrivalDate = data.EstimatedArrivalOf(shipment),
                    PackageCodes = codes
                });
            }

            dto.EstimatedArrivalDate = dto.Shipments
                .Where(s => s.EstimatedArrivalDate.HasValue)
                .Select(s => s.EstimatedArrivalDate)
                .DefaultIfEmpty(null)
                .Max();

            dto.Events = BuildCustomerEvents(data, order, parcels, shipments.Values.Select(v => v.Shipment).ToList(), originName);
            dto.Route = BuildRoute(data, order, dto, routeId);
            dto.Money = BuildMoney(data, order);
            return dto;
        }

        /// <summary>Chữ tiếng Việt của phương thức vận chuyển — màn hình không phải tự dịch mã.</summary>
        private static string TransportModeText(string? mode) => mode?.Trim().ToUpperInvariant() switch
        {
            "AIR" => "Đường hàng không",
            "SEA" => "Đường biển",
            "ROAD" => "Đường bộ",
            "RAIL" => "Đường sắt",
            _ => "Chưa xác định phương thức",
        };

        /// <summary>
        /// Lộ trình đơn: năm chặng từ kho nguồn tới tận nhà khách, kèm chặng nào đã qua và qua lúc nào.
        ///
        /// `Reached` so theo THANG CHẶNG (Rank) chứ không theo việc có mốc lưu hay không: dữ liệu mốc
        /// có thể thiếu một nhịp (kho quên bấm), nhưng hàng đã về Việt Nam thì chắc chắn đã qua kho
        /// nguồn. `ReachedAt` thì ngược lại, chỉ điền khi có mốc thật — không bao giờ đoán thời gian.
        /// </summary>
        private static OrderTrackingRouteDto BuildRoute(
            TrackingData data, Order order, OrderTrackingDto dto, Guid? routeId)
        {
            var route = data.RouteById(routeId);
            var carrierName = dto.Shipments.Select(sh => sh.CarrierName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            var currentRank = CustomerTrackingStages.Rank(dto.CurrentStage);

            // Mốc sớm nhất của mỗi chặng — events đã sắp cũ → mới nên First() là lần đầu qua chặng.
            DateTime? FirstTimeOf(params string[] stages)
            {
                var wanted = new HashSet<string>(stages, StringComparer.OrdinalIgnoreCase);
                foreach (var ev in dto.Events)
                {
                    if (!string.IsNullOrWhiteSpace(ev.Stage) && wanted.Contains(ev.Stage!))
                        return ev.Time;
                }
                return null;
            }

            OrderTrackingRouteStopDto Stop(string key, string title, string? subtitle, string minStage, params string[] stages)
                => new()
                {
                    Key = key,
                    Title = title,
                    Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle,
                    Reached = currentRank >= CustomerTrackingStages.Rank(minStage),
                    ReachedAt = FirstTimeOf(stages),
                };

            var transitSubtitle = route != null
                ? string.Join(" → ", new[] { route.OriginCountry, route.DestinationCountry }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                : null;

            return new OrderTrackingRouteDto
            {
                RouteCode = route?.RouteCode,
                RouteName = route?.RouteName,
                TransportMode = route?.TransportMode,
                TransportModeText = TransportModeText(route?.TransportMode),
                OriginCountry = route?.OriginCountry,
                DestinationCountry = route?.DestinationCountry,
                EstimatedTransitDays = route?.EstimatedTransitDays,
                Stops = new List<OrderTrackingRouteStopDto>
                {
                    Stop("ORIGIN", "Kho nguồn nhận hàng", dto.OriginWarehouseName,
                        CustomerTrackingStages.AtOriginWarehouse,
                        CustomerTrackingStages.AtOriginWarehouse, CustomerTrackingStages.PreparingExport),

                    Stop("EXPORTED", "Xuất kho, bàn giao hãng vận chuyển", carrierName,
                        ExportShipmentStatuses.HandedOver,
                        ExportShipmentStatuses.HandedOver, ExportShipmentStatuses.Departed),

                    Stop("TRANSIT", "Vận chuyển quốc tế",
                        string.IsNullOrWhiteSpace(transitSubtitle)
                            ? TransportModeText(route?.TransportMode)
                            : $"{TransportModeText(route?.TransportMode)} · {transitSubtitle}",
                        ExportShipmentStatuses.InTransit,
                        ExportShipmentStatuses.InTransit, ExportShipmentStatuses.Delayed,
                        ExportShipmentStatuses.OnHold, ExportShipmentStatuses.CustomsCleared,
                        ExportShipmentStatuses.ArrivedVn),

                    Stop("VN_WAREHOUSE", "Kho Việt Nam nhận và kiểm hàng", dto.DestinationWarehouseName,
                        ExportShipmentStatuses.ArrivedDestination,
                        ExportShipmentStatuses.ArrivedDestination, CustomerTrackingStages.ReceivedAtVn,
                        CustomerTrackingStages.Quarantined, CustomerTrackingStages.StoredAtVn),

                    Stop("DELIVERY", "Giao tới địa chỉ của bạn", order.ReceiverAddress,
                        CustomerTrackingStages.OutForDelivery,
                        CustomerTrackingStages.OutForDelivery, CustomerTrackingStages.DeliveryFailed,
                        CustomerTrackingStages.Delivered),
                }
            };
        }

        /// <summary>
        /// Tiền của đơn đọc từ báo giá đã lưu + các giao dịch đã PAID. Trả null khi đơn chưa có
        /// báo giá: chưa có giá thì bày ô tiền toàn số 0 chỉ làm khách tưởng đơn miễn phí.
        /// </summary>
        private static OrderTrackingMoneyDto? BuildMoney(TrackingData data, Order order)
        {
            if (!data.QuotationByOrder.TryGetValue(order.Id, out var quotation)) return null;

            var total = (quotation.EstimatedFreightCharge ?? 0m)
                        + (quotation.ServiceFee ?? 0m)
                        + (quotation.TaxAndDuty ?? 0m);
            var paid = data.PaidAmountByOrder.TryGetValue(order.Id, out var amount) ? amount : 0m;

            return new OrderTrackingMoneyDto
            {
                QuotationId = quotation.Id,
                QuotationStatus = quotation.Status,
                EstimatedFreightCharge = quotation.EstimatedFreightCharge ?? 0m,
                ServiceFee = quotation.ServiceFee ?? 0m,
                TaxAndDuty = quotation.TaxAndDuty ?? 0m,
                TotalEstimatedCost = total,
                PaidAmount = paid,
                OutstandingAmount = Math.Max(0m, total - paid),
                PaymentStatus = paid <= 0m ? "UNPAID" : paid + 0.01m >= total ? "PAID" : "PARTIALLY_PAID",
            };
        }

        /// <summary>
        /// Dòng thời gian khách xem: kho nhận hàng → chuẩn bị xuất → các mốc của lô. Mỗi mốc lô
        /// chỉ hiện lời nhắn cho khách (hoặc câu mặc định), không bao giờ hiện ghi chú nội bộ.
        /// </summary>
        private static List<OrderTrackingEventDto> BuildCustomerEvents(
            TrackingData data, Order order, List<Parcel> parcels, List<Shipment> shipments, string? originName)
        {
            var events = new List<OrderTrackingEventDto>();

            var firstCheckIn = parcels.Where(p => p.CheckInTime.HasValue).Select(p => p.CheckInTime!.Value).DefaultIfEmpty().Min();
            if (firstCheckIn != default)
            {
                events.Add(new OrderTrackingEventDto
                {
                    Time = firstCheckIn,
                    Stage = CustomerTrackingStages.AtOriginWarehouse,
                    Title = "Kho đã nhận hàng",
                    Message = string.IsNullOrWhiteSpace(originName)
                        ? $"Kho đã nhận hàng của đơn {order.ConsignmentCode}."
                        : $"{originName} đã nhận hàng của đơn {order.ConsignmentCode}."
                });
            }

            var approvedAt = parcels
                .Select(p => data.StageOf(p).Wro)
                .Where(w => w?.DecidedAt != null && !string.Equals(w.Status, WroStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
                .Select(w => w!.DecidedAt!.Value)
                .DefaultIfEmpty()
                .Min();
            if (approvedAt != default)
            {
                events.Add(new OrderTrackingEventDto
                {
                    Time = approvedAt,
                    Stage = CustomerTrackingStages.PreparingExport,
                    Title = "Đang chuẩn bị xuất kho",
                    Message = $"Hàng của đơn {order.ConsignmentCode} đã được xếp lên chuyến và đang chuẩn bị xuất kho."
                });
            }

            // Kho đích nhận lô ghi một dòng RECEIVED_AT_DESTINATION cho CẢ lô, còn AddVietnamEvents
            // ghi mốc "Kho Việt Nam đã nhận hàng" theo biên bản kiểm kiện của CHÍNH đơn này — hai
            // dòng cùng nghĩa, khách từng thấy mốc này hai lần. Luật: có biên bản kiểm của đơn thì
            // chỉ giữ mốc theo đơn (đúng giờ kiểm kiện của đơn); đơn không có biên bản (dữ liệu cũ)
            // thì giữ dòng của lô (chỉ một dòng dù đơn tách nhiều lô) và quy về chặng RECEIVED_AT_VN.
            var orderParcelIds = parcels.Select(p => p.Id).ToHashSet();
            var hasOrderInspection = data.Inspections.Any(i => orderParcelIds.Contains(i.ParcelId) && i.InspectedAt.HasValue);
            var receivedRowShown = false;

            foreach (var shipment in shipments)
            {
                foreach (var t in data.TrackingsOf(shipment.Id).OrderBy(x => x.TrackingTime))
                {
                    if (!t.TrackingTime.HasValue) continue;
                    var isReceivedRow = string.Equals(t.TrackingStatus?.Trim(), ShipmentMilestones.ReceivedAtDestination,
                        StringComparison.OrdinalIgnoreCase);
                    if (isReceivedRow && (hasOrderInspection || receivedRowShown)) continue;
                    if (isReceivedRow) receivedRowShown = true;

                    events.Add(new OrderTrackingEventDto
                    {
                        Time = t.TrackingTime.Value,
                        Stage = isReceivedRow ? CustomerTrackingStages.ReceivedAtVn : t.TrackingStatus,
                        Title = ShipmentMilestones.CustomerTitle(t.TrackingStatus),
                        Message = string.IsNullOrWhiteSpace(t.CustomerMessage)
                            ? ShipmentMilestones.CustomerDefaultMessage(t.TrackingStatus, order.ConsignmentCode)
                            : t.CustomerMessage,
                        Location = t.Location,
                        ShipmentCode = shipment.ShipmentCode
                    });
                }
            }

            AddVietnamEvents(data, order, parcels, events);
            return events.OrderBy(e => e.Time).ToList();
        }

        /// <summary>
        /// Các mốc ở Việt Nam: kho kiểm hàng, sự cố, tất toán, giao nội địa, giao xong. Chỉ mốc khách
        /// cần biết, không có tên người thao tác hay ghi chú nội bộ.
        /// </summary>
        private static void AddVietnamEvents(TrackingData data, Order order, List<Parcel> parcels, List<OrderTrackingEventDto> events)
        {
            var parcelIds = parcels.Select(p => p.Id).ToHashSet();
            var code = order.ConsignmentCode;

            var inspectedAt = data.Inspections.Where(i => parcelIds.Contains(i.ParcelId) && i.InspectedAt.HasValue)
                .Select(i => i.InspectedAt!.Value).DefaultIfEmpty().Min();
            if (inspectedAt != default)
            {
                events.Add(new OrderTrackingEventDto
                {
                    Time = inspectedAt,
                    Stage = CustomerTrackingStages.ReceivedAtVn,
                    Title = "Kho Việt Nam đã nhận hàng",
                    Message = $"Kho Việt Nam đã nhận và cân đo hàng của đơn {code}. Bạn chọn giao ngay hay gửi lại kho cho từng kiện, rồi thanh toán đợt cuối."
                });
            }

            foreach (var incident in data.Incidents.Where(i => i.OrderId == order.Id))
            {
                if (string.Equals(incident.IncidentType, IncidentTypes.WeightDeviation, StringComparison.OrdinalIgnoreCase)) continue;
                var packageCode = parcels.FirstOrDefault(p => p.Id == incident.ParcelId)?.PackageCode;

                events.Add(new OrderTrackingEventDto
                {
                    Time = incident.ReportedAt,
                    Stage = CustomerTrackingStages.Quarantined,
                    Title = string.Equals(incident.IncidentType, IncidentTypes.Complaint, StringComparison.OrdinalIgnoreCase)
                        ? "Đã ghi nhận khiếu nại"
                        : $"Kiện {packageCode} gặp sự cố: {IncidentTypes.Describe(incident.IncidentType)}",
                    Message = incident.Description
                });

                if (incident.ResolvedAt.HasValue)
                {
                    events.Add(new OrderTrackingEventDto
                    {
                        Time = incident.ResolvedAt.Value,
                        Stage = CustomerTrackingStages.ReceivedAtVn,
                        Title = $"Sự cố {incident.IncidentCode} đã được xử lý",
                        Message = incident.Resolution switch
                        {
                            IncidentResolutions.Dispose => $"Kiện {packageCode} được huỷ theo thoả thuận.",
                            IncidentResolutions.Compensate => $"Kiện {packageCode}: bồi thường {incident.CompensationAmount:#,##0}đ.",
                            _ => $"Kiện {packageCode} tiếp tục được xử lý bình thường."
                        }
                    });
                }
            }

            var finalPaidAt = data.FinalPaidAtByOrder.TryGetValue(order.Id, out var paidAt) ? paidAt : null;
            if (finalPaidAt.HasValue)
            {
                events.Add(new OrderTrackingEventDto
                {
                    Time = finalPaidAt.Value,
                    Stage = CustomerTrackingStages.ReceivedAtVn,
                    Title = "Đã thanh toán đợt cuối",
                    Message = $"Đơn {code} đã tất toán. Hàng sẽ được lưu kho hoặc giao theo lựa chọn của bạn."
                });
            }

            foreach (var delivery in data.DeliveriesOf(order.Id))
            {
                if (delivery.DispatchedAt.HasValue)
                {
                    events.Add(new OrderTrackingEventDto
                    {
                        Time = delivery.DispatchedAt.Value,
                        Stage = CustomerTrackingStages.OutForDelivery,
                        Title = "Đã đặt giao hàng",
                        Message = string.IsNullOrWhiteSpace(delivery.CarrierTrackingCode)
                            ? $"Hàng đã được giao cho đơn vị vận chuyển nội địa ({delivery.DeliveryCode})."
                            : $"Hàng đã được giao cho đơn vị vận chuyển nội địa, mã vận đơn {delivery.CarrierTrackingCode}."
                    });
                }

                if (delivery.ReturnedAt.HasValue)
                {
                    events.Add(new OrderTrackingEventDto
                    {
                        Time = delivery.ReturnedAt.Value,
                        Stage = CustomerTrackingStages.DeliveryFailed,
                        Title = "Giao hàng chưa thành công",
                        Message = "Hàng đã về lại kho. Bạn có thể đặt giao lại."
                    });
                }
            }

            if (order.DeliveredAt.HasValue)
            {
                events.Add(new OrderTrackingEventDto
                {
                    Time = order.DeliveredAt.Value,
                    Stage = CustomerTrackingStages.Delivered,
                    Title = "Đã giao hàng",
                    Message = $"Hàng của đơn {code} đã được giao. Vui lòng xác nhận đã nhận hàng hoặc khiếu nại nếu có vấn đề."
                });
            }

            if (order.CompletedAt.HasValue)
            {
                events.Add(new OrderTrackingEventDto
                {
                    Time = order.CompletedAt.Value,
                    Stage = CustomerTrackingStages.Delivered,
                    Title = "Đơn hoàn thành",
                    Message = $"Đơn {code} đã hoàn thành."
                });
            }
        }

        // ── Sale ───────────────────────────────────────────────────────────────────────────

        public async Task<ShipmentTimelineDto> GetShipmentTimelineAsync(Guid userId, string role, Guid shipmentId)
        {
            EnsureStaff(role);
            var data = await TrackingData.LoadAsync(_unitOfWork);
            var shipment = data.Shipments.TryGetValue(shipmentId, out var s) ? s
                : throw new NotFoundException($"Không tìm thấy lô vận chuyển {shipmentId}.");

            var documents = await _attachmentService.DocumentTypesOfAsync(AttachmentEntityTypes.Shipment, shipment.Id);
            var summary = BuildSummary(data, shipment, documents);

            var timeline = new ShipmentTimelineDto();
            foreach (var prop in typeof(ShipmentTrackingSummaryDto).GetProperties())
                prop.SetValue(timeline, prop.GetValue(summary));

            timeline.Events = data.TrackingsOf(shipment.Id)
                .Select(t => new ShipmentTrackingEventDto
                {
                    Id = t.Id,
                    Time = t.TrackingTime,
                    Status = t.TrackingStatus,
                    StatusText = ShipmentMilestones.StaffText(t.TrackingStatus),
                    Location = t.Location,
                    Note = t.Note,
                    CustomerMessage = t.CustomerMessage,
                    CreatedByName = data.UserName(t.CreatedById)
                })
                .ToList();

            timeline.Orders = data.ParcelsOfShipment(shipment)
                .Where(p => p.ConsignmentId.HasValue)
                .GroupBy(p => p.ConsignmentId!.Value)
                .Select(g =>
                {
                    var order = data.Orders.TryGetValue(g.Key, out var ord) ? ord : null;
                    return new ShipmentOrderRefDto
                    {
                        OrderId = g.Key,
                        ConsignmentCode = order?.ConsignmentCode,
                        CustomerName = order != null ? data.CustomerNameOf(order) : null,
                        ParcelCount = g.Count()
                    };
                })
                .OrderBy(x => x.ConsignmentCode)
                .ToList();

            timeline.Attachments = await _attachmentService.ListAsync(userId, role, AttachmentEntityTypes.Shipment, shipment.Id);
            return timeline;
        }

        public async Task<List<ShipmentTrackingSummaryDto>> GetInTransitQueueAsync(
            Guid userId, string role, Guid? originWarehouseId, bool attentionOnly)
        {
            EnsureStaff(role);
            var data = await TrackingData.LoadAsync(_unitOfWork);

            var attachments = (await _unitOfWork.Repository<Attachment>().GetAllAsync())
                .Where(a => string.Equals(a.EntityType, AttachmentEntityTypes.Shipment, StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => a.EntityId)
                .ToDictionary(g => g.Key, g => g.Select(a => a.DocumentType?.Trim().ToUpperInvariant() ?? string.Empty)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));

            var result = data.Shipments.Values
                .Where(sh => ShipmentMilestones.InProgressStatuses.Contains(sh.Status?.Trim() ?? string.Empty))
                .Where(sh => !originWarehouseId.HasValue || sh.OriginWarehouseId == originWarehouseId.Value)
                .Select(sh => BuildSummary(data, sh,
                    attachments.TryGetValue(sh.Id, out var docs) ? docs : new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                .Where(x => !attentionOnly || x.IsOverdue || x.NeedsUpdate
                            || string.Equals(x.Status, ExportShipmentStatuses.OnHold, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(x.Status, ExportShipmentStatuses.Delayed, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.IsOverdue)
                .ThenByDescending(x => x.NeedsUpdate)
                .ThenBy(x => x.LastUpdateAt ?? DateTime.MinValue)
                .ToList();

            return result;
        }

        private static ShipmentTrackingSummaryDto BuildSummary(TrackingData data, Shipment shipment, HashSet<string> documents)
        {
            var now = DateTime.UtcNow;
            var trackings = data.TrackingsOf(shipment.Id);
            var lastUpdate = trackings.Where(t => t.TrackingTime.HasValue).Select(t => t.TrackingTime).DefaultIfEmpty(null).Max()
                             ?? shipment.HandedOverAt ?? shipment.ShippedAt;
            var eta = data.EstimatedArrivalOf(shipment);
            var parcels = data.ParcelsOfShipment(shipment);
            var status = shipment.Status?.Trim().ToUpperInvariant() ?? string.Empty;
            var inProgress = ShipmentMilestones.InProgressStatuses.Contains(status);
            var arrivedVn = status is ExportShipmentStatuses.ArrivedVn or ExportShipmentStatuses.ArrivedDestination;
            var days = lastUpdate.HasValue ? (int)Math.Floor((now - lastUpdate.Value).TotalDays) : 0;

            return new ShipmentTrackingSummaryDto
            {
                ShipmentId = shipment.Id,
                ShipmentCode = shipment.ShipmentCode,
                Status = shipment.Status,
                StatusText = ShipmentMilestones.StaffText(shipment.Status),
                OriginWarehouseName = data.WarehouseName(shipment.OriginWarehouseId),
                DestinationWarehouseName = data.WarehouseName(shipment.DestinationWarehouseId),
                CarrierName = data.CarrierName(shipment.CarrierId),
                CarrierTrackingCode = shipment.CarrierTrackingCode,
                HandedOverAt = shipment.HandedOverAt ?? shipment.ShippedAt,
                EstimatedArrivalDate = eta,
                LastUpdateAt = lastUpdate,
                DaysSinceLastUpdate = days,
                IsOverdue = inProgress && !arrivedVn && eta.HasValue && eta.Value < now,
                NeedsUpdate = inProgress && days >= StaleAfterDays,
                OrderCount = parcels.Where(p => p.ConsignmentId.HasValue).Select(p => p.ConsignmentId).Distinct().Count(),
                ParcelCount = parcels.Count,
                NextMilestones = ShipmentMilestones.NextFrom(shipment.Status)
                    .Select(next =>
                    {
                        var requirements = ShipmentMilestones.RequirementsOf(next).ToList();
                        return new NextMilestoneDto
                        {
                            Status = next,
                            Text = ShipmentMilestones.StaffText(next),
                            Requirements = requirements,
                            MissingDocuments = requirements
                                .Where(r => r != ShipmentMilestones.RequiresNote && !documents.Contains(r))
                                .ToList()
                        };
                    })
                    .ToList()
            };
        }

        private static void EnsureStaff(string role)
        {
            if (!StaffRoleHelper.IsInternalStaff(role))
                throw new UnauthorizedAccessException("Chỉ nhân viên mới xem được hành trình lô vận chuyển.");
        }

        // ── Dữ liệu nạp một lần cho mỗi lượt đọc ───────────────────────────────────────────

        private sealed class TrackingData
        {
            public Dictionary<Guid, Order> Orders { get; private init; } = new();
            public Dictionary<Guid, Customer> Customers { get; private init; } = new();
            public Dictionary<Guid, OrderItem> OrderItems { get; private init; } = new();
            public Dictionary<Guid, Shipment> Shipments { get; private init; } = new();
            public List<ParcelInspection> Inspections { get; private set; } = new();
            public List<ParcelIncident> Incidents { get; private set; } = new();
            public Dictionary<Guid, DateTime?> FinalPaidAtByOrder { get; private set; } = new();

            /// <summary>Báo giá đang có hiệu lực của từng đơn (ưu tiên bản khách đã chấp nhận).</summary>
            public Dictionary<Guid, Quotation> QuotationByOrder { get; private set; } = new();

            /// <summary>Tổng tiền ĐÃ THU của từng đơn (chỉ giao dịch PAID).</summary>
            public Dictionary<Guid, decimal> PaidAmountByOrder { get; private set; } = new();
            private List<DeliveryRequest> _deliveries = new();

            public IEnumerable<DeliveryRequest> DeliveriesOf(Guid orderId) => _deliveries.Where(d => d.OrderId == orderId);

            private Dictionary<Guid, List<Parcel>> _parcelsByOrder = new();
            private Dictionary<Guid, Parcel> _parcels = new();
            private Dictionary<Guid, WroRequest> _wros = new();
            private Dictionary<Guid, WroParcel> _rowByParcel = new();
            private Dictionary<Guid, Guid> _shipmentByWro = new();
            private Dictionary<Guid, Guid> _legacyShipmentByParcel = new();
            private Dictionary<Guid, List<Tracking>> _trackings = new();
            private Dictionary<Guid, string> _warehouses = new();
            private Dictionary<Guid, string> _carriers = new();
            private Dictionary<Guid, ShippingRoute> _routes = new();
            private Dictionary<Guid, string> _users = new();
            private Dictionary<Guid, Guid?> _binWarehouse = new();

            public static async Task<TrackingData> LoadAsync(IUnitOfWork uow)
            {
                var parcels = (await uow.Repository<Parcel>().GetAllAsync())
                    .Where(p => !RetiredParcelStatuses.Contains(p.PackageStatus?.Trim() ?? string.Empty))
                    .ToList();

                var shipments = (await uow.Repository<Shipment>().GetAllAsync())
                    .Where(s => !string.Equals(s.Status?.Trim(), ExportShipmentStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(s => s.Id);

                var data = new TrackingData
                {
                    Orders = (await uow.Repository<Order>().GetAllAsync()).ToDictionary(o => o.Id),
                    Customers = (await uow.Repository<Customer>().GetAllAsync()).ToDictionary(c => c.Id),
                    OrderItems = (await uow.Repository<OrderItem>().GetAllAsync()).ToDictionary(i => i.Id),
                    Shipments = shipments
                };

                data._parcels = parcels.ToDictionary(p => p.Id);
                data._parcelsByOrder = parcels.Where(p => p.ConsignmentId.HasValue)
                    .GroupBy(p => p.ConsignmentId!.Value)
                    .ToDictionary(g => g.Key, g => g.OrderBy(p => p.PackageCode).ToList());

                data._wros = (await uow.Repository<WroRequest>().GetAllAsync()).ToDictionary(w => w.Id);

                // Kiện có thể từng nằm trong nhiều phiếu (bị bỏ rồi lập lại): lấy dòng còn hiệu lực,
                // không có thì lấy dòng mới nhất để vẫn biết lịch sử.
                data._rowByParcel = (await uow.Repository<WroParcel>().GetAllAsync())
                    .GroupBy(r => r.ParcelId)
                    .ToDictionary(g => g.Key, g => g
                        .OrderBy(r => string.Equals(r.Status, WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .ThenByDescending(r => r.CreatedAt)
                        .First());

                foreach (var link in await uow.Repository<ShipmentWroRequest>().GetAllAsync())
                {
                    if (shipments.ContainsKey(link.ShipmentId))
                        data._shipmentByWro[link.WroRequestId] = link.ShipmentId;
                }

                foreach (var sp in await uow.Repository<ShipmentParcel>().GetAllAsync())
                {
                    if (shipments.ContainsKey(sp.ShipmentId))
                        data._legacyShipmentByParcel[sp.ParcelId] = sp.ShipmentId;
                }

                data._trackings = (await uow.Repository<Tracking>().GetAllAsync())
                    .Where(t => t.ShipmentId.HasValue)
                    .GroupBy(t => t.ShipmentId!.Value)
                    .ToDictionary(g => g.Key, g => g.OrderBy(t => t.TrackingTime ?? DateTime.MinValue).ToList());

                data._warehouses = (await uow.Repository<Warehouse>().GetAllAsync()).ToDictionary(w => w.Id, w => w.WarehouseName);

                data.Inspections = (await uow.Repository<ParcelInspection>().GetAllAsync()).ToList();
                data.Incidents = (await uow.Repository<ParcelIncident>().GetAllAsync()).ToList();
                data._deliveries = (await uow.Repository<DeliveryRequest>().GetAllAsync()).ToList();

                var quotationOrder = (await uow.Repository<Quotation>().GetAllAsync())
                    .Where(q => q.OrderId.HasValue).ToDictionary(q => q.Id, q => q.OrderId!.Value);
                var invoiceOrder = (await uow.Repository<Invoice>().GetAllAsync())
                    .Where(i => i.QuotationId.HasValue && quotationOrder.ContainsKey(i.QuotationId.Value))
                    .ToDictionary(i => i.Id, i => quotationOrder[i.QuotationId!.Value]);
                var payments = (await uow.Repository<Payment>().GetAllAsync()).ToList();
                data.FinalPaidAtByOrder = payments
                    .Where(p => p.InvoiceId.HasValue && invoiceOrder.ContainsKey(p.InvoiceId.Value)
                                && string.Equals(p.InstallmentType, "FINAL_PAYMENT", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(p => invoiceOrder[p.InvoiceId!.Value])
                    .ToDictionary(g => g.Key, g => (DateTime?)(g.Max(p => p.PaidAt ?? p.CreatedAt) ?? DateTime.UtcNow));

                // Tiền khách đã trả cho đơn: mọi đợt (cọc, cuối) đã ở trạng thái PAID.
                data.PaidAmountByOrder = payments
                    .Where(p => p.InvoiceId.HasValue && invoiceOrder.ContainsKey(p.InvoiceId.Value)
                                && string.Equals(p.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(p => invoiceOrder[p.InvoiceId!.Value])
                    .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount ?? 0m));

                // Báo giá của đơn: bản khách đã chấp nhận là bản có hiệu lực; chưa chấp nhận bản
                // nào thì lấy bản mới nhất (khách đang xem giá đó), bỏ bản đã từ chối.
                data.QuotationByOrder = (await uow.Repository<Quotation>().GetAllAsync())
                    .Where(q => q.OrderId.HasValue
                                && !string.Equals(q.Status?.Trim(), "REJECTED", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(q => q.OrderId!.Value)
                    .ToDictionary(g => g.Key, g => g
                        .OrderByDescending(q => string.Equals(q.Status?.Trim(), "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(q => q.CreatedAt ?? DateTime.MinValue)
                        .First());
                data._carriers = (await uow.Repository<Carrier>().GetAllAsync()).ToDictionary(c => c.Id, c => c.CarrierName);
                data._routes = (await uow.Repository<ShippingRoute>().GetAllAsync()).ToDictionary(r => r.Id);
                data._users = (await uow.Repository<User>().GetAllAsync()).ToDictionary(u => u.Id, u => u.FullName);

                var shelves = (await uow.Repository<Shelf>().GetAllAsync()).ToDictionary(s => s.Id);
                var zones = (await uow.Repository<WarehouseZone>().GetAllAsync()).ToDictionary(z => z.Id);
                foreach (var bin in await uow.Repository<Bin>().GetAllAsync())
                {
                    Guid? wh = bin.WarehouseId;
                    if (bin.ShelfId.HasValue && shelves.TryGetValue(bin.ShelfId.Value, out var shelf)
                        && shelf.ZoneId.HasValue && zones.TryGetValue(shelf.ZoneId.Value, out var zone))
                        wh = zone.WarehouseId ?? wh;
                    data._binWarehouse[bin.Id] = wh;
                }

                return data;
            }

            /// <summary>Tuyến đã cấu hình theo id — null khi phiếu chưa gán tuyến.</summary>
            public ShippingRoute? RouteById(Guid? routeId) =>
                routeId.HasValue && _routes.TryGetValue(routeId.Value, out var route) ? route : null;

            public List<Parcel> ParcelsOf(Guid orderId) =>
                _parcelsByOrder.TryGetValue(orderId, out var list) ? list : new List<Parcel>();

            public List<Tracking> TrackingsOf(Guid shipmentId) =>
                _trackings.TryGetValue(shipmentId, out var list) ? list : new List<Tracking>();

            /// <summary>Kiện của lô: theo phiếu trong lô, cộng kiện gắn thẳng vào lô (dữ liệu cũ).</summary>
            public List<Parcel> ParcelsOfShipment(Shipment shipment)
            {
                var wroIds = _shipmentByWro.Where(kv => kv.Value == shipment.Id).Select(kv => kv.Key).ToHashSet();
                var ids = _rowByParcel.Values
                    .Where(r => wroIds.Contains(r.WroRequestId)
                                && !string.Equals(r.Status, WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.ParcelId)
                    .ToHashSet();
                ids.UnionWith(_legacyShipmentByParcel.Where(kv => kv.Value == shipment.Id).Select(kv => kv.Key));

                return ids.Where(_parcels.ContainsKey).Select(id => _parcels[id]).ToList();
            }

            /// <summary>Chặng của một kiện, kèm lô và phiếu đang chở nó (nếu có).</summary>
            public (string Stage, Shipment? Shipment, WroRequest? Wro) StageOf(Parcel parcel)
            {
                WroRequest? wro = null;
                Shipment? shipment = null;

                if (_rowByParcel.TryGetValue(parcel.Id, out var row)
                    && !string.Equals(row.Status, WroParcelStatuses.Removed, StringComparison.OrdinalIgnoreCase)
                    && _wros.TryGetValue(row.WroRequestId, out var w)
                    && !string.Equals(w.Status, WroStatuses.Rejected, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(w.Status, WroStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                {
                    wro = w;
                    if (_shipmentByWro.TryGetValue(w.Id, out var shipmentId))
                        Shipments.TryGetValue(shipmentId, out shipment);
                }

                if (shipment == null && _legacyShipmentByParcel.TryGetValue(parcel.Id, out var legacyId))
                    Shipments.TryGetValue(legacyId, out shipment);

                // Lô còn nháp thì với khách hàng vẫn đang "chuẩn bị xuất kho".
                if (shipment != null && !string.Equals(shipment.Status, ExportShipmentStatuses.Draft, StringComparison.OrdinalIgnoreCase))
                {
                    var shipmentStage = NormalizeShipmentStatus(shipment.Status);

                    // Lô đã về kho đích: từ đây kiện đi theo chặng Việt Nam của riêng nó (kiểm,
                    // lưu kho, giao...). Chỉ đọc trạng thái kiện sau mốc này, vì IN_TRANSIT trước
                    // đó là chặng quốc tế còn sau đó là giao nội địa.
                    if (shipmentStage == ExportShipmentStatuses.ArrivedDestination)
                        return (CustomerTrackingStages.FromVnParcelStatus(parcel.PackageStatus) ?? shipmentStage, shipment, wro);

                    return (shipmentStage, shipment, wro);
                }

                if (wro != null || shipment != null)
                    return (CustomerTrackingStages.PreparingExport, shipment, wro);

                // Kiện không thấy lô (dữ liệu cũ) nhưng trạng thái chỉ có ở Việt Nam.
                var vnOnly = parcel.PackageStatus?.Trim().ToUpperInvariant() switch
                {
                    VnParcelStatuses.ReceivedAtDestination or VnParcelStatuses.Quarantined or VnParcelStatuses.Delivered
                        or VnParcelStatuses.Disposed => CustomerTrackingStages.FromVnParcelStatus(parcel.PackageStatus),
                    _ => null
                };
                return (vnOnly ?? CustomerTrackingStages.AtOriginWarehouse, null, null);
            }

            /// <summary>Trạng thái lô cũ (trước luồng mới) quy về thang hiện tại.</summary>
            private static string NormalizeShipmentStatus(string? status) => status?.Trim().ToUpperInvariant() switch
            {
                "ARRIVED_IN_VN" => ExportShipmentStatuses.ArrivedVn,
                "HOLD" or "ISSUE" => ExportShipmentStatuses.OnHold,
                "CUSTOMS_IMPORT_PENDING" => ExportShipmentStatuses.InTransit,
                null or "" => CustomerTrackingStages.PreparingExport,
                var s => s
            };

            public DateTime? EstimatedArrivalOf(Shipment shipment)
            {
                var start = shipment.HandedOverAt ?? shipment.ShippedAt;
                if (!start.HasValue) return null;

                var days = _shipmentByWro.Where(kv => kv.Value == shipment.Id)
                    .Select(kv => _wros.TryGetValue(kv.Key, out var w) ? w.ShippingRouteId : null)
                    .Where(id => id.HasValue && _routes.ContainsKey(id.Value))
                    .Select(id => _routes[id!.Value].EstimatedTransitDays)
                    .Where(d => d.HasValue)
                    .DefaultIfEmpty(null)
                    .Max();

                return days.HasValue ? start.Value.AddDays(days.Value) : null;
            }

            public Guid? WarehouseOfBin(Guid? binId) =>
                binId.HasValue && _binWarehouse.TryGetValue(binId.Value, out var wh) ? wh : null;

            public string? WarehouseName(Guid? id) => id.HasValue && _warehouses.TryGetValue(id.Value, out var n) ? n : null;
            public string? CarrierName(Guid? id) => id.HasValue && _carriers.TryGetValue(id.Value, out var n) ? n : null;
            public string? UserName(Guid? id) => id.HasValue && _users.TryGetValue(id.Value, out var n) ? n : null;

            public string? CustomerNameOf(Order order)
            {
                if (!order.CustomerId.HasValue || !Customers.TryGetValue(order.CustomerId.Value, out var customer)) return null;
                if (!string.IsNullOrWhiteSpace(customer.FullName)) return customer.FullName;
                return customer.UserId.HasValue ? UserName(customer.UserId) : null;
            }
        }
    }
}
