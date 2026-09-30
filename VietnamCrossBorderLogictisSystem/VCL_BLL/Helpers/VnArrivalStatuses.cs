using System;
using System.Collections.Generic;

namespace VCL_BLL.Helpers
{
    /// <summary>Trạng thái kiện sau khi về kho Việt Nam (luồng 10–15).</summary>
    public static class VnParcelStatuses
    {
        /// <summary>Lô đã về kho đích, kho chưa kiểm kiện.</summary>
        public const string ArrivedDestination = "ARRIVED_DESTINATION";

        /// <summary>Kho VN đã kiểm, kiện tốt, đang ở khu nhận — chưa phải tồn lưu kho.</summary>
        public const string ReceivedAtDestination = "RECEIVED_AT_DESTINATION";

        /// <summary>Kiện hỏng / sai / thiếu, đang ở khu cách ly chờ xử lý sự cố.</summary>
        public const string Quarantined = "QUARANTINED";

        public const string Stored = "STORED";
        public const string Delivered = "DELIVERED";

        /// <summary>Huỷ theo quyết định xử lý sự cố — không tính cước, không giao.</summary>
        public const string Disposed = "DISPOSED";

        public const string DeliveryFailed = "DELIVERY_FAILED";
        public const string Returning = "RETURNING";
        public const string Returned = "RETURNED";

        /// <summary>Trạng thái giao nội địa do hãng báo về.</summary>
        public static readonly HashSet<string> OutForDelivery = new(StringComparer.OrdinalIgnoreCase)
        {
            "AWAITING_PICKUP", "PICKING_UP", "PICKED_UP", "OUT_FOR_DELIVERY", "IN_TRANSIT",
            "AT_CARRIER_WAREHOUSE", "DELIVERY_DELAYED", "PARTIALLY_DELIVERED"
        };

        public static readonly HashSet<string> FailedOrReturning = new(StringComparer.OrdinalIgnoreCase)
        {
            DeliveryFailed, Returning, Returned, "DELIVERY_CANCELLED", "LOST"
        };
    }

    public static class DestinationHandlings
    {
        public const string DirectDelivery = "DIRECT_DELIVERY";
        public const string StoreAtVn = "STORE_AT_VN";

        public static string? Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
        {
            DirectDelivery => DirectDelivery,
            StoreAtVn => StoreAtVn,
            _ => null
        };
    }

    /// <summary>Tình trạng kiện kho VN ghi lúc tiếp nhận.</summary>
    public static class ReceivingConditions
    {
        public const string Good = "GOOD";
        public const string Damaged = "DAMAGED";
        public const string WrongItem = "WRONG_ITEM";
        public const string MissingItems = "MISSING_ITEMS";

        public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
        { Good, Damaged, WrongItem, MissingItems };
    }

    public static class IncidentTypes
    {
        public const string Damaged = "DAMAGED";
        public const string WrongItem = "WRONG_ITEM";
        public const string MissingItems = "MISSING_ITEMS";
        public const string WeightDeviation = "WEIGHT_DEVIATION";
        public const string Complaint = "COMPLAINT";

        public static readonly HashSet<string> ReportableByWarehouse = new(StringComparer.OrdinalIgnoreCase)
        { Damaged, WrongItem, MissingItems };

        public static string Describe(string? type) => type?.Trim().ToUpperInvariant() switch
        {
            Damaged => "Hàng hỏng",
            WrongItem => "Sai hàng",
            MissingItems => "Thiếu hàng",
            WeightDeviation => "Cân lệch bất thường",
            Complaint => "Khách khiếu nại",
            _ => type ?? string.Empty
        };
    }

    public static class IncidentStatuses
    {
        public const string Open = "OPEN";
        public const string CustomerResponded = "CUSTOMER_RESPONDED";
        public const string Resolved = "RESOLVED";
    }

    public static class IncidentStages
    {
        public const string Receiving = "RECEIVING";
        public const string AfterDelivery = "AFTER_DELIVERY";
    }

    /// <summary>Cách xử lý sự cố — khách chọn, quản lý kho quyết.</summary>
    public static class IncidentResolutions
    {
        /// <summary>Nhận hàng như hiện trạng, đi tiếp.</summary>
        public const string Accept = "ACCEPT";

        /// <summary>Hàng vẫn đi tiếp, công ty bồi thường một khoản.</summary>
        public const string Compensate = "COMPENSATE";

        /// <summary>Huỷ hàng — kiện không tính cước, không giao.</summary>
        public const string Dispose = "DISPOSE";

        public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase) { Accept, Compensate, Dispose };
    }
}
