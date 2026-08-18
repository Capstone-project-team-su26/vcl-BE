using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    /// <summary>
    /// Tiến trình giao hàng chặng cuối, dựng cho khách xem trên web.
    /// <para>
    /// Khách chỉ được thấy đơn của chính mình, và chỉ những thông tin phục vụ việc theo dõi —
    /// không kèm dữ liệu vận hành nội bộ như mã ô kệ hay tên nhân viên xử lý.
    /// </para>
    /// </summary>
    public class OrderDeliveryTrackingDto
    {
        public Guid OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;

        /// <summary>Trạng thái đơn, kèm nhãn tiếng Việt để FE hiện thẳng.</summary>
        public string OrderStatus { get; set; } = string.Empty;
        public string OrderStatusText { get; set; } = string.Empty;

        /// <summary>Khách đã bấm xác nhận nhận đủ hàng chưa.</summary>
        public bool IsCustomerConfirmed { get; set; }
        public DateTime? CustomerConfirmedAt { get; set; }

        /// <summary>
        /// True khi mọi kiện đã giao xong mà khách chưa xác nhận — FE dựa vào đây để bật nút
        /// "Tôi đã nhận đủ hàng".
        /// </summary>
        public bool CanConfirmReceipt { get; set; }

        /// <summary>Phí lưu kho đang phát sinh, để khách biết trước khi tới lấy.</summary>
        public decimal StorageFeeAmount { get; set; }
        public string? StorageFeeNote { get; set; }

        public List<CustomerDeliveryDto> Deliveries { get; set; } = new();
        public List<CustomerParcelStatusDto> Parcels { get; set; } = new();
    }

    /// <summary>Một phiếu giao của đơn, nhìn từ phía khách.</summary>
    public class CustomerDeliveryDto
    {
        public Guid DeliveryRequestId { get; set; }
        public string DeliveryCode { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;

        public string ReceiverName { get; set; } = string.Empty;
        public string FullAddress { get; set; } = string.Empty;

        /// <summary>Mã vận đơn bên giao hàng, để khách tra trên trang của hãng.</summary>
        public string? CarrierTrackingCode { get; set; }

        public DateTime? ScheduledDate { get; set; }
        public DateTime? DispatchedAt { get; set; }

        public int TotalParcels { get; set; }
    }

    /// <summary>Trạng thái một kiện, diễn giải theo ngôn ngữ của khách.</summary>
    public class CustomerParcelStatusDto
    {
        public string PackageCode { get; set; } = string.Empty;
        public string PackageStatus { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;

        /// <summary>DIRECT_DELIVERY giao thẳng · STORE_AT_VN gửi lại kho VN · null chưa phân loại.</summary>
        public string? DestinationHandling { get; set; }
        public string? HandlingText { get; set; }

        public DateTime? StoredAt { get; set; }
    }
}
