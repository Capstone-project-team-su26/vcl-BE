using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    public class CreateInternationalShipmentDto
    {
        [Required]
        [MinLength(1, ErrorMessage = "Phải chọn ít nhất một yêu cầu xuất kho.")]
        public List<Guid> WroRequestIds { get; set; } = new();

        [Required(ErrorMessage = "Kho đi không được để trống.")]
        public Guid OriginWarehouseId { get; set; }

        [Required(ErrorMessage = "Kho đến không được để trống.")]
        public Guid DestinationWarehouseId { get; set; }

        /// <summary>Đơn vị vận chuyển (nếu có).</summary>
        public Guid? CarrierId { get; set; }

        /// <summary>Phương thức vận chuyển (nếu có), VD: Sea, Air.</summary>
        public string? ShippingMethod { get; set; }

        /// <summary>
        /// Ảnh/scan tờ khai thông quan giao kèm lô cho đơn vị vận chuyển thứ ba.
        /// Mỗi lần bàn giao chỉ có một tờ, dù lô gồm nhiều phiếu xuất kho hay chỉ một kiện lẻ.
        /// </summary>
        public List<string>? CustomsDocumentUrls { get; set; }
    }

    public class InternationalShipmentWroDto
    {
        public Guid WroRequestId { get; set; }
        public string WroCode { get; set; } = string.Empty;
    }

    public class InternationalShipmentParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public decimal? Weight { get; set; }

        /// <summary>
        /// Đơn hàng và khách hàng của kiện này.
        /// <para>
        /// Một lô gom lô chứa kiện của nhiều đơn, nhiều khách khác nhau; sale bấm xác nhận lô
        /// về kho đích một lần là mọi đơn trong lô cùng đổi trạng thái. Nên màn xác nhận phải
        /// liệt kê được trước ai sẽ bị ảnh hưởng — thiếu mấy trường này thì FE chỉ có mã kiện,
        /// không dựng nổi danh sách đó.
        /// </para>
        /// </summary>
        public Guid? OrderId { get; set; }

        public string? OrderCode { get; set; }

        public Guid? CustomerId { get; set; }

        public string? CustomerName { get; set; }
    }

    public class UpdateInternationalShipmentStatusDto
    {
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        /// <summary>Ghi chú vận chuyển (nếu có).</summary>
        public string? Note { get; set; }

        /// <summary>
        /// Mã vận đơn / số container bên đối tác cấp cho lô. Sale nhập tay khi theo dõi lô trên
        /// trang quản lý của họ. Bỏ trống thì giữ nguyên mã đã lưu, không xoá mất.
        /// </summary>
        [MaxLength(100, ErrorMessage = "Mã vận đơn đối tác tối đa 100 ký tự.")]
        public string? CarrierTrackingCode { get; set; }
    }

    public class UpdateInternationalShipmentStatusResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Nhóm trạng thái ứng với 5 tab màn Vận chuyển:
        /// PREPARING · IN_TRANSIT · CUSTOMS · ARRIVED · ISSUE.
        /// FE lọc theo trường này thay vì tự map từng trạng thái.
        /// </summary>
        public string StatusTab { get; set; } = string.Empty;

        public string? Note { get; set; }

        /// <summary>Mã vận đơn / số container bên đối tác, sau khi cập nhật.</summary>
        public string? CarrierTrackingCode { get; set; }

        public DateTime UpdatedAt { get; set; }
        public string UpdatedByName { get; set; } = string.Empty;
    }

    public class ConfirmShipmentArrivalDto
    {
        /// <summary>Ghi chú khi xác nhận đến kho đích (nếu có).</summary>
        public string? Note { get; set; }
    }

    public class ConfirmShipmentArrivalResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public Guid? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ArrivedAt { get; set; }
        public string ConfirmedByName { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int TotalPackages { get; set; }
        public List<InternationalShipmentParcelDto> Parcels { get; set; } = new();
    }

    public class DestinationReceivingDto
    {
        /// <summary>Danh sách Parcel cần xác nhận nhận (bỏ trống = nhận tất cả kiện trong lô chưa nhận).</summary>
        public List<Guid>? ParcelIds { get; set; }

        public string? Note { get; set; }
    }

    public class DestinationReceivingParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
    }

    public class DestinationReceivingResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public Guid? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
        public string ReceivedByName { get; set; } = string.Empty;
        public string? Note { get; set; }
        public int ReceivedCount { get; set; }
        public List<DestinationReceivingParcelDto> Parcels { get; set; } = new();
    }

    public class InternationalShipmentResponseDto
    {
        public Guid ShipmentId { get; set; }
        public string ShipmentCode { get; set; } = string.Empty;
        public Guid? OriginWarehouseId { get; set; }
        public string OriginWarehouseName { get; set; } = string.Empty;
        public Guid? DestinationWarehouseId { get; set; }
        public string DestinationWarehouseName { get; set; } = string.Empty;
        public Guid? CarrierId { get; set; }
        public string? CarrierName { get; set; }
        public string? ShippingMethod { get; set; }
        public int TotalPackages { get; set; }
        public decimal TotalWeight { get; set; }
        public string Status { get; set; } = string.Empty;

        /// <summary>Nhóm trạng thái ứng với 5 tab màn Vận chuyển. FE lọc theo trường này.</summary>
        public string StatusTab { get; set; } = string.Empty;

        /// <summary>Tuyến vận chuyển, suy từ các WRO thành phần (mọi WRO trong lô đều cùng tuyến).</summary>
        public Guid? ShippingRouteId { get; set; }
        public string? ShippingRouteName { get; set; }

        /// <summary>
        /// Mã vận đơn / số container bên đối tác cấp. Sale dùng mã này tra cứu lô trên trang
        /// quản lý của họ, nên FE nên hiện rõ ở màn chi tiết lô và cho copy nhanh.
        /// </summary>
        public string? CarrierTrackingCode { get; set; }

        public DateTime? ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }

        /// <summary>
        /// Toàn bộ chứng từ PDF của lô: phiếu nhập kho của các đơn ký gửi liên quan,
        /// và phiếu xuất kho của từng WRO. FE hiển thị dạng danh sách link để mở/in.
        /// </summary>
        public List<string> PdfUrls { get; set; } = new();

        /// <summary>Ảnh chụp thực tế lúc đóng gói và bàn giao lô.</summary>
        public List<string> ExportImageUrls { get; set; } = new();

        /// <summary>Chứng từ thông quan của các WRO trong lô.</summary>
        public List<string> CustomsDocUrls { get; set; } = new();

        public List<InternationalShipmentWroDto> WroRequests { get; set; } = new();
        public List<InternationalShipmentParcelDto> Parcels { get; set; } = new();
    }
}
