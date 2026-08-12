using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CreateWroRequestItemDto
    {
        [Required]
        public Guid InventoryId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng xuất kho phải lớn hơn 0.")]
        public int Quantity { get; set; }
    }

    /// <summary>
    /// TH1: DTO Tạo phiếu xuất kho lẻ theo Đơn hàng (Hỏa tốc, lẻ, 1 đơn riêng biệt).
    /// Hỗ trợ truyền OrderId (Đơn ký gửi / Đơn mua hộ) HOẶC PurchaseRequestId (Yêu cầu mua hộ).
    /// </summary>
    public class CreateSingleWroRequestDto
    {
        public Guid? OrderId { get; set; }
        public Guid? PurchaseRequestId { get; set; }

        public string? ShelfCode { get; set; }
        public string? ExportReason { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }
    }

    /// <summary>
    /// TH2: DTO Tạo phiếu xuất kho gom lô (Nhiều kiện của nhiều đơn hàng khác nhau).
    /// Hỗ trợ gom theo danh sách Items (lẻ từng kiện) HOẶC danh sách OrderIds / PurchaseRequestIds.
    /// </summary>
    public class CreateBatchWroRequestDto
    {
        public string? ShelfCode { get; set; }
        public string? ExportReason { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }

        /// <summary>Danh sách tồn kho xuất lẻ từng kiện/mặt hàng.</summary>
        public List<CreateWroRequestItemDto> Items { get; set; } = new();

        /// <summary>Danh sách ID Đơn hàng (Order.Id) muốn gom toàn bộ tồn kho.</summary>
        public List<Guid>? OrderIds { get; set; }

        /// <summary>Danh sách ID Yêu cầu mua hộ (PurchaseRequest.Id) muốn gom toàn bộ tồn kho.</summary>
        public List<Guid>? PurchaseRequestIds { get; set; }
    }

    /// <summary>
    /// DTO tổng cho phiếu xuất kho (TH1: Lẻ theo OrderId hoặc TH2: Gom lô theo danh sách Items).
    /// </summary>
    public class CreateWroRequestDto
    {
        /// <summary>ID đơn hàng nếu xuất kho lẻ cho 1 đơn (TH1 - hỏa tốc, lẻ)</summary>
        public Guid? OrderId { get; set; }

        /// <summary>Loại xuất kho: "SINGLE" (phiếu lẻ theo đơn) hoặc "BATCH" (gom lô nhiều đơn)</summary>
        public string? ExportType { get; set; }

        public string? ShelfCode { get; set; }
        public string? ExportReason { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }

        /// <summary>Danh sách mặt hàng xuất kho (Bắt buộc với TH2 - Gom lô; Tùy chọn với TH1 nếu đã truyền OrderId)</summary>
        public List<CreateWroRequestItemDto> Items { get; set; } = new();
    }

    public class WroItemResponseDto
    {
        public Guid ItemId { get; set; }
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;
    }

    public class WroResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;

        /// <summary>Loại xuất kho ("SINGLE" hoặc "BATCH").</summary>
        public string ExportType { get; set; } = string.Empty;

        /// <summary>Mã vạch xuất kho quét khi bàn giao (VD: EXP-20260731-9348).</summary>
        public string ExportBarcode => !string.IsNullOrWhiteSpace(WroCode)
            ? WroCode.Replace("WRO-", "EXP-", StringComparison.OrdinalIgnoreCase)
            : string.Empty;

        public Guid? CustomerId { get; set; }
        public Guid? OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public string ReceiverAddress { get; set; } = string.Empty;

        /// <summary>Alias đồng bộ với consigneeName ở khâu Nhập kho cho Mobile App.</summary>
        public string ConsigneeName => ReceiverName;

        /// <summary>Alias đồng bộ với consigneePhone ở khâu Nhập kho cho Mobile App.</summary>
        public string ConsigneePhone => ReceiverPhone;

        /// <summary>Alias đồng bộ với consigneeAddress ở khâu Nhập kho cho Mobile App.</summary>
        public string ConsigneeAddress => !string.IsNullOrWhiteSpace(ReceiverAddress) ? ReceiverAddress : DeliveryAddress;

        public string Status { get; set; } = string.Empty;
        public string? ShelfCode { get; set; }
        public string? ExportReason { get; set; }
        public Guid? CarrierId { get; set; }
        public string CarrierName { get; set; } = string.Empty;
        public Guid? ShippingRouteId { get; set; }
        public string ShippingRoute { get; set; } = string.Empty;
        public decimal? ShippingFee { get; set; }
        public List<string> CustomsDocumentUrls { get; set; } = new();
        public bool IsCustomsCleared => CustomsDocumentUrls != null && CustomsDocumentUrls.Any();
        public string CustomsStatus => IsCustomsCleared ? "CUSTOMS_CLEARED" : "CUSTOMS_PENDING";
        public string CustomsStatusText => IsCustomsCleared ? "Đã thông quan" : "Chờ thông quan";
        public DateTime CreatedAt { get; set; }
        public List<WroItemResponseDto> Items { get; set; } = new();
    }

    public class WroDetailItemDto
    {
        public Guid ItemId { get; set; }
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
        public Guid? OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;
        public string PackageCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
        public string ShelfCode { get; set; } = string.Empty;
        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
    }

    public class WroDetailResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public string ExportType { get; set; } = string.Empty;

        /// <summary>Mã vạch xuất kho quét khi bàn giao (VD: EXP-20260731-9348).</summary>
        public string ExportBarcode => !string.IsNullOrWhiteSpace(WroCode)
            ? WroCode.Replace("WRO-", "EXP-", StringComparison.OrdinalIgnoreCase)
            : string.Empty;

        public string CreatedByName { get; set; } = string.Empty;
        public string CreatedByUserRole { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public string ReceiverAddress { get; set; } = string.Empty;

        /// <summary>Alias đồng bộ với consigneeName ở khâu Nhập kho cho Mobile App.</summary>
        public string ConsigneeName => ReceiverName;

        /// <summary>Alias đồng bộ với consigneePhone ở khâu Nhập kho cho Mobile App.</summary>
        public string ConsigneePhone => ReceiverPhone;

        /// <summary>Alias đồng bộ với consigneeAddress ở khâu Nhập kho cho Mobile App.</summary>
        public string ConsigneeAddress => !string.IsNullOrWhiteSpace(ReceiverAddress) ? ReceiverAddress : DeliveryAddress;
        public string Status { get; set; } = string.Empty;
        public string? ShelfCode { get; set; }
        public string? ExportReason { get; set; }
        public Guid? CarrierId { get; set; }
        public string CarrierName { get; set; } = string.Empty;
        public Guid? ShippingRouteId { get; set; }
        public string ShippingRoute { get; set; } = string.Empty;
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public string? VehicleNumber { get; set; }
        public string? TrackingNumber { get; set; }
        public string? HandoverNotes { get; set; }
        public DateTime? HandoverAt { get; set; }
        public decimal? ShippingFee { get; set; }
        public List<string> CustomsDocumentUrls { get; set; } = new();
        public bool IsCustomsCleared => CustomsDocumentUrls != null && CustomsDocumentUrls.Any();
        public string CustomsStatus => IsCustomsCleared ? "CUSTOMS_CLEARED" : "CUSTOMS_PENDING";
        public string CustomsStatusText => IsCustomsCleared ? "Đã thông quan" : "Chờ thông quan";
        public List<string> PackagingPhotos { get; set; } = new();
        public string? PackingNotes { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalQuantity { get; set; }
        public List<WroDetailItemDto> Items { get; set; } = new();
    }


    public class UpdateWroStatusRequestDto
    {
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        public string? RejectionReason { get; set; }
    }

    public class UpdateWroStatusResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class PickingItemResponseDto
    {
        public Guid ItemId { get; set; }
        public Guid InventoryId { get; set; }
        public Guid? OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string ConsignmentType { get; set; } = string.Empty;
        public string PackageCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
        public string ShelfCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public class PickingListResponseDto
    {
        public Guid PickingListId { get; set; }
        public Guid WroRequestId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public string ReceiverAddress { get; set; } = string.Empty;

        public string ConsigneeName => ReceiverName;
        public string ConsigneePhone => ReceiverPhone;
        public string ConsigneeAddress => !string.IsNullOrWhiteSpace(ReceiverAddress) ? ReceiverAddress : DeliveryAddress;

        public string Status { get; set; } = string.Empty;
        public Guid CreatedBy { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<PickingItemResponseDto> Items { get; set; } = new();
    }

    public class CompleteWroItemDto
    {
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
    }

    public class CompleteWroRequestDto
    {
        public List<CompleteWroItemDto> Items { get; set; } = new();
    }

    public class CompleteWroItemResponseDto
    {
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class CompleteWroResponseDto
    {
        public Guid WroRequestId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? ReleasedBy { get; set; }
        public string ReleasedByName { get; set; } = string.Empty;
        public DateTime? ReleasedAt { get; set; }
        public Guid? WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public List<CompleteWroItemResponseDto> Items { get; set; } = new();
    }

    public class AssignShippingRouteRequestDto
    {
        public Guid? CarrierId { get; set; }
        public Guid? ShippingMethodId { get; set; }
        public Guid? ShippingRouteId { get; set; }
        public string ShippingRoute { get; set; } = string.Empty;
        public int? EstimatedDeliveryDays { get; set; }
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public string? VehicleNumber { get; set; }
        public string? TrackingNumber { get; set; }
        public string? HandoverNotes { get; set; }
        public List<string>? CustomsDocumentUrls { get; set; }
        public string? Note { get; set; }
    }

    public class AssignShippingRouteResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? CarrierId { get; set; }
        public string CarrierName { get; set; } = string.Empty;
        public Guid? ShippingRouteId { get; set; }
        public string ShippingRoute { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    public class UpdatePackingDetailsRequestDto
    {
        /// <summary>Loại kiện xuất (VD: CARTON / WOODEN / PALLET / BUBBLE_BAG hoặc "Thùng carton", "Kiện gỗ", "Pallet", "Túi chống sốc").</summary>
        public string? PackageType { get; set; }

        /// <summary>Số lượng kiện cần đóng gói (mặc định là 1).</summary>
        public int PackageCount { get; set; } = 1;

        [Range(0.01, 10000, ErrorMessage = "Trọng lượng phải lớn hơn 0.")]
        public decimal Weight { get; set; }

        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public List<string> PackagingPhotos { get; set; } = new();
        public string? PackingNotes { get; set; }
    }

    public class UpdatePackingDetailsResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;

        /// <summary>Mã vạch xuất kho quét khi bàn giao (VD: EXP-20260731-9348).</summary>
        public string ExportBarcode => !string.IsNullOrWhiteSpace(WroCode)
            ? WroCode.Replace("WRO-", "EXP-", StringComparison.OrdinalIgnoreCase)
            : string.Empty;

        public string Status { get; set; } = string.Empty;
        public string? PackageType { get; set; }
        public int PackageCount { get; set; }
        public decimal Weight { get; set; }
        public decimal VolumeWeight { get; set; }
        public int PhotosCount { get; set; }
        public DateTime PackedAt { get; set; }
    }

    public class ReleaseHandoverRequestDto
    {
        [Required(ErrorMessage = "Vui lòng chọn đơn vị vận chuyển.")]
        public Guid CarrierId { get; set; }

        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }

        /// <summary>Biển số xe hoặc mã chuyến bay vận chuyển (VD: 51C-123.45 hoặc VN781).</summary>
        public string? VehicleNumber { get; set; }

        public string? TrackingNumber { get; set; }
        public string? HandoverNotes { get; set; }
    }

    public class HandoverDetailsDto
    {
        public Guid? CarrierId { get; set; }
        public string CarrierName { get; set; } = string.Empty;
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public string? VehicleNumber { get; set; }
        public string? TrackingNumber { get; set; }
        public string? HandoverNotes { get; set; }
        public DateTime HandoverAt { get; set; }
    }

    public class ReleaseHandoverResponseDto
    {
        public Guid WroId { get; set; }
        public string WroCode { get; set; } = string.Empty;

        /// <summary>Mã vạch xuất kho quét khi bàn giao (VD: EXP-20260731-9348).</summary>
        public string ExportBarcode => !string.IsNullOrWhiteSpace(WroCode)
            ? WroCode.Replace("WRO-", "EXP-", StringComparison.OrdinalIgnoreCase)
            : string.Empty;

        public string Status { get; set; } = string.Empty;
        public Guid? CarrierId { get; set; }
        public string CarrierName { get; set; } = string.Empty;
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public string? VehicleNumber { get; set; }
        public string? TrackingNumber { get; set; }
        public string? HandoverNotes { get; set; }
        public DateTime HandoverAt { get; set; }

        public HandoverDetailsDto HandoverDetails => new HandoverDetailsDto
        {
            CarrierId = CarrierId,
            CarrierName = CarrierName,
            DriverName = DriverName,
            DriverPhone = DriverPhone,
            VehicleNumber = VehicleNumber,
            TrackingNumber = TrackingNumber,
            HandoverNotes = HandoverNotes,
            HandoverAt = HandoverAt
        };
    }

    public class GetWroListRequestDto
    {
        public string? Status { get; set; }
        public string? SearchTerm { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public class PagedWroListResponseDto
    {
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
        public List<WroDetailResponseDto> Items { get; set; } = new();
    }
}

