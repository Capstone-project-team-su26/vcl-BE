using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Tạo phiếu nhập kho tại kho VN cho những kiện khách xin gửi lại.
    /// Chỉ nhận kiện đã được kho mở lô xác nhận và phân loại <c>STORE_AT_VN</c>.
    /// </summary>
    public class CreateWarehouseInboundRequestDto
    {
        /// <summary>
        /// Lập phiếu theo ĐƠN — màn "Đơn hàng cần xử lý" của kho đi đường này, vì kiện của một
        /// đơn hoàn toàn có thể nằm rải ở nhiều lô.
        /// </summary>
        public Guid? OrderId { get; set; }

        /// <summary>Lập phiếu theo LÔ — giữ cho màn kiểm đếm cũ. Phải có đúng một trong hai.</summary>
        public Guid? ShipmentId { get; set; }

        /// <summary>
        /// Kho VN nhận hàng vào. Bỏ trống thì suy ra từ kho đích của lô chở kiện.
        /// </summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>
        /// Kiện đưa vào phiếu. Bỏ trống thì tự gom mọi kiện đã được phân loại <c>STORE_AT_VN</c>
        /// mà chưa nằm trong phiếu còn hiệu lực nào.
        /// </summary>
        public List<Guid>? ParcelIds { get; set; }

        [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
        public string? Note { get; set; }
    }

    /// <summary>OM duyệt hoặc từ chối phiếu nhập kho.</summary>
    public class UpdateWarehouseInboundStatusDto
    {
        /// <summary><c>INBOUND_APPROVED</c> hoặc <c>INBOUND_REJECTED</c>.</summary>
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        /// <summary>Bắt buộc khi từ chối.</summary>
        [MaxLength(500, ErrorMessage = "Lý do từ chối tối đa 500 ký tự.")]
        public string? RejectionReason { get; set; }
    }

    public class WarehouseInboundRequestDto
    {
        public Guid InboundRequestId { get; set; }
        public string InboundCode { get; set; } = string.Empty;

        public Guid? ShipmentId { get; set; }
        public string? ShipmentCode { get; set; }

        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }

        /// <summary>INBOUND_PENDING · INBOUND_APPROVED · INBOUND_REJECTED.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Nhãn tiếng Việt để FE hiện thẳng, khỏi tự map.</summary>
        public string StatusText { get; set; } = string.Empty;

        public string? Note { get; set; }
        public string? RejectionReason { get; set; }

        public string? CreatedByName { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public int TotalParcels { get; set; }

        public List<WarehouseInboundParcelDto> Parcels { get; set; } = new();
    }

    public class WarehouseInboundParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;

        /// <summary>Trạng thái kiện — <c>STORED</c> nghĩa là kho đã xếp lên kệ xong.</summary>
        public string PackageStatus { get; set; } = string.Empty;

        public Guid? OrderId { get; set; }
        public string? OrderCode { get; set; }
        public string? CustomerName { get; set; }

        /// <summary>
        /// Nguyện vọng khách khai lúc đặt đơn: <c>DIRECT_DELIVERY</c> hoặc <c>STORE_AT_VN</c>.
        /// <para>
        /// OM duyệt phiếu nhập kho cần thấy trường này: kiện mà khách muốn giao ngay lại bị đưa
        /// vào phiếu nhập kho là dấu hiệu lệch — hoặc khách gọi đổi ý (hợp lệ), hoặc kho tick
        /// nhầm. Có thông tin thì OM hỏi lại được trước khi ký duyệt.
        /// </para>
        /// </summary>
        public string? CustomerIntent { get; set; }

        /// <summary>Nhãn tiếng Việt của <see cref="CustomerIntent"/>, null khi khách chưa chọn.</summary>
        public string? CustomerIntentText { get; set; }

        /// <summary>Ô kệ đang chứa kiện, có sau khi kho xếp kệ.</summary>
        public Guid? BinId { get; set; }
        public string? BinCode { get; set; }
    }

    /// <summary>Một đơn đang chờ kho lập phiếu nhập kho.</summary>
    public class PendingInboundOrderDto
    {
        public Guid OrderId { get; set; }
        public string? OrderCode { get; set; }

        /// <summary><c>CONSIGNMENT</c> hoặc <c>PURCHASE</c>.</summary>
        public string OrderType { get; set; } = string.Empty;

        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }

        public int StoreAtVnParcelCount { get; set; }
        public decimal StoreAtVnTotalWeight { get; set; }

        /// <summary>Kiện chờ nhập kho, để màn hình gửi thẳng lên khi lập phiếu.</summary>
        public List<Guid> ParcelIds { get; set; } = new();
        public List<PendingInboundParcelDto> Parcels { get; set; } = new();

        /// <summary>Kiện của đơn có thể nằm nhiều lô — kho vẫn cần biết hàng về theo chuyến nào.</summary>
        public List<string> ShipmentCodes { get; set; } = new();

        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }

        public DateTime? ArrivedAt { get; set; }

        /// <summary>Mốc Sale báo cho kho. Null với đơn mua hộ (loại này không qua bước báo).</summary>
        public DateTime? WarehouseNotifiedAt { get; set; }
        public string? WarehouseNotifiedByName { get; set; }
        public string? WarehouseNotifiedNote { get; set; }

        /// <summary>Số kiện kho từng ghi nhận lệch — biết trước khi đi lấy hàng.</summary>
        public int DiscrepancyParcelCount { get; set; }

        /// <summary>Lý do OM từ chối phiếu lần trước, nếu có.</summary>
        public string? LastRejectionReason { get; set; }
    }

    public class PendingInboundParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public decimal? Weight { get; set; }
        public string? ShipmentCode { get; set; }
        public bool HasDiscrepancy { get; set; }
        public string? DiscrepancySummary { get; set; }
    }
}
