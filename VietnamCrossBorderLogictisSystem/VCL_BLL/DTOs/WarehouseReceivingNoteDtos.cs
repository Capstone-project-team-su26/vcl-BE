using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class WarehouseReceivingNoteExpectedItemDto
    {
        public string ProductName { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal? DeclaredValue { get; set; }
    }

    /// <summary>
    /// Kết quả scan phiếu tiếp nhận kho (VCL-146) — thông tin để Warehouse Staff nhận hàng.
    /// </summary>
    public class WarehouseReceivingNoteScanResponseDto
    {
        public Guid ReceivingNoteId { get; set; }
        public string ReceivingNoteCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public Guid ConsignmentOrderId { get; set; }
        public string ConsignmentCode { get; set; } = string.Empty;

        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;

        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string WarehouseNote { get; set; } = string.Empty;

        public List<WarehouseReceivingNoteExpectedItemDto> ExpectedItems { get; set; } = new();
    }

    public class WarehouseReceivingNoteResponseDto
    {
        public Guid Id { get; set; }
        public string ReceivingNoteCode { get; set; } = string.Empty;
        public Guid ConsignmentOrderId { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string WarehouseNote { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class CreateWarehouseReceivingNoteRequestDto
    {
        public Guid ConsignmentOrderId { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseNote { get; set; } = string.Empty;
    }

    /// <summary>Số liệu thực tế nhân viên kho ghi nhận cho một dòng hàng</summary>
    public class ConfirmReceiptItemDto
    {
        /// <summary>Dòng hàng khai báo tương ứng, để trống nếu là hàng phát sinh ngoài khai báo</summary>
        public Guid? OrderItemId { get; set; }

        /// <summary>Bắt buộc khi không có OrderItemId</summary>
        public string ProductName { get; set; } = string.Empty;

        public string ProductType { get; set; } = string.Empty;

        /// <summary>Số lượng đếm thực tế tại kho</summary>
        public int ActualQuantity { get; set; }

        /// <summary>Trọng lượng cân thực tế tại kho, đơn vị kg</summary>
        public decimal? ActualWeight { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Yêu cầu xác nhận nhập kho kèm số liệu thực tế từng dòng hàng</summary>
    public class ConfirmReceiptRequestDto
    {
        public string WarehouseNote { get; set; } = string.Empty;

        public List<ConfirmReceiptItemDto> Items { get; set; } = new List<ConfirmReceiptItemDto>();
    }

    /// <summary>Dòng chi tiết biên bản nhập kho, đối chiếu khai báo với thực tế</summary>
    public class WarehouseReceivingNoteItemDto
    {
        public Guid Id { get; set; }
        public Guid? OrderItemId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductType { get; set; } = string.Empty;
        public int DeclaredQuantity { get; set; }
        public int ActualQuantity { get; set; }

        /// <summary>Âm là thiếu so với khai báo, dương là thừa</summary>
        public int QuantityDifference { get; set; }

        public decimal? DeclaredWeight { get; set; }
        public decimal? ActualWeight { get; set; }
        public decimal? WeightDifference { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>Biên bản nhập kho đầy đủ, gồm phần đầu phiếu và các dòng đối chiếu</summary>
    public class WarehouseReceivingNoteDetailDto : WarehouseReceivingNoteResponseDto
    {
        public string ConsignmentCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;

        /// <summary>True khi có bất kỳ dòng nào lệch số lượng hoặc trọng lượng</summary>
        public bool HasDiscrepancy { get; set; }

        public List<WarehouseReceivingNoteItemDto> Items { get; set; } = new List<WarehouseReceivingNoteItemDto>();
    }
}
