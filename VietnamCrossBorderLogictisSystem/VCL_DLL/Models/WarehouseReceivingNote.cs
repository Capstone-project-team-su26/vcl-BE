using System;

namespace VCL_DLL.Models
{
    public partial class WarehouseReceivingNote
    {
        public Guid Id { get; set; }
        public string ReceivingNoteCode { get; set; }
        public Guid ConsignmentOrderId { get; set; }
        public Guid WarehouseId { get; set; }
        public string WarehouseNote { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual Order ConsignmentOrder { get; set; }
        public virtual Warehouse Warehouse { get; set; }
    }
}
