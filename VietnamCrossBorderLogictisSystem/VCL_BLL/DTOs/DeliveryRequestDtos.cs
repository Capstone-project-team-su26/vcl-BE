using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>Sale lập yêu cầu giao hàng chặng cuối cho khách.</summary>
    public class CreateDeliveryRequestDto
    {
        [Required(ErrorMessage = "Thiếu đơn hàng.")]
        public Guid OrderId { get; set; }

        /// <summary>
        /// Kiện đem giao. Bỏ trống thì tự gom mọi kiện của đơn đang sẵn sàng giao
        /// (giao thẳng đã kiểm đếm, hoặc đã nằm kệ kho VN) mà chưa nằm trong phiếu giao nào.
        /// </summary>
        public List<Guid>? ParcelIds { get; set; }

        [Required(ErrorMessage = "Thiếu tên người nhận.")]
        [MaxLength(150)]
        public string ReceiverName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Thiếu số điện thoại người nhận.")]
        [MaxLength(30)]
        public string ReceiverPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Thiếu địa chỉ chi tiết (số nhà, tên đường).")]
        [MaxLength(300)]
        public string AddressDetail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Thiếu tỉnh/thành phố.")]
        [MaxLength(100)]
        public string Province { get; set; } = string.Empty;

        [Required(ErrorMessage = "Thiếu quận/huyện.")]
        [MaxLength(100)]
        public string District { get; set; } = string.Empty;

        [Required(ErrorMessage = "Thiếu phường/xã.")]
        [MaxLength(100)]
        public string Ward { get; set; } = string.Empty;

        public DateTime? ScheduledDate { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    /// <summary>OM duyệt hoặc từ chối yêu cầu giao hàng.</summary>
    public class UpdateDeliveryRequestStatusDto
    {
        /// <summary><c>DELIVERY_APPROVED</c> hoặc <c>DELIVERY_REJECTED</c>.</summary>
        [Required(ErrorMessage = "Trạng thái không được để trống.")]
        public string Status { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? RejectionReason { get; set; }
    }

    /// <summary>Kho bấm đặt đơn vị giao hàng cho phiếu đã được duyệt.</summary>
    public class DispatchDeliveryRequestDto
    {
        /// <summary>Token GoShip, dùng khi chạy local chưa cấu hình sẵn.</summary>
        public string? Token { get; set; }
    }

    public class DeliveryRequestDto
    {
        public Guid DeliveryRequestId { get; set; }
        public string DeliveryCode { get; set; } = string.Empty;

        public Guid? OrderId { get; set; }
        public string? OrderCode { get; set; }
        public string? CustomerName { get; set; }

        /// <summary>DELIVERY_PENDING · DELIVERY_APPROVED · DELIVERY_REJECTED · DELIVERY_DISPATCHED.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Nhãn tiếng Việt để FE hiện thẳng.</summary>
        public string StatusText { get; set; } = string.Empty;

        public string ReceiverName { get; set; } = string.Empty;
        public string ReceiverPhone { get; set; } = string.Empty;
        public string AddressDetail { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public string Ward { get; set; } = string.Empty;

        /// <summary>Địa chỉ ghép sẵn để hiện một dòng.</summary>
        public string FullAddress { get; set; } = string.Empty;

        public DateTime? ScheduledDate { get; set; }
        public string? Note { get; set; }
        public string? RejectionReason { get; set; }

        /// <summary>Mã vận đơn bên giao hàng, có sau khi kho đặt giao.</summary>
        public string? CarrierTrackingCode { get; set; }
        public DateTime? DispatchedAt { get; set; }

        public string? CreatedByName { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public int TotalParcels { get; set; }

        /// <summary>
        /// True khi trong phiếu có kiện phải nhặt trên ô kệ — FE dựa vào đây để quyết định màn
        /// gom hàng của kho có hiện cột mã ô kệ hay không.
        /// </summary>
        public bool HasShelfPick { get; set; }

        public List<DeliveryRequestParcelDto> Parcels { get; set; } = new();
    }

    public class DeliveryRequestParcelDto
    {
        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;

        /// <summary><c>FROM_SHELF</c> nhặt trên ô kệ · <c>FROM_STAGING</c> lấy ở khu tạm.</summary>
        public string? PickSource { get; set; }

        /// <summary>
        /// Nguyện vọng khách khai lúc đặt đơn: <c>DIRECT_DELIVERY</c> hoặc <c>STORE_AT_VN</c>.
        /// <para>
        /// Khách chọn gửi lại kho mà lại có yêu cầu giao thì OM nên xác nhận với khách trước khi
        /// duyệt — có thể khách gọi đổi ý, cũng có thể là nhầm đơn.
        /// </para>
        /// </summary>
        public string? CustomerIntent { get; set; }

        /// <summary>Nhãn tiếng Việt của <see cref="CustomerIntent"/>, null khi khách chưa chọn.</summary>
        public string? CustomerIntentText { get; set; }

        /// <summary>Chỉ có khi kiện đang nằm trên kệ.</summary>
        public Guid? BinId { get; set; }
        public string? BinCode { get; set; }
    }
}
