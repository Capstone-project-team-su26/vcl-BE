using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class ExportBlockReasonDto
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }

    public class EligibleParcelDto
    {
        public Guid ParcelId { get; set; }
        public string? PackageCode { get; set; }
        public Guid? OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string? CustomerName { get; set; }
        public string? ProductName { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public string? BinCode { get; set; }
        public string? ShelfCode { get; set; }
        public string? ZoneName { get; set; }
        /// <summary>Kiện thuộc đơn mua hộ (Order.OrderType = PURCHASE). FE hiện nhãn "Mua hộ" / "Ký gửi".</summary>
        public bool IsPurchase { get; set; }
        /// <summary>PURCHASE hoặc CONSIGNMENT.</summary>
        public string OrderType { get; set; } = "CONSIGNMENT";
        /// <summary>Mã yêu cầu mua hộ (PUR-…); null với đơn ký gửi.</summary>
        public string? PurchaseCode { get; set; }
        public bool IsEligible { get; set; }
        public List<ExportBlockReasonDto> Reasons { get; set; } = new();
    }

    public class WroParcelDto
    {
        public Guid Id { get; set; }
        public Guid ParcelId { get; set; }
        public string? PackageCode { get; set; }
        public string? Status { get; set; }
        public Guid? OrderId { get; set; }
        public string? ConsignmentCode { get; set; }
        public string? CustomerName { get; set; }
        public string? ProductName { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public string? SourceBinCode { get; set; }
        public string? StagingBinCode { get; set; }
        public DateTime? PickedAt { get; set; }
        public string? PickedByName { get; set; }
        public DateTime? RemovedAt { get; set; }
        public string? RemovedReason { get; set; }
        /// <summary>Kiện thuộc đơn mua hộ (Order.OrderType = PURCHASE). FE hiện nhãn "Mua hộ" / "Ký gửi".</summary>
        public bool IsPurchase { get; set; }
        /// <summary>PURCHASE hoặc CONSIGNMENT.</summary>
        public string OrderType { get; set; } = "CONSIGNMENT";
        /// <summary>Mã yêu cầu mua hộ (PUR-…); null với đơn ký gửi.</summary>
        public string? PurchaseCode { get; set; }
    }

    public class WroListItemDto
    {
        public Guid Id { get; set; }
        public string? WroCode { get; set; }
        public string? Status { get; set; }
        public string? StatusText { get; set; }
        public Guid? OriginWarehouseId { get; set; }
        public string? OriginWarehouseName { get; set; }
        public Guid? DestinationWarehouseId { get; set; }
        public string? DestinationWarehouseName { get; set; }
        public Guid? ShippingRouteId { get; set; }
        public string? ShippingRouteCode { get; set; }
        public int ParcelCount { get; set; }
        public int PickedCount { get; set; }

        /// <summary>Số kiện (còn trên phiếu) thuộc đơn mua hộ; phần còn lại là ký gửi.</summary>
        public int PurchaseParcelCount { get; set; }
        public decimal TotalWeight { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecidedByName { get; set; }
        public string? DecisionNote { get; set; }
        public Guid? ShipmentId { get; set; }
        public string? ShipmentCode { get; set; }
        public bool IsLegacy { get; set; }
    }

    public class WroDetailDto : WroListItemDto
    {
        public string? Note { get; set; }
        public Guid? SplitFromId { get; set; }
        public string? SplitFromCode { get; set; }
        public List<WroParcelDto> Parcels { get; set; } = new();
        public List<AttachmentDto> Attachments { get; set; } = new();
    }

    public class PagedWroListDto
    {
        public List<WroListItemDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    public class WroListQuery
    {
        public string? Status { get; set; }
        public Guid? WarehouseId { get; set; }
        public string? Search { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class CreateWarehouseReleaseRequest
    {
        public Guid ShippingRouteId { get; set; }
        public List<Guid> ParcelIds { get; set; } = new();
        public string? Note { get; set; }
    }

    public class DecideWarehouseReleaseRequest
    {
        /// <summary>APPROVED hoặc REJECTED.</summary>
        public string? Decision { get; set; }
        public string? Reason { get; set; }
    }

    public class PickParcelRequest
    {
        public Guid? ParcelId { get; set; }
        public string? PackageCode { get; set; }

        /// <summary>Ô ở khu xuất mà kho vừa quét để đặt kiện vào.</summary>
        public string? StagingBinCode { get; set; }
        public Guid? StagingBinId { get; set; }
    }

    public class RemoveWroParcelRequest
    {
        public string? Reason { get; set; }
        public List<Guid>? AttachmentIds { get; set; }

        /// <summary>Ô kệ khu lưu kho để trả kiện về — bắt buộc khi kiện đã bốc sang khu xuất.</summary>
        public string? ReturnBinCode { get; set; }
        public Guid? ReturnBinId { get; set; }
    }

    public class SplitWarehouseReleaseRequest
    {
        public List<Guid> ParcelIds { get; set; } = new();
    }
}
