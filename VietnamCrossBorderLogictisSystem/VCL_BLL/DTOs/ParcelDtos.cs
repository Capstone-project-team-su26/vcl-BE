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
        public Guid ParcelId { get; set; }
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

    /// <summary>Một kiện con khi tách kiện</summary>
    public class SplitParcelChildDto
    {
        /// <summary>Mã kiện con, để trống thì hệ thống tự sinh</summary>
        public string PackageCode { get; set; } = string.Empty;

        /// <summary>Số lượng hàng chuyển sang kiện con này</summary>
        public int Quantity { get; set; }

        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }

        /// <summary>Ô kệ chứa kiện con, để trống thì dùng ô kệ của kiện gốc</summary>
        public Guid? BinId { get; set; }
    }

    public class SplitParcelRequestDto
    {
        public List<SplitParcelChildDto> Children { get; set; } = new List<SplitParcelChildDto>();

        public string Note { get; set; } = string.Empty;
    }

    public class MergeParcelsRequestDto
    {
        /// <summary>Các kiện gốc cần gộp, tối thiểu 2 kiện</summary>
        public List<Guid> SourceParcelIds { get; set; } = new List<Guid>();

        /// <summary>Mã kiện sau khi gộp, để trống thì hệ thống tự sinh</summary>
        public string PackageCode { get; set; } = string.Empty;

        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }

        /// <summary>Ô kệ chứa kiện sau khi gộp, để trống thì dùng ô kệ của kiện gốc đầu tiên</summary>
        public Guid? BinId { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Thông tin rút gọn của một kiện trong kết quả gộp / tách</summary>
    public class ParcelSummaryDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public Guid? ParentParcelId { get; set; }
        public decimal? ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string PackageStatus { get; set; } = string.Empty;
        public Guid? BinId { get; set; }
        public string BinCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    /// <summary>Kết quả của thao tác gộp hoặc tách kiện</summary>
    public class ParcelStructureResponseDto
    {
        /// <summary>SPLIT hoặc MERGE</summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>Kiện gốc khi tách, hoặc kiện mới sau khi gộp</summary>
        public ParcelSummaryDto Parent { get; set; } = new ParcelSummaryDto();

        /// <summary>Kiện con khi tách, hoặc các kiện đã bị gộp</summary>
        public List<ParcelSummaryDto> Children { get; set; } = new List<ParcelSummaryDto>();
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

        // Trạng thái đã ghi nhận tồn kho
        public bool HasInventory { get; set; }
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
