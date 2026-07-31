using System;

namespace VCL_BLL.DTOs
{
    public class InventoryResponseDto
    {
        public Guid InventoryId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid OrderId { get; set; }
        public Guid? OrderItemId { get; set; }
        public Guid ParcelId { get; set; }
        public Guid BinId { get; set; }
        public int Quantity { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        /// <summary>Mã khách hàng sở hữu hàng tồn</summary>
        public string CustomerCode { get; set; } = string.Empty;

        /// <summary>Tên khách hàng sở hữu hàng tồn</summary>
        public string CustomerName { get; set; } = string.Empty;

        /// <summary>Số điện thoại khách hàng, dùng cho báo cáo tồn kho theo khách hàng</summary>
        public string CustomerPhone { get; set; } = string.Empty;

        /// <summary>Mã yêu cầu ký gửi liên kết</summary>
        public string ConsignmentCode { get; set; } = string.Empty;

        /// <summary>Mã kiện hàng đang lưu kho</summary>
        public string PackageCode { get; set; } = string.Empty;

        /// <summary>Mã ô kệ đang chứa kiện hàng</summary>
        public string BinCode { get; set; } = string.Empty;

        /// <summary>Kho đang lưu kiện hàng</summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>Tên kho đang lưu kiện hàng</summary>
        public string WarehouseName { get; set; } = string.Empty;

        /// <summary>
        /// Thời điểm bắt đầu lưu kho (UTC), lấy theo thời điểm put-away của kiện hàng.
        /// Nếu kiện hàng chưa có thời điểm put-away thì lấy theo thời điểm ghi nhận tồn kho.
        /// Dùng để tính số ngày lưu kho và phí lưu kho.
        /// </summary>
        public DateTime StoredAt { get; set; }

        /// <summary>Cân nặng thực tế của kiện hàng (kg)</summary>
        public decimal ActualWeight { get; set; }

        /// <summary>Thể tích thực tế của kiện hàng = Length × Width × Height</summary>
        public decimal ActualVolume { get; set; }
    }

    /// <summary>Yêu cầu điều chuyển kiện hàng sang ô kệ khác trong cùng kho</summary>
    public class RelocateInventoryRequestDto
    {
        /// <summary>Ô kệ đích</summary>
        public Guid ToBinId { get; set; }

        /// <summary>Lý do điều chuyển</summary>
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Một dòng nhật ký di chuyển kiện hàng trong kho</summary>
    public class InventoryMovementResponseDto
    {
        public Guid Id { get; set; }
        public Guid InventoryId { get; set; }
        public Guid ParcelId { get; set; }

        /// <summary>Mã kiện hàng</summary>
        public string PackageCode { get; set; } = string.Empty;

        public Guid? FromBinId { get; set; }

        /// <summary>Mã ô kệ trước khi di chuyển, rỗng nếu là lần cất kiện đầu tiên</summary>
        public string FromBinCode { get; set; } = string.Empty;

        public Guid ToBinId { get; set; }

        /// <summary>Mã ô kệ sau khi di chuyển</summary>
        public string ToBinCode { get; set; } = string.Empty;

        /// <summary>PUTAWAY hoặc RELOCATION</summary>
        public string Type { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public int Quantity { get; set; }

        /// <summary>Kho nơi diễn ra thao tác</summary>
        public Guid? WarehouseId { get; set; }

        /// <summary>Tên nhân viên thực hiện</summary>
        public string StaffName { get; set; } = string.Empty;

        /// <summary>Thời điểm thực hiện (UTC)</summary>
        public DateTime MovedAt { get; set; }
    }

    /// <summary>Yêu cầu ghi nhận kiểm kê định kỳ tại một ô kệ</summary>
    public class CreateCycleCountRequestDto
    {
        /// <summary>Ô kệ được kiểm kê</summary>
        public Guid BinId { get; set; }

        /// <summary>Số kiện đếm được thực tế</summary>
        public int ActualCount { get; set; }

        /// <summary>Ghi chú của nhân viên kiểm kê</summary>
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Yêu cầu xử lý chênh lệch của một phiếu kiểm kê</summary>
    public class ResolveCycleCountRequestDto
    {
        /// <summary>Cách xử lý chênh lệch tồn kho</summary>
        public string ResolutionNote { get; set; } = string.Empty;
    }

    /// <summary>Kết quả một lần kiểm kê định kỳ</summary>
    public class CycleCountResponseDto
    {
        public Guid Id { get; set; }
        public Guid? WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public Guid BinId { get; set; }
        public string BinCode { get; set; } = string.Empty;

        /// <summary>Số kiện hệ thống ghi nhận tại thời điểm kiểm kê</summary>
        public int ExpectedCount { get; set; }

        /// <summary>Số kiện đếm được thực tế</summary>
        public int ActualCount { get; set; }

        /// <summary>ActualCount - ExpectedCount</summary>
        public int Difference { get; set; }

        /// <summary>MATCHED, DISCREPANCY hoặc RESOLVED</summary>
        public string Status { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;

        /// <summary>Tên nhân viên thực hiện kiểm kê</summary>
        public string StaffName { get; set; } = string.Empty;

        /// <summary>Thời điểm kiểm kê (UTC)</summary>
        public DateTime CountedAt { get; set; }

        /// <summary>Tên người xử lý chênh lệch</summary>
        public string ResolvedByName { get; set; } = string.Empty;

        public DateTime? ResolvedAt { get; set; }

        public string ResolutionNote { get; set; } = string.Empty;
    }
}
