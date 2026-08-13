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
        [Required(ErrorMessage = "Thiếu lô vận chuyển.")]
        public Guid ShipmentId { get; set; }

        /// <summary>
        /// Kho VN nhận hàng vào. Bỏ trống thì lấy kho đích của lô.
        /// </summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>
        /// Kiện đưa vào phiếu. Bỏ trống thì tự gom mọi kiện của lô đã được phân loại
        /// <c>STORE_AT_VN</c> mà chưa nằm trong phiếu nào.
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

        /// <summary>Ô kệ đang chứa kiện, có sau khi kho xếp kệ.</summary>
        public Guid? BinId { get; set; }
        public string? BinCode { get; set; }
    }
}
