#nullable disable
using System;

namespace VCL_DLL.Models;

/// <summary>
/// Chi tiết một khoản HOÀN TIỀN mua hộ, theo từng sản phẩm.
/// <para>
/// Khoản hoàn (dòng PURCHASE_PAYMENTS có PaymentType bắt đầu bằng REFUND) chỉ giữ tổng tiền. Bảng
/// này ghi số đó được cộng từ đâu — tiền hàng, chênh giá, phí mua hộ, VAT phí, phí huỷ — để khách,
/// Sale và kế toán cùng đọc được công thức, và để lần hoàn sau không hoàn lại phần đã hoàn.
/// </para>
/// Amount = GoodsAmount + PriceDifferenceAmount + ServiceFeeAmount + VatAmount
///          + (ImportTaxInRefund ? ImportTaxAdjustment : 0) − CancelFeeAmount.
/// </summary>
public partial class PurchaseRefundLine
{
    public Guid Id { get; set; }

    /// <summary>Khoản hoàn (PURCHASE_PAYMENTS) chứa dòng này.</summary>
    public Guid PurchasePaymentId { get; set; }

    public Guid PurchaseRequestId { get; set; }

    /// <summary>Đơn mua NCC liên quan. Null khi sản phẩm chưa từng vào đơn mua nào.</summary>
    public Guid? PurchaseOrderId { get; set; }

    /// <summary>Sản phẩm của yêu cầu. Null với dòng tính cho cả đơn.</summary>
    public Guid? PurchaseRequestItemId { get; set; }

    public string ProductName { get; set; }

    /// <summary>
    /// PRICE_DIFF · UNFULFILLED · SUPPLIER_SHORT · CANCEL_CUSTOMER · CANCEL_SUPPLIER · PRICE_DIFF_RETURN.
    /// </summary>
    public string ReasonCode { get; set; }

    public int Quantity { get; set; }

    /// <summary>Đơn giá tiền hàng khách đã trả cho một đơn vị (giá báo, hoặc giá mua thật khi chênh giá đã tất toán).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Tiền hàng theo giá đã báo.</summary>
    public decimal GoodsAmount { get; set; }

    /// <summary>Phần chênh giá (dương: khách đã trả thêm, âm: đã/đang được hoàn phần thừa).</summary>
    public decimal PriceDifferenceAmount { get; set; }

    /// <summary>Phí mua hộ tương ứng phần hoàn.</summary>
    public decimal ServiceFeeAmount { get; set; }

    /// <summary>VAT của phần phí mua hộ được hoàn.</summary>
    public decimal VatAmount { get; set; }

    /// <summary>Thuế nhập khẩu không thu của phần hàng này (ghi dòng TAX_ADJUSTMENT âm trên hoá đơn đơn kho).</summary>
    public decimal ImportTaxAdjustment { get; set; }

    /// <summary>True khi thuế NK đã thu rồi nên trả lại bằng tiền trong khoản hoàn này thay vì trừ trên hoá đơn.</summary>
    public bool ImportTaxInRefund { get; set; }

    /// <summary>Phí huỷ bị trừ.</summary>
    public decimal CancelFeeAmount { get; set; }

    /// <summary>Số tiền hoàn của dòng.</summary>
    public decimal Amount { get; set; }

    /// <summary>Công thức dạng chữ để hiện cho khách / kế toán.</summary>
    public string Formula { get; set; }

    public string Note { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
