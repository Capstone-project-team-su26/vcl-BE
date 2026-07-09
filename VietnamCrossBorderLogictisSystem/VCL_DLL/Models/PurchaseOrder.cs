#nullable disable
using System;
using System.Collections.Generic;

namespace VCL_DLL.Models;

public partial class PurchaseOrder
{
    public Guid Id { get; set; }

    public Guid PurchaseRequestId { get; set; }

    public Guid? PurchaseQuotationId { get; set; }

    public string PurchaseOrderCode { get; set; }

    /// <summary>Nhà cung cấp (nếu có).</summary>
    public string SupplierName { get; set; }

    /// <summary>Ghi chú mua hàng khi tạo đơn (nếu có).</summary>
    public string PurchaseNote { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>PURCHASE_ORDER_CREATED | ORDERED | PURCHASED | WAITING_WAREHOUSE_RECEIVE.</summary>
    public string Status { get; set; }

    /// <summary>Ghi chú xử lý ở lần cập nhật trạng thái gần nhất (nếu có).</summary>
    public string StatusNote { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual PurchaseRequest PurchaseRequest { get; set; }

    public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();
}
