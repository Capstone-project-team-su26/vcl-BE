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

        /// <summary>Operations Manager đã duyệt biên bản đối chiếu. Null nghĩa là chưa ai duyệt.</summary>
        public Guid? ApprovedBy { get; set; }

        public DateTime? ApprovedAt { get; set; }

        /// <summary>Lý do OM từ chối, bắt buộc có khi phiếu ở REJECTED để kho biết phải sửa gì.</summary>
        public string RejectionReason { get; set; }

        public virtual Order ConsignmentOrder { get; set; }
        public virtual Warehouse Warehouse { get; set; }
    }
}
