using System;
using System.Collections.Generic;
using System.Linq;

namespace VCL_BLL.Helpers
{
    /// <summary>
    /// Một nơi khai các mốc hành trình của lô sau bàn giao: mốc nào đi từ mốc nào, cần kèm gì, và
    /// khách đọc thấy câu gì. Service ghi mốc, màn hình của Sale (bước tiếp theo được bấm) và màn
    /// theo dõi đơn của khách đều đọc từ đây, nên không bao giờ lệch nhau.
    /// </summary>
    public static class ShipmentMilestones
    {
        public const string RequiresNote = "NOTE";

        /// <summary>
        /// Dòng lịch sử lô do kho đích ghi khi kiểm nhận xong (InternationalShipmentService) — không
        /// phải trạng thái lô nên không nằm trong ExportShipmentStatuses, nhưng vẫn phải có chữ.
        /// </summary>
        public const string ReceivedAtDestination = VnParcelStatuses.ReceivedAtDestination;

        /// <summary>Mốc đích → các mốc được phép đi lên từ đó.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> AllowedFrom =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [ExportShipmentStatuses.Departed] = new[] { ExportShipmentStatuses.HandedOver },
                [ExportShipmentStatuses.InTransit] = new[]
                {
                    ExportShipmentStatuses.HandedOver, ExportShipmentStatuses.Departed,
                    ExportShipmentStatuses.Delayed, ExportShipmentStatuses.OnHold
                },
                [ExportShipmentStatuses.Delayed] = new[]
                {
                    ExportShipmentStatuses.Departed, ExportShipmentStatuses.InTransit, ExportShipmentStatuses.OnHold
                },
                [ExportShipmentStatuses.OnHold] = new[]
                {
                    ExportShipmentStatuses.Departed, ExportShipmentStatuses.InTransit, ExportShipmentStatuses.Delayed
                },
                [ExportShipmentStatuses.CustomsCleared] = new[]
                {
                    ExportShipmentStatuses.InTransit, ExportShipmentStatuses.Delayed, ExportShipmentStatuses.OnHold
                },

                // Tuyến không phải làm thủ tục nhập thì đi thẳng từ IN_TRANSIT — không bắt bấm một
                // mốc thông quan giả để qua được bước này.
                [ExportShipmentStatuses.ArrivedVn] = new[]
                {
                    ExportShipmentStatuses.CustomsCleared, ExportShipmentStatuses.InTransit,
                    ExportShipmentStatuses.Delayed, ExportShipmentStatuses.OnHold
                },
                [ExportShipmentStatuses.ArrivedDestination] = new[] { ExportShipmentStatuses.ArrivedVn }
            };

        /// <summary>Thứ tự hiển thị các mốc trên màn hình — cũng là thứ tự Sale nên bấm.</summary>
        public static readonly string[] DisplayOrder =
        {
            ExportShipmentStatuses.Departed, ExportShipmentStatuses.InTransit, ExportShipmentStatuses.Delayed,
            ExportShipmentStatuses.OnHold, ExportShipmentStatuses.CustomsCleared, ExportShipmentStatuses.ArrivedVn,
            ExportShipmentStatuses.ArrivedDestination
        };

