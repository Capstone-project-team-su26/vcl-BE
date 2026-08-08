using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class ParcelCheckInRequest
    {
        public string ShippingCode { get; set; } = string.Empty;

        /// <summary>
        /// Dòng hàng (kiện) đang được nhận. Bỏ trống thì hệ thống lấy kiện chưa nhận đầu tiên của đơn.
        /// Nên truyền để tránh nhận nhầm kiện khi hàng về lẻ.
        /// </summary>
        public Guid? OrderItemId { get; set; }

        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }

        /// <summary>
        /// Nhận nhiều kiện của cùng đơn trong một lần gọi. Nếu có giá trị thì các field
        /// cân nặng/kích thước phẳng ở trên bị bỏ qua.
        /// </summary>
        public List<ParcelCheckInPackage>? Packages { get; set; }
    }

    /// <summary>Một kiện trong lần check-in nhiều kiện.</summary>
    public class ParcelCheckInPackage
    {
        public Guid? OrderItemId { get; set; }
        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
    }

    public class ParcelCheckInResponse
    {
        public Guid ParcelId { get; set; }

        /// <summary>Mã người dùng đã nhập lúc check-in — có thể là mã ký gửi (VCL-) hoặc mã mua hộ (PUR-).</summary>
        public string ShippingCode { get; set; } = string.Empty;

        public Guid ConsignmentId { get; set; }

        /// <summary>Mã đơn kho mà kiện này thuộc về. Hàng mua hộ cũng có mã đơn kho riêng.</summary>
        public string ConsignmentCode { get; set; } = string.Empty;

        /// <summary>Loại đơn kho: CONSIGNMENT hoặc PURCHASE.</summary>
        public string? OrderType { get; set; }

        /// <summary>Mã yêu cầu mua hộ (PUR-) nếu đơn kho này sinh ra từ mua hộ. Null với đơn ký gửi.</summary>
        public string? PurchaseCode { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;

        /// <summary>Người nhận hàng cuối tại Việt Nam, khác với khách ký gửi.</summary>
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }

        public List<DeclaredItemDto> DeclaredItems { get; set; } = new List<DeclaredItemDto>();
        public string PackageCode { get; set; } = string.Empty;
        public decimal ActualWeight { get; set; }
        public decimal? Length { get; set; }
        public decimal? Width { get; set; }
        public decimal? Height { get; set; }
        public decimal? VolumetricWeight { get; set; }
        public decimal? ChargeableWeight { get; set; }
        public string PackageStatus { get; set; } = string.Empty;

        /// <summary>Số kiện khách khai báo trên đơn (mỗi dòng hàng là một kiện).</summary>
        public int DeclaredPackageCount { get; set; }

        /// <summary>Số kiện kho đã check-in tính cả kiện vừa nhận.</summary>
        public int ReceivedPackageCount { get; set; }

        /// <summary>Số kiện còn thiếu so với khai báo. 0 nghĩa là đã nhận đủ.</summary>
        public int RemainingPackageCount { get; set; }

        /// <summary>Đã nhận đủ kiện hay chưa. Chỉ khi đủ, đơn mới chuyển sang CHECKED_IN.</summary>
        public bool IsFullyReceived { get; set; }

        /// <summary>Trạng thái đơn sau khi check-in, để client không phải gọi lại API chi tiết.</summary>
        public string OrderStatus { get; set; } = string.Empty;

        /// <summary>Chênh lệch giữa cân nặng thực nhận và cân nặng khách khai cho kiện tương ứng (kg). Dương là nặng hơn khai báo.</summary>
        public decimal? WeightDifference { get; set; }

        /// <summary>Cảnh báo đối chiếu để kho xem lại, rỗng nghĩa là không có bất thường.</summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>Dòng hàng ứng với kiện vừa nhận.</summary>
        public Guid? OrderItemId { get; set; }

        /// <summary>Tên sản phẩm của kiện vừa nhận.</summary>
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Cân nặng khách khai cho kiện này.</summary>
        public decimal? DeclaredWeight { get; set; }

        /// <summary>Các kiện đã nhận trong lần gọi này. Chỉ có giá trị khi gửi nhiều kiện một lượt.</summary>
        public List<ParcelCheckInResponse> Parcels { get; set; } = new();

        /// <summary>Tiến độ nhận từng kiện của đơn, để app kho hiển thị kiện nào đã nhận.</summary>
        public List<ConsignmentPackageProgress> PackageProgress { get; set; } = new();
    }

    /// <summary>Tiến độ nhận của một kiện trong đơn.</summary>
    public class ConsignmentPackageProgress
    {
        public Guid OrderItemId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal? DeclaredWeight { get; set; }
        public bool IsReceived { get; set; }
        public string? PackageCode { get; set; }
        public decimal? ActualWeight { get; set; }
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
        public string? WarehouseRegion { get; set; }
        public string? RegionCode => WarehouseRegion;

        /// <summary>Người nhận hàng cuối tại Việt Nam, khác với khách ký gửi.</summary>
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }

        /// <summary>Dòng hàng ứng với kiện. Null với kiện check-in trước 03/08/2026.</summary>
        public Guid? OrderItemId { get; set; }

        /// <summary>Tên sản phẩm trong kiện, để nhân viên xếp kệ phân biệt các kiện cùng đơn.</summary>
        public string ProductName { get; set; } = string.Empty;

        public string ProductType { get; set; } = string.Empty;

        /// <summary>Số sản phẩm khách khai trong kiện.</summary>
        public int Quantity { get; set; }

        /// <summary>Ảnh sản phẩm khách gửi kèm, giúp đối chiếu bằng mắt khi xếp kệ.</summary>
        public List<string> ReferenceUrls { get; set; } = new();
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

        // Dòng hàng ứng với kiện
        public Guid? OrderItemId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int Quantity { get; set; }

        /// <summary>Cân nặng khách khai cho kiện này, để đối chiếu với số kho cân được.</summary>
        public decimal? DeclaredWeight { get; set; }

        public List<string> ReferenceUrls { get; set; } = new();

        // Khách hàng liên quan
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }

        // Người nhận hàng cuối tại Việt Nam, khác với khách ký gửi
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? ReceiverAddress { get; set; }

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
