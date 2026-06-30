using System;

namespace VCL_BLL.DTOs
{
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
