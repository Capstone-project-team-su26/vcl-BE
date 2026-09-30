using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    /// <summary>Phiếu xuất kho đã bốc xong, chờ đưa vào lô.</summary>
    public class ReadyWroForShipmentDto
    {
        public Guid WroRequestId { get; set; }
        public string? WroCode { get; set; }
        public string? Status { get; set; }
        public Guid? OriginWarehouseId { get; set; }
        public string? OriginWarehouseName { get; set; }
        public Guid? DestinationWarehouseId { get; set; }
        public string? DestinationWarehouseName { get; set; }
        public Guid? ShippingRouteId { get; set; }
        public string? ShippingRouteCode { get; set; }
        public int ParcelCount { get; set; }

        /// <summary>Số kiện của phiếu thuộc đơn mua hộ; phần còn lại là ký gửi.</summary>
        public int PurchaseParcelCount { get; set; }
        public decimal TotalWeight { get; set; }
        public DateTime? DecidedAt { get; set; }
        public bool IsLegacy { get; set; }
    }

    public class CreateExportShipmentRequest
    {
        public List<Guid> WroRequestIds { get; set; } = new();
        public string? ShippingMethod { get; set; }
        public Guid? CarrierId { get; set; }
        public string? Note { get; set; }
    }

    public class AddWroRequestsToShipmentRequest
    {
        public List<Guid> WroRequestIds { get; set; } = new();
    }

    public class ShipmentHandoverRequest
    {
        public Guid CarrierId { get; set; }
        public string? RecipientName { get; set; }
        public string? RecipientPhone { get; set; }
        public string? WaybillCode { get; set; }
        public string? VehicleNumber { get; set; }
        public string? SealNumber { get; set; }
        public string? HandoverLocation { get; set; }
        public string? Note { get; set; }
    }

    public class ShipmentMilestoneRequest
    {
        /// <summary>DEPARTED · IN_TRANSIT · DELAYED · ON_HOLD · CUSTOMS_CLEARED · ARRIVED_VN · ARRIVED_DESTINATION.</summary>
        public string? Status { get; set; }
        /// <summary>Ghi chú nội bộ — khách không thấy.</summary>
        public string? Note { get; set; }

        /// <summary>Lời nhắn hiển thị cho khách ở mốc này. Bỏ trống thì khách nhận câu mặc định của mốc.</summary>
        public string? CustomerMessage { get; set; }

        /// <summary>Vị trí hiện tại của hàng (cảng, sân bay, cửa khẩu...).</summary>
        public string? Location { get; set; }

        public string? CarrierTrackingCode { get; set; }
    }

    public class SetExportHoldRequest
    {
        public bool Hold { get; set; }
        public string? Reason { get; set; }
    }
}
