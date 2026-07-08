using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class ParcelCheckInRequest
    {
        public string ShippingCode { get; set; } = string.Empty;
        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
    }

    public class ParcelCheckInResponse
    {
        public string ShippingCode { get; set; } = string.Empty;
        public Guid ConsignmentId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public List<DeclaredItemDto> DeclaredItems { get; set; } = new List<DeclaredItemDto>();
        public string PackageCode { get; set; } = string.Empty;
        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string PackageStatus { get; set; } = string.Empty;
    }

    public class DeclaredItemDto
    {
        public string ProductName { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? DeclaredValue { get; set; }
    }

    public class PendingPutawayParcelDto
    {
        public Guid Id { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string PackageStatus { get; set; } = string.Empty;
        public DateTime? CheckInTime { get; set; }
    }

    public class ParcelPutAwayRequest
    {
        public Guid WarehouseId { get; set; }
        public Guid ZoneId { get; set; }
        public Guid ShelfId { get; set; }
        public Guid BinId { get; set; }
    }

    /// <summary>
    /// Chi tiết một kiện hàng trong kho (VCL-88).
    /// </summary>
    public class ParcelDetailResponse
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;

        // Yêu cầu ký gửi liên quan
        public Guid? ConsignmentId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;

        // Khách hàng liên quan
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }

        // Cân đo thực tế
        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }

        // Thời điểm
        public DateTime? CheckInTime { get; set; }
        public DateTime? PutAwayTime { get; set; }

        // Vị trí kho (chỉ có khi đã put-away)
        public ParcelStorageLocation? StorageLocation { get; set; }
    }

    /// <summary>
    /// Vị trí lưu trữ của kiện trong kho.
    /// </summary>
    public class ParcelStorageLocation
    {
        public string WarehouseName { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public string ShelfCode { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// Một sản phẩm khai báo trong kiện (dùng cho tra cứu kiện của Customer - VCL-90).
    /// </summary>
    public class CustomerParcelItemDto
    {
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal? DeclaredValue { get; set; }
    }

    /// <summary>
    /// Kiện hàng thuộc yêu cầu ký gửi của Customer (VCL-90).
    /// </summary>
    public class CustomerParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public Guid? ConsignmentId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;

        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        /// <summary>Trọng lượng quy đổi.</summary>
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }

        public DateTime? CheckInTime { get; set; }
        public DateTime? PutAwayTime { get; set; }

        /// <summary>Ghi chú của sale (từ yêu cầu ký gửi).</summary>
        public string? SaleNote { get; set; }

        public List<CustomerParcelItemDto> Items { get; set; } = new();
    }

    public class ParcelPutAwayResponse
    {
        public string PackageCode { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string ZoneName { get; set; } = string.Empty;
        public string ShelfCode { get; set; } = string.Empty;
        public string BinCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
        public DateTime PutAwayTime { get; set; }
    }
}
