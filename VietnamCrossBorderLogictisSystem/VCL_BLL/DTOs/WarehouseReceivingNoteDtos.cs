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
}
