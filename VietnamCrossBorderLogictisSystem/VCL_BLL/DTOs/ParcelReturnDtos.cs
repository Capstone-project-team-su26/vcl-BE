using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VCL_BLL.DTOs
{
    /// <summary>Một hồ sơ hàng hoàn, dùng cho cả danh sách lẫn chi tiết.</summary>
    public class ParcelReturnDto
    {
        public Guid ReturnId { get; set; }
        public string ReturnCode { get; set; } = string.Empty;

        public Guid ParcelId { get; set; }
        public string PackageCode { get; set; } = string.Empty;

        public Guid? OrderId { get; set; }
        public string? OrderCode { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }

        public Guid? DeliveryRequestId { get; set; }
        public string? DeliveryCode { get; set; }
        public string? TrackingCode { get; set; }

        /// <summary>Địa chỉ đã giao hỏng — Sale gọi khách là hỏi ngay địa chỉ này.</summary>
        public string? ReceiverName { get; set; }
        public string? ReceiverPhone { get; set; }
        public string? DeliveryAddress { get; set; }

        public string Reason { get; set; } = string.Empty;
        public string ReasonText { get; set; } = string.Empty;
        public string? CarrierStatusText { get; set; }

        public string Status { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;

        public Guid? WarehouseId { get; set; }
        public string? WarehouseName { get; set; }

        /// <summary>Ô kệ kiện đang nằm sau khi kho xếp lại, null nếu còn ở khu tạm.</summary>
        public string? BinCode { get; set; }

        public DateTime? OpenedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public string? ReceivedByName { get; set; }

        public string? Resolution { get; set; }
        public string? ResolutionText { get; set; }
        public string? FeeBearer { get; set; }
        public string? FeeBearerText { get; set; }
        public bool WaiveStorageFee { get; set; }

        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedByName { get; set; }

        public string? Note { get; set; }
        public string? ResolutionNote { get; set; }

        /// <summary>
        /// Số ngày kể từ lúc mở hồ sơ. Hồ sơ nằm quá lâu chưa ai chốt là dấu hiệu bỏ quên —
        /// FE tô đỏ theo trường này thay vì tự trừ ngày.
        /// </summary>
        public int DaysOpen { get; set; }
    }

    /// <summary>Kho VN xác nhận đã cầm được kiện hoàn về.</summary>
    public class ReceiveParcelReturnRequest
    {
        /// <summary>Ô kệ xếp kiện vào. Bỏ trống thì kiện nằm khu tạm chờ xử lý.</summary>
        public Guid? BinId { get; set; }

        [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
        public string? Note { get; set; }
    }

    /// <summary>OM chốt hướng xử lý cho kiện đã hoàn về.</summary>
    public class ResolveParcelReturnRequest
    {
        /// <summary>
        /// <c>REDELIVER</c> giao lại · <c>CUSTOMER_PICKUP</c> khách tự đến lấy ·
        /// <c>DISPOSE</c> thanh lý · <c>COMPENSATE</c> bồi thường cho khách.
        /// </summary>
        [Required(ErrorMessage = "Vui lòng chọn hướng xử lý.")]
        public string Resolution { get; set; } = string.Empty;

        /// <summary>
        /// Ai chịu cước giao lại: <c>CUSTOMER</c> hoặc <c>COMPANY</c>.
        /// Bắt buộc khi chọn <c>REDELIVER</c>, vì đó là khoản tiền có người phải trả.
        /// </summary>
        public string? FeeBearer { get; set; }

        /// <summary>Miễn phí lưu kho cho quãng kiện nằm chờ sau khi hoàn về.</summary>
        public bool WaiveStorageFee { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập lý do chốt hướng xử lý.")]
        [MaxLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")]
        public string ResolutionNote { get; set; } = string.Empty;
    }

    /// <summary>Nhân viên mở hồ sơ bằng tay khi hãng chưa kịp báo hoặc báo sai.</summary>
    public class OpenParcelReturnRequest
    {
        [Required(ErrorMessage = "Thiếu mã kiện hàng.")]
        public Guid ParcelId { get; set; }

        /// <summary><c>FAILED_DELIVERY</c> · <c>RETURNING</c> · <c>LOST</c> · <c>CANCELLED</c>.</summary>
        [Required(ErrorMessage = "Vui lòng chọn lý do hoàn.")]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
        public string? Note { get; set; }
    }

    /// <summary>Số liệu đầu màn hình, để nhân viên biết chỗ nào đang tắc.</summary>
    public class ParcelReturnSummaryDto
    {
        public int Total { get; set; }

        /// <summary>Hãng đã báo hỏng nhưng hàng chưa về tới kho.</summary>
        public int AwaitingReturn { get; set; }

        /// <summary>Kho đã nhận lại, đang chờ OM chốt hướng xử lý.</summary>
        public int AwaitingDecision { get; set; }

        /// <summary>Hàng mất hẳn, phải xử lý bồi thường.</summary>
        public int Lost { get; set; }

        /// <summary>Hồ sơ mở quá 3 ngày mà chưa chốt xong.</summary>
        public int Overdue { get; set; }
    }

    public class ParcelReturnListDto
    {
        public ParcelReturnSummaryDto Summary { get; set; } = new();
        public List<ParcelReturnDto> Items { get; set; } = new();
    }
}