        /// <summary>Lô đã bàn giao nhưng chưa về kho đích — Sale còn phải theo dõi.</summary>
        public static readonly HashSet<string> InProgressStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            ExportShipmentStatuses.HandedOver, ExportShipmentStatuses.Departed, ExportShipmentStatuses.InTransit,
            ExportShipmentStatuses.Delayed, ExportShipmentStatuses.OnHold, ExportShipmentStatuses.CustomsCleared,
            ExportShipmentStatuses.ArrivedVn
        };

        /// <summary>Những thứ phải có trước khi ghi mốc: ghi chú (NOTE) và/hoặc loại giấy tờ trên lô.</summary>
        public static string[] RequirementsOf(string status) => status?.Trim().ToUpperInvariant() switch
        {
            ExportShipmentStatuses.Delayed => new[] { RequiresNote },
            ExportShipmentStatuses.OnHold => new[] { RequiresNote, AttachmentDocumentTypes.Incident },
            ExportShipmentStatuses.CustomsCleared => new[] { AttachmentDocumentTypes.CustomsImport },
            _ => Array.Empty<string>()
        };

        /// <summary>Các mốc được bấm tiếp từ trạng thái hiện tại của lô.</summary>
        public static List<string> NextFrom(string currentStatus) => DisplayOrder
            .Where(target => AllowedFrom[target].Contains(currentStatus?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();

        /// <summary>
        /// Tên mốc cho nhân viên. Phủ mọi hằng của ExportShipmentStatuses, dòng lịch sử
        /// RECEIVED_AT_DESTINATION và các trạng thái lô đời cũ; mã lạ ra câu chung — KHÔNG BAO GIỜ
        /// trả lại chính cái mã (FE từng hiện nguyên "RECEIVED_AT_DESTINATION").
        /// </summary>
        public static string StaffText(string status) => status?.Trim().ToUpperInvariant() switch
        {
            null or "" => string.Empty,
            ExportShipmentStatuses.Draft => "Lô nháp",
            ExportShipmentStatuses.HandedOver => "Đã bàn giao cho hãng vận chuyển",
            ExportShipmentStatuses.Departed => "Đã khởi hành",
            ExportShipmentStatuses.InTransit => "Đang vận chuyển",
            ExportShipmentStatuses.Delayed => "Trễ lịch",
            ExportShipmentStatuses.OnHold => "Tạm giữ / sự cố",
            ExportShipmentStatuses.CustomsCleared => "Đã thông quan nhập",
            ExportShipmentStatuses.ArrivedVn => "Đã về Việt Nam",
            ExportShipmentStatuses.ArrivedDestination => "Đã về kho đích",
            ExportShipmentStatuses.Cancelled => "Đã huỷ",
            ReceivedAtDestination => "Kho đích đã nhận lô",
            // Trạng thái lô đời cũ (trước luồng xuất kho mới) — ShipmentTrackingService.NormalizeShipmentStatus.
            "CREATED" or "LOT_CREATED" => "Lô mới tạo",
            "PREPARING" => "Đang chuẩn bị",
            "READY_TO_SHIP" => "Sẵn sàng xuất kho",
            "MANIFESTED" => "Đã lên manifest",
            "CUSTOMS_EXPORT_PENDING" => "Chờ thông quan xuất",
            "CUSTOMS_IMPORT_PENDING" => "Chờ thông quan nhập",
            "CUSTOMS_REJECTED" => "Hải quan từ chối",
            "ARRIVED" => "Đã về",
            "ARRIVED_IN_VN" => "Đã về Việt Nam",
            "HOLD" or "ISSUE" => "Tạm giữ / sự cố",
            _ => "Cập nhật hành trình"
        };

        /// <summary>Tiêu đề mốc khách thấy — không dùng từ ngữ nội bộ như "lô", "phiếu".</summary>
        public static string CustomerTitle(string status) => status?.Trim().ToUpperInvariant() switch
        {
            null or "" => string.Empty,
            ExportShipmentStatuses.Draft => "Đang chuẩn bị xuất kho",
            ExportShipmentStatuses.HandedOver => "Hàng đã xuất kho",
            ExportShipmentStatuses.Departed => "Hàng đã khởi hành",
            ExportShipmentStatuses.InTransit => "Đang vận chuyển về Việt Nam",
            ExportShipmentStatuses.Delayed => "Hàng bị chậm so với dự kiến",
            ExportShipmentStatuses.OnHold => "Hàng đang tạm giữ để xử lý",
            ExportShipmentStatuses.CustomsCleared => "Đã thông quan",
            ExportShipmentStatuses.ArrivedVn => "Hàng đã về Việt Nam",
            ExportShipmentStatuses.ArrivedDestination => "Hàng đã về kho Việt Nam",
            ExportShipmentStatuses.Cancelled => "Chuyến vận chuyển đã huỷ",
            // Cùng câu với mốc "kho kiểm hàng" theo đơn (ShipmentTrackingService.AddVietnamEvents).
            ReceivedAtDestination => "Kho Việt Nam đã nhận hàng",
            "CREATED" or "LOT_CREATED" or "PREPARING" or "READY_TO_SHIP" or "MANIFESTED"
                or "CUSTOMS_EXPORT_PENDING" => "Đang chuẩn bị xuất kho",
            "CUSTOMS_IMPORT_PENDING" => "Đang làm thủ tục thông quan",
            "ARRIVED" or "ARRIVED_IN_VN" => "Hàng đã về Việt Nam",
            "CUSTOMS_REJECTED" or "HOLD" or "ISSUE" => "Hàng đang tạm giữ để xử lý",
            _ => "Hành trình có cập nhật mới"
        };

        /// <summary>Câu mặc định gửi khách khi Sale không nhập lời nhắn riêng.</summary>
        public static string CustomerDefaultMessage(string status, string orderCode) => status?.Trim().ToUpperInvariant() switch
        {
            ExportShipmentStatuses.HandedOver => $"Đơn {orderCode} đã rời kho nguồn và được bàn giao cho đơn vị vận chuyển.",
            ExportShipmentStatuses.Departed => $"Hàng của đơn {orderCode} đã khởi hành về Việt Nam.",
            ExportShipmentStatuses.InTransit => $"Hàng của đơn {orderCode} đang trên đường về Việt Nam.",
            ExportShipmentStatuses.Delayed => $"Hàng của đơn {orderCode} bị chậm so với dự kiến. Nhân viên sẽ cập nhật thời gian mới sớm nhất.",
            ExportShipmentStatuses.OnHold => $"Hàng của đơn {orderCode} đang tạm giữ để xử lý. Nhân viên sẽ liên hệ với bạn.",
            ExportShipmentStatuses.CustomsCleared => $"Hàng của đơn {orderCode} đã hoàn tất thông quan nhập khẩu.",
            ExportShipmentStatuses.ArrivedVn => $"Hàng của đơn {orderCode} đã về tới Việt Nam và đang được chuyển về kho.",
            ExportShipmentStatuses.ArrivedDestination => $"Hàng của đơn {orderCode} đã về tới kho tại Việt Nam, kho đang kiểm đếm.",
            ReceivedAtDestination => $"Kho Việt Nam đã nhận và cân đo hàng của đơn {orderCode}.",
            _ => $"Đơn {orderCode} có cập nhật hành trình mới."
        };
    }

    /// <summary>
    /// Chặng của một đơn nhìn từ phía khách — gộp trạng thái kiện, phiếu xuất và lô về một thang
    /// duy nhất, để màn "theo dõi đơn" không phải tự suy từ ba bảng.
    /// </summary>
    public static class CustomerTrackingStages
    {
        public const string NotReceived = "NOT_RECEIVED";
        public const string AtOriginWarehouse = "AT_ORIGIN_WAREHOUSE";
        public const string PreparingExport = "PREPARING_EXPORT";

        // Chặng ở Việt Nam, sau khi lô về kho đích.
        public const string ReceivedAtVn = "RECEIVED_AT_VN";
        public const string Quarantined = "QUARANTINED";
        public const string StoredAtVn = "STORED_AT_VN";
        public const string OutForDelivery = "OUT_FOR_DELIVERY";
        public const string DeliveryFailed = "DELIVERY_FAILED";
        public const string Delivered = "DELIVERED";
        public const string Disposed = "DISPOSED";

        private static readonly string[] Ladder =
        {
            NotReceived, AtOriginWarehouse, PreparingExport,
            ExportShipmentStatuses.HandedOver, ExportShipmentStatuses.Departed, ExportShipmentStatuses.InTransit,
            ExportShipmentStatuses.CustomsCleared, ExportShipmentStatuses.ArrivedVn, ExportShipmentStatuses.ArrivedDestination,
            ReceivedAtVn, StoredAtVn, OutForDelivery, Delivered
        };

        /// <summary>
        /// Vị trí trên thang. Trễ/tạm giữ tính ngang "đang vận chuyển", cách ly ngang "kho VN đã
        /// nhận", giao thất bại ngang "đang giao". Kiện đã huỷ theo sự cố không giữ chân đơn nên
        /// đứng cao nhất.
        /// </summary>
        public static int Rank(string? stage)
        {
            var s = stage?.Trim().ToUpperInvariant();
            if (s == Disposed) return Ladder.Length;
            if (s == ExportShipmentStatuses.Delayed || s == ExportShipmentStatuses.OnHold) s = ExportShipmentStatuses.InTransit;
            if (s == Quarantined) s = ReceivedAtVn;
            if (s == DeliveryFailed) s = OutForDelivery;
            var i = Array.IndexOf(Ladder, s);
            return i < 0 ? 0 : i;
        }

        /// <summary>Đơn còn đang trên đường tới tay khách (chưa giao xong).</summary>
        public static bool IsFinished(string? stage) =>
            string.Equals(stage, Delivered, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, Disposed, StringComparison.OrdinalIgnoreCase);

        public static string Text(string? stage, string? originWarehouseName) => stage?.Trim().ToUpperInvariant() switch
        {
            NotReceived => "Kho chưa nhận hàng",
            AtOriginWarehouse => string.IsNullOrWhiteSpace(originWarehouseName)
                ? "Hàng đang lưu tại kho nguồn, chờ xếp chuyến"
                : $"Hàng đang lưu tại {originWarehouseName}, chờ xếp chuyến",
            PreparingExport => "Đang chuẩn bị xuất kho",
            ReceivedAtVn => "Kho Việt Nam đã nhận và kiểm hàng",
            Quarantined => "Hàng đang được xử lý sự cố",
            StoredAtVn => "Đang lưu tại kho Việt Nam",
            OutForDelivery => "Đang giao hàng",
            DeliveryFailed => "Giao hàng chưa thành công",
            Delivered => "Đã giao hàng",
            Disposed => "Đã huỷ theo xử lý sự cố",
            _ => ShipmentMilestones.CustomerTitle(stage ?? string.Empty)
        };

        /// <summary>Chặng ở Việt Nam suy từ trạng thái kiện, khi lô đã về kho đích.</summary>
        public static string? FromVnParcelStatus(string? packageStatus)
        {
            var s = packageStatus?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(s)) return null;
            if (s == VnParcelStatuses.ReceivedAtDestination) return ReceivedAtVn;
            if (s == VnParcelStatuses.Quarantined) return Quarantined;
            if (s == VnParcelStatuses.Stored) return StoredAtVn;
            if (s == VnParcelStatuses.Delivered) return Delivered;
            if (s == VnParcelStatuses.Disposed) return Disposed;
            if (VnParcelStatuses.FailedOrReturning.Contains(s)) return DeliveryFailed;
            if (VnParcelStatuses.OutForDelivery.Contains(s)) return OutForDelivery;
            return null;
        }
    }
}
