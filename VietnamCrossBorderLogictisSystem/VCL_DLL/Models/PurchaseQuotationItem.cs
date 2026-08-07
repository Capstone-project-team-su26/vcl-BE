#nullable disable
using System;

namespace VCL_DLL.Models;

public partial class PurchaseQuotationItem
{
    public Guid Id { get; set; }

    public Guid PurchaseQuotationId { get; set; }

    public Guid PurchaseRequestItemId { get; set; }

    /// <summary>Đơn giá đã quy đổi sang VND — mọi tính toán tiền bạc dùng cột này.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Đơn giá gốc Sale nhập theo ngoại tệ. Null khi Sale nhập thẳng VND.</summary>
    public decimal? UnitPriceOriginal { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }

    public virtual PurchaseQuotation PurchaseQuotation { get; set; }
}
