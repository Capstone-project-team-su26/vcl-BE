using System;
using System.Collections.Generic;

namespace VCL_BLL.DTOs
{
    public class ConfirmPickingItemDto
    {
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
    }

    public class ConfirmPickingRequestDto
    {
        public List<ConfirmPickingItemDto> Items { get; set; } = new();
    }

    public class ConfirmPickingItemResponseDto
    {
        public Guid InventoryId { get; set; }
        public int Quantity { get; set; }
        public Guid BinId { get; set; }
        public string BinCode { get; set; } = string.Empty;
        public string ShelfCode { get; set; } = string.Empty;
    }

    public class ConfirmPickingResponseDto
    {
        public Guid PickingListId { get; set; }
        public Guid WroRequestId { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? ConfirmedBy { get; set; }
        public string ConfirmedByName { get; set; } = string.Empty;
        public DateTime? ConfirmedAt { get; set; }
        public List<ConfirmPickingItemResponseDto> Items { get; set; } = new();
    }
}
