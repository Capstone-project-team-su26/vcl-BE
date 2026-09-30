#nullable disable
using Microsoft.EntityFrameworkCore;

namespace VCL_DLL.Models;

public partial class VCLDBContext
{
    /// <summary>Chi tiết từng khoản hoàn tiền mua hộ theo sản phẩm.</summary>
    public virtual DbSet<PurchaseRefundLine> PurchaseRefundLines { get; set; }

    /// <summary>Cột của luồng mua hộ chuẩn — gọi từ <c>OnModelCreatingPartial</c>.</summary>
    private static void ConfigurePurchaseFlow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseQuotation>(entity =>
        {
            entity.Property(e => e.DomesticShippingFee).HasColumnType("decimal(18, 2)").HasDefaultValue(0m).HasColumnName("domestic_shipping_fee");
            entity.Property(e => e.FreightRatePerKg).HasColumnType("decimal(18, 2)").HasColumnName("freight_rate_per_kg");
            entity.Property(e => e.EstimatedWeight).HasColumnType("decimal(18, 3)").HasColumnName("estimated_weight");
            entity.Property(e => e.ServicePricingId).HasColumnName("service_pricing_id");
            entity.Property(e => e.VatRate).HasColumnType("decimal(9, 4)").HasColumnName("vat_rate");
            entity.Property(e => e.PrepayAmount).HasColumnType("decimal(18, 2)").HasColumnName("prepay_amount");
            entity.Property(e => e.EstimatedLaterAmount).HasColumnType("decimal(18, 2)").HasColumnName("estimated_later_amount");
        });

        modelBuilder.Entity<PurchasePayment>(entity =>
        {
            entity.Property(e => e.PurchaseOrderId).HasColumnName("purchase_order_id");
            entity.HasIndex(e => e.PurchaseOrderId).HasDatabaseName("IX_PURCHASE_PAYMENTS_PURCHASE_ORDER");
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.Currency).HasMaxLength(10).HasColumnName("currency");
            entity.Property(e => e.ExchangeRate).HasColumnType("decimal(18, 6)").HasColumnName("exchange_rate");
            entity.Property(e => e.SequenceNo).HasColumnName("sequence_no");
            entity.Property(e => e.QuotedGoodsAmount).HasColumnType("decimal(18, 2)").HasColumnName("quoted_goods_amount");
            entity.Property(e => e.PriceDifferenceAmount).HasColumnType("decimal(18, 2)").HasColumnName("price_difference_amount");
            entity.Property(e => e.CustomerDecisionAt).HasColumnName("customer_decision_at");
            entity.Property(e => e.CustomerRejectReason).HasMaxLength(1000).HasColumnName("customer_reject_reason");
            entity.Property(e => e.SubmittedAt).HasColumnName("submitted_at");
            entity.Property(e => e.SubmittedBy).HasColumnName("submitted_by");
            entity.Property(e => e.DecidedAt).HasColumnName("decided_at");
            entity.Property(e => e.DecidedBy).HasColumnName("decided_by");
            entity.Property(e => e.DecisionNote).HasMaxLength(1000).HasColumnName("decision_note");
            entity.Property(e => e.SupplierOrderCode).HasMaxLength(100).HasColumnName("supplier_order_code");
            entity.Property(e => e.OrderedAt).HasColumnName("ordered_at");
            entity.Property(e => e.OrderedBy).HasColumnName("ordered_by");
            entity.Property(e => e.SupplierConfirmedAt).HasColumnName("supplier_confirmed_at");
            entity.Property(e => e.SupplierShippedAt).HasColumnName("supplier_shipped_at");
            entity.Property(e => e.DomesticCarrier).HasMaxLength(100).HasColumnName("domestic_carrier");
            entity.Property(e => e.DomesticTrackingCode).HasMaxLength(100).HasColumnName("domestic_tracking_code");
            entity.Property(e => e.CancelledAt).HasColumnName("cancelled_at");
            entity.Property(e => e.CancelledBy).HasColumnName("cancelled_by");
            entity.Property(e => e.CancelReason).HasMaxLength(1000).HasColumnName("cancel_reason");

            entity.HasIndex(e => new { e.PurchaseRequestId, e.Status }).HasDatabaseName("IX_PURCHASE_ORDERS_REQUEST_STATUS");
            entity.HasOne<Supplier>().WithMany()
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_PURCHASE_ORDERS_SUPPLIER");
            entity.HasOne<Warehouse>().WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_PURCHASE_ORDERS_WAREHOUSE");
        });

        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.Property(e => e.UnitPriceOriginal).HasColumnType("decimal(18, 4)").HasColumnName("unit_price_original");
            entity.Property(e => e.QuotedUnitPrice).HasColumnType("decimal(18, 2)").HasColumnName("quoted_unit_price");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.PurchaseOrderId).HasColumnName("purchase_order_id");
            entity.HasIndex(e => e.PurchaseOrderId).HasDatabaseName("IX_ORDERS_PURCHASE_ORDER");
        });

        modelBuilder.Entity<PurchaseRefundLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_PURCHASE_REFUND_LINES");
            entity.ToTable("PURCHASE_REFUND_LINES");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.PurchasePaymentId).HasColumnName("purchase_payment_id");
            entity.Property(e => e.PurchaseRequestId).HasColumnName("purchase_request_id");
            entity.Property(e => e.PurchaseOrderId).HasColumnName("purchase_order_id");
            entity.Property(e => e.PurchaseRequestItemId).HasColumnName("purchase_request_item_id");
            entity.Property(e => e.ProductName).HasMaxLength(500).HasColumnName("product_name");
            entity.Property(e => e.ReasonCode).IsRequired().HasMaxLength(40).HasColumnName("reason_code");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)").HasColumnName("unit_price");
            entity.Property(e => e.GoodsAmount).HasColumnType("decimal(18, 2)").HasColumnName("goods_amount");
            entity.Property(e => e.PriceDifferenceAmount).HasColumnType("decimal(18, 2)").HasColumnName("price_difference_amount");
            entity.Property(e => e.ServiceFeeAmount).HasColumnType("decimal(18, 2)").HasColumnName("service_fee_amount");
            entity.Property(e => e.VatAmount).HasColumnType("decimal(18, 2)").HasColumnName("vat_amount");
            entity.Property(e => e.ImportTaxAdjustment).HasColumnType("decimal(18, 2)").HasColumnName("import_tax_adjustment");
            entity.Property(e => e.ImportTaxInRefund).HasColumnName("import_tax_in_refund");
            entity.Property(e => e.CancelFeeAmount).HasColumnType("decimal(18, 2)").HasColumnName("cancel_fee_amount");
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)").HasColumnName("amount");
            entity.Property(e => e.Formula).HasMaxLength(1000).HasColumnName("formula");
            entity.Property(e => e.Note).HasMaxLength(1000).HasColumnName("note");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(e => e.PurchasePaymentId).HasDatabaseName("IX_PURCHASE_REFUND_LINES_PAYMENT");
            entity.HasIndex(e => e.PurchaseRequestId).HasDatabaseName("IX_PURCHASE_REFUND_LINES_REQUEST");
            entity.HasIndex(e => e.PurchaseOrderId).HasDatabaseName("IX_PURCHASE_REFUND_LINES_ORDER");
            entity.HasOne<PurchasePayment>().WithMany()
                .HasForeignKey(e => e.PurchasePaymentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_PURCHASE_REFUND_LINES_PAYMENT");
        });
    }
}
