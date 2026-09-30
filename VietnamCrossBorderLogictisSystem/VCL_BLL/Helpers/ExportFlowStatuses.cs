using System;
using System.Collections.Generic;

namespace VCL_BLL.Helpers
{
    /// <summary>Vòng đời phiếu xuất kho: lập → duyệt → bốc sang khu xuất → vào lô → bàn giao.</summary>
    public static class WroStatuses
    {
        public const string Draft = "DRAFT";
        public const string PendingApproval = "PENDING_APPROVAL";
        public const string Approved = "APPROVED";
        public const string Picking = "PICKING";
        public const string Ready = "READY";
        public const string InShipment = "IN_SHIPMENT";
        public const string HandedOver = "HANDED_OVER";
        public const string Rejected = "REJECTED";
        public const string Cancelled = "CANCELLED";

        public static string Describe(string status) => status?.Trim().ToUpperInvariant() switch
        {
            Draft => "Nháp",
            PendingApproval => "Chờ quản lý kho duyệt",
            Approved => "Đã duyệt, chờ bốc hàng",
            Picking => "Đang bốc hàng sang khu xuất",
            Ready => "Đã bốc xong, chờ vào lô",
            InShipment => "Đã vào lô vận chuyển",
            HandedOver => "Đã bàn giao cho hãng vận chuyển",
            Rejected => "Bị từ chối",
            Cancelled => "Đã huỷ",
            _ => status ?? string.Empty
        };
    }

    /// <summary>Trạng thái một kiện trong phiếu xuất.</summary>
    public static class WroParcelStatuses
    {
        /// <summary>Đã khoá hàng, kiện vẫn nằm ở ô kệ khu lưu kho.</summary>
        public const string Reserved = "RESERVED";

        /// <summary>Đã bốc sang ô ở khu xuất.</summary>
        public const string Picked = "PICKED";

        public const string HandedOver = "HANDED_OVER";

        /// <summary>Bị bỏ khỏi phiếu, hàng trả về khu lưu kho.</summary>
        public const string Removed = "REMOVED";
    }

    /// <summary>Vòng đời lô vận chuyển quốc tế theo luồng xuất mới.</summary>
    public static class ExportShipmentStatuses
    {
        public const string Draft = "DRAFT";
        public const string HandedOver = "HANDED_OVER";
        public const string Departed = "DEPARTED";
        public const string InTransit = "IN_TRANSIT";
        public const string Delayed = "DELAYED";
        public const string OnHold = "ON_HOLD";
        public const string CustomsCleared = "CUSTOMS_CLEARED";
        public const string ArrivedVn = "ARRIVED_VN";
        public const string ArrivedDestination = "ARRIVED_DESTINATION";
        public const string Cancelled = "CANCELLED";
    }

    public static class AttachmentEntityTypes
    {
        public const string Order = "ORDER";
        public const string Wro = "WRO";
        public const string WroParcel = "WRO_PARCEL";
        public const string Shipment = "SHIPMENT";

        /// <summary>Sự cố hàng hoá (luồng hàng về VN).</summary>
        public const string Incident = "INCIDENT";

        /// <summary>Yêu cầu giao nội địa.</summary>
        public const string DeliveryRequest = "DELIVERY_REQUEST";

        /// <summary>Đơn mua NCC (luồng mua hộ) — chỉ nhân viên xem.</summary>
        public const string PurchaseOrder = "PURCHASE_ORDER";

        /// <summary>Một kiện hàng — ảnh chụp lúc xếp kiện vào ô kệ, ảnh tiếp nhận tại kho VN.</summary>
        public const string Parcel = "PARCEL";

        public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
        { Order, Wro, WroParcel, Shipment, Incident, DeliveryRequest, PurchaseOrder, Parcel };
    }

    public static class AttachmentDocumentTypes
    {
        public const string Permit = "PERMIT";
        public const string PickingIssue = "PICKING_ISSUE";
        public const string ParcelPhoto = "PARCEL_PHOTO";
        public const string CustomsExport = "CUSTOMS_EXPORT";
        public const string CommercialInvoice = "COMMERCIAL_INVOICE";
        public const string HandoverRecord = "HANDOVER_RECORD";
        public const string Waybill = "WAYBILL";
        public const string CustomsImport = "CUSTOMS_IMPORT";
        public const string TaxReceipt = "TAX_RECEIPT";
        public const string Incident = "INCIDENT";
        public const string Other = "OTHER";

        /// <summary>Ảnh / biên bản hiện trạng kiện của một sự cố — bắt buộc trước khi quyết định.</summary>
        public const string IncidentPhoto = "INCIDENT_PHOTO";

        /// <summary>Chứng từ đã chi bồi thường cho khách.</summary>
        public const string CompensationReceipt = "COMPENSATION_RECEIPT";

        /// <summary>Bằng chứng giao hàng (ảnh ký nhận) khi hãng không báo về.</summary>
        public const string DeliveryProof = "DELIVERY_PROOF";

        /// <summary>Bằng chứng đã đặt hàng bên NCC (ảnh đơn / hoá đơn) — bắt buộc khi Sale đặt NCC.</summary>
        public const string PurchaseProof = "PURCHASE_PROOF";

        /// <summary>
        /// Ảnh kiện đã nằm trong ô kệ — BẮT BUỘC trước khi ghi nhận xếp kệ.
        ///
        /// Sổ sách nói kiện nằm ở ô nào thì phải có ảnh chứng minh nó thật sự nằm ở đó.
        /// Không có ảnh thì "đã xếp kệ" chỉ là một cái nút ai cũng bấm được.
        /// </summary>
        public const string PutAwayProof = "PUT_AWAY_PROOF";

        /// <summary>
        /// Ảnh hiện trạng lô hàng lúc quản lý kho duyệt phiếu xuất — BẮT BUỘC khi duyệt.
        /// Duyệt xong là hàng rời kho, nên phải có ảnh tại thời điểm quyết định.
        /// </summary>
        public const string WroApprovalProof = "WRO_APPROVAL_PROOF";

        /// <summary>
        /// Ảnh kiện lúc kho VN tiếp nhận từ lô quốc tế (nhãn kiện trên cân, hiện trạng thùng) —
        /// BẮT BUỘC mỗi kiện trước khi xác nhận tiếp nhận. Gắn vào PARCEL; gắn vào SHIPMENT là ảnh
        /// toàn lô lúc mở container (không bắt buộc). Chỉ nhận file ảnh.
        /// </summary>
        public const string VnArrivalProof = "VN_ARRIVAL_PROOF";

        public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
        {
            Permit, PickingIssue, ParcelPhoto, CustomsExport, CommercialInvoice,
            HandoverRecord, Waybill, CustomsImport, TaxReceipt, Incident, Other,
            IncidentPhoto, CompensationReceipt, DeliveryProof, PurchaseProof,
            PutAwayProof, WroApprovalProof, VnArrivalProof
        };
    }

    /// <summary>Trạng thái kiện và tồn kho dùng chung với các luồng kho hiện có.</summary>
    public static class ParcelStatuses
    {
        public const string Stored = "STORED";
        public const string InTransit = "IN_TRANSIT";
    }

    public static class InventoryStatuses
    {
        public const string Available = "AVAILABLE";
        public const string Reserved = "RESERVED";
        public const string Picked = "PICKED";
        public const string Released = "RELEASED";
    }
}
