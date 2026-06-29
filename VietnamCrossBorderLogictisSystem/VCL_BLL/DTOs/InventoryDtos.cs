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
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
