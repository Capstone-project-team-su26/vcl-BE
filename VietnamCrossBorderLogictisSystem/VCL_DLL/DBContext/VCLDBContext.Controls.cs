#nullable disable
using Microsoft.EntityFrameworkCore;

namespace VCL_DLL.Models;

public partial class VCLDBContext
{
    public virtual DbSet<WarehouseManagerHistory> WarehouseManagerHistories { get; set; }
    public virtual DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }
    public virtual DbSet<OrderItemService> OrderItemServices { get; set; }
    public virtual DbSet<WroParcel> WroParcels { get; set; }
    public virtual DbSet<Attachment> Attachments { get; set; }
    public virtual DbSet<ParcelIncident> ParcelIncidents { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.Property(e => e.ManagerId).HasColumnName("manager_id");
            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_WAREHOUSE_MANAGER");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(e => e.ReceivedAmount).HasColumnType("decimal(18, 2)").HasColumnName("received_amount");
            entity.Property(e => e.ConfirmedById).HasColumnName("confirmed_by");
            entity.Property(e => e.ConfirmedAt).HasColumnName("confirmed_at");
            entity.Property(e => e.ConfirmationSource).HasMaxLength(30).HasColumnName("confirmation_source");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");

            // Một mã đơn của cổng thanh toán chỉ ứng với một khoản thu, để webhook bắn trùng hoặc
            // hai request song song không ghi nhận cùng một lần chuyển tiền hai lần.
            entity.HasIndex(e => e.OrderCode)
                .IsUnique()
                .HasFilter("[order_code] IS NOT NULL")
                .HasDatabaseName("UX_PAYMENTS_ORDER_CODE");
        });

        modelBuilder.Entity<Quotation>(entity =>
        {
            entity.Property(e => e.CreatedById).HasColumnName("created_by");
            entity.Property(e => e.AcceptedById).HasColumnName("accepted_by");
            entity.Property(e => e.AcceptedAt).HasColumnName("accepted_at");
            entity.Property(e => e.PriceApprovalStatus).HasMaxLength(30).HasColumnName("price_approval_status");
            entity.Property(e => e.OverrideReason).HasMaxLength(500).HasColumnName("override_reason");
            entity.Property(e => e.PriceApprovedById).HasColumnName("price_approved_by");
            entity.Property(e => e.PriceApprovedAt).HasColumnName("price_approved_at");
            entity.Property(e => e.PriceApprovalNote).HasMaxLength(500).HasColumnName("price_approval_note");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");
        });

        modelBuilder.Entity<QuotationFee>(entity =>
        {
            entity.Property(e => e.OriginalAmount).HasColumnType("decimal(18, 2)").HasColumnName("original_amount");
            entity.Property(e => e.IsOverride).HasDefaultValue(false).HasColumnName("is_override");
            entity.Property(e => e.OrderItemId).HasColumnName("order_item_id");
            entity.HasIndex(e => e.OrderItemId).HasDatabaseName("IX_QUOTATION_FEES_ORDER_ITEM");
            entity.HasOne<OrderItem>().WithMany()
                .HasForeignKey(e => e.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_QUOTATION_FEES_ORDER_ITEM");
        });

        modelBuilder.Entity<WarehouseReceivingNote>(entity =>
        {
            entity.Property(e => e.CreatedById).HasColumnName("created_by");
            entity.Property(e => e.ReceiveApprovedById).HasColumnName("receive_approved_by");
            entity.Property(e => e.ReceiveApprovedAt).HasColumnName("receive_approved_at");
            entity.Property(e => e.ApprovalNote).HasMaxLength(500).HasColumnName("approval_note");
            entity.Property(e => e.ReceivedById).HasColumnName("received_by");
            entity.Property(e => e.ReceivedAt).HasColumnName("received_at");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");
        });

        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");
        });

        modelBuilder.Entity<WarehouseManagerHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_WAREHOUSE_MANAGER_HISTORIES");
            entity.ToTable("WAREHOUSE_MANAGER_HISTORIES");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.ManagerId).HasColumnName("manager_id");
            entity.Property(e => e.PreviousManagerId).HasColumnName("previous_manager_id");
            entity.Property(e => e.ChangedById).HasColumnName("changed_by");
            entity.Property(e => e.Reason).HasMaxLength(500).HasColumnName("reason");
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("changed_at");
            entity.HasIndex(e => e.WarehouseId).HasDatabaseName("IX_WAREHOUSE_MANAGER_HISTORIES_WAREHOUSE");
            entity.HasOne<Warehouse>().WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_WMH_WAREHOUSE");
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_ORDER_STATUS_HISTORIES");
            entity.ToTable("ORDER_STATUS_HISTORIES");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.FromStatus).HasMaxLength(50).HasColumnName("from_status");
            entity.Property(e => e.ToStatus).HasMaxLength(50).HasColumnName("to_status");
            entity.Property(e => e.EventCode).HasMaxLength(60).HasColumnName("event_code");
            entity.Property(e => e.Note).HasMaxLength(1000).HasColumnName("note");
            entity.Property(e => e.ActorId).HasColumnName("actor_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("created_at");
            entity.HasIndex(e => new { e.OrderId, e.CreatedAt }).HasDatabaseName("IX_ORDER_STATUS_HISTORIES_ORDER");
            entity.HasOne<Order>().WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OSH_ORDER");
        });

        modelBuilder.Entity<OrderItemService>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_ORDER_ITEM_SERVICES");
            entity.ToTable("ORDER_ITEM_SERVICES");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.OrderItemId).HasColumnName("order_item_id");
            entity.Property(e => e.PricingRuleId).HasColumnName("pricing_rule_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("created_at");
            // Một kiện chọn mỗi dịch vụ một lần, để phí không bị tính trùng.
            entity.HasIndex(e => new { e.OrderItemId, e.PricingRuleId })
                .IsUnique()
                .HasDatabaseName("UX_ORDER_ITEM_SERVICES_ITEM_RULE");
            entity.HasOne<OrderItem>().WithMany()
                .HasForeignKey(e => e.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OIS_ORDER_ITEM");
            entity.HasOne<PricingRule>().WithMany()
                .HasForeignKey(e => e.PricingRuleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OIS_PRICING_RULE");
        });
        // ── Luồng xuất kho quốc tế ──────────────────────────────────────────────────────────
        modelBuilder.Entity<WroRequest>(entity =>
        {
            entity.Property(e => e.OriginWarehouseId).HasColumnName("origin_warehouse_id");
            entity.Property(e => e.DestinationWarehouseId).HasColumnName("destination_warehouse_id");
            entity.Property(e => e.SubmittedById).HasColumnName("submitted_by");
            entity.Property(e => e.SubmittedAt).HasColumnName("submitted_at");
            entity.Property(e => e.DecidedById).HasColumnName("decided_by");
            entity.Property(e => e.DecidedAt).HasColumnName("decided_at");
            entity.Property(e => e.DecisionNote).HasMaxLength(1000).HasColumnName("decision_note");
            entity.Property(e => e.SplitFromId).HasColumnName("split_from_id");
            entity.Property(e => e.IsLegacy).HasDefaultValue(false).HasColumnName("is_legacy");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");
            entity.HasIndex(e => e.OriginWarehouseId).HasDatabaseName("IX_WRO_REQUESTS_ORIGIN_WAREHOUSE");
            entity.HasOne<Warehouse>().WithMany()
                .HasForeignKey(e => e.OriginWarehouseId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_WRO_ORIGIN_WAREHOUSE");
            entity.HasOne<Warehouse>().WithMany()
                .HasForeignKey(e => e.DestinationWarehouseId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_WRO_DESTINATION_WAREHOUSE");
        });

        modelBuilder.Entity<WroParcel>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_WRO_PARCELS");
            entity.ToTable("WRO_PARCELS");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.WroRequestId).HasColumnName("wro_request_id");
            entity.Property(e => e.ParcelId).HasColumnName("parcel_id");
            entity.Property(e => e.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(e => e.SourceBinId).HasColumnName("source_bin_id");
            entity.Property(e => e.StagingBinId).HasColumnName("staging_bin_id");
            entity.Property(e => e.PickedById).HasColumnName("picked_by");
            entity.Property(e => e.PickedAt).HasColumnName("picked_at");
            entity.Property(e => e.RemovedById).HasColumnName("removed_by");
            entity.Property(e => e.RemovedAt).HasColumnName("removed_at");
            entity.Property(e => e.RemovedReason).HasMaxLength(500).HasColumnName("removed_reason");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("created_at");
            entity.HasIndex(e => e.WroRequestId).HasDatabaseName("IX_WRO_PARCELS_REQUEST");
            // Một kiện chỉ nằm trong đúng một phiếu còn hiệu lực; kiện bị bỏ khỏi phiếu (REMOVED)
            // phải đưa lại được vào phiếu khác nên không nằm trong ràng buộc này.
            entity.HasIndex(e => e.ParcelId)
                .IsUnique()
                .HasFilter("[status] <> 'REMOVED'")
                .HasDatabaseName("UX_WRO_PARCELS_ACTIVE_PARCEL");
            entity.HasOne<WroRequest>().WithMany()
                .HasForeignKey(e => e.WroRequestId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_WRO_PARCELS_REQUEST");
            entity.HasOne<Parcel>().WithMany()
                .HasForeignKey(e => e.ParcelId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_WRO_PARCELS_PARCEL");
        });

        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.Property(e => e.CreatedById).HasColumnName("created_by");
            entity.Property(e => e.HandedOverAt).HasColumnName("handed_over_at");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");
        });

        modelBuilder.Entity<ShipmentHandover>(entity =>
        {
            entity.Property(e => e.RecipientName).HasMaxLength(150).HasColumnName("recipient_name");
            entity.Property(e => e.RecipientPhone).HasMaxLength(30).HasColumnName("recipient_phone");
            entity.Property(e => e.WaybillCode).HasMaxLength(100).HasColumnName("waybill_code");
            entity.Property(e => e.VehicleNumber).HasMaxLength(50).HasColumnName("vehicle_number");
            entity.Property(e => e.SealNumber).HasMaxLength(50).HasColumnName("seal_number");
            entity.Property(e => e.Note).HasMaxLength(1000).HasColumnName("note");
            entity.Property(e => e.HandedOverById).HasColumnName("handed_over_by");
        });

        modelBuilder.Entity<ShippingRoute>(entity =>
        {
            entity.Property(e => e.RequiresCustomsExport).HasDefaultValue(false).HasColumnName("requires_customs_export");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.ExportHold).HasDefaultValue(false).HasColumnName("export_hold");
            entity.Property(e => e.ExportHoldReason).HasMaxLength(500).HasColumnName("export_hold_reason");
            entity.Property(e => e.ExportHoldById).HasColumnName("export_hold_by");
            entity.Property(e => e.ExportHoldAt).HasColumnName("export_hold_at");
        });

        modelBuilder.Entity<Tracking>(entity =>
        {
            entity.Property(e => e.CreatedById).HasColumnName("created_by");
            entity.Property(e => e.CustomerMessage).HasMaxLength(1000).HasColumnName("customer_message");
            entity.HasIndex(e => new { e.ShipmentId, e.TrackingTime }).HasDatabaseName("IX_TRACKINGS_SHIPMENT_TIME");
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_ATTACHMENTS");
            entity.ToTable("ATTACHMENTS");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.EntityType).HasMaxLength(30).HasColumnName("entity_type");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.DocumentType).HasMaxLength(40).HasColumnName("document_type");
            entity.Property(e => e.FileName).HasMaxLength(255).HasColumnName("file_name");
            entity.Property(e => e.ContentType).HasMaxLength(120).HasColumnName("content_type");
            entity.Property(e => e.Size).HasColumnName("size");
            entity.Property(e => e.Sha256).HasMaxLength(64).HasColumnName("sha256");
            entity.Property(e => e.StorageKey).HasMaxLength(400).HasColumnName("storage_key");
            entity.Property(e => e.UploadedById).HasColumnName("uploaded_by");
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("uploaded_at");
            entity.Property(e => e.Note).HasMaxLength(500).HasColumnName("note");
            entity.HasIndex(e => new { e.EntityType, e.EntityId }).HasDatabaseName("IX_ATTACHMENTS_ENTITY");
            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.UploadedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ATTACHMENTS_UPLOADER");
        });
        // ── Luồng hàng về Việt Nam ──────────────────────────────────────────────────────────
        modelBuilder.Entity<ParcelInspection>(entity =>
        {
            entity.Property(e => e.Length).HasColumnType("decimal(10, 2)").HasColumnName("length");
            entity.Property(e => e.Width).HasColumnType("decimal(10, 2)").HasColumnName("width");
            entity.Property(e => e.Height).HasColumnType("decimal(10, 2)").HasColumnName("height");
        });

        modelBuilder.Entity<ParcelIncident>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_PARCEL_INCIDENTS");
            entity.ToTable("PARCEL_INCIDENTS");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.IncidentCode).HasMaxLength(40).HasColumnName("incident_code");
            entity.Property(e => e.ParcelId).HasColumnName("parcel_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.IncidentType).HasMaxLength(30).HasColumnName("incident_type");
            entity.Property(e => e.Status).HasMaxLength(30).HasColumnName("status");
            entity.Property(e => e.Stage).HasMaxLength(30).HasColumnName("stage");
            entity.Property(e => e.Description).HasMaxLength(1000).HasColumnName("description");
            entity.Property(e => e.ReportedById).HasColumnName("reported_by");
            entity.Property(e => e.ReportedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("reported_at");
            entity.Property(e => e.CustomerChoice).HasMaxLength(30).HasColumnName("customer_choice");
            entity.Property(e => e.CustomerNote).HasMaxLength(1000).HasColumnName("customer_note");
            entity.Property(e => e.CustomerRespondedAt).HasColumnName("customer_responded_at");
            entity.Property(e => e.Resolution).HasMaxLength(30).HasColumnName("resolution");
            entity.Property(e => e.ResolutionNote).HasMaxLength(1000).HasColumnName("resolution_note");
            entity.Property(e => e.CompensationAmount).HasColumnType("decimal(18, 2)").HasColumnName("compensation_amount");
            entity.Property(e => e.ResolvedById).HasColumnName("resolved_by");
            entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(e => e.CompensationPaidAt).HasColumnName("compensation_paid_at");
            entity.Property(e => e.CompensationPaidById).HasColumnName("compensation_paid_by");
            entity.Property(e => e.CompensationReference).HasMaxLength(100).HasColumnName("compensation_reference");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");
            entity.HasIndex(e => e.IncidentCode).IsUnique().HasDatabaseName("UX_PARCEL_INCIDENTS_CODE");
            entity.HasIndex(e => new { e.OrderId, e.Status }).HasDatabaseName("IX_PARCEL_INCIDENTS_ORDER_STATUS");
            entity.HasIndex(e => e.ParcelId).HasDatabaseName("IX_PARCEL_INCIDENTS_PARCEL");
            entity.HasOne<Parcel>().WithMany()
                .HasForeignKey(e => e.ParcelId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_PARCEL_INCIDENTS_PARCEL");
            entity.HasOne<Order>().WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_PARCEL_INCIDENTS_ORDER");
        });

        modelBuilder.Entity<DeliveryRequest>(entity =>
        {
            entity.Property(e => e.RedeliveryFee).HasColumnType("decimal(18, 2)").HasColumnName("redelivery_fee");
            entity.Property(e => e.RedeliveryFeePaymentId).HasColumnName("redelivery_fee_payment_id");
            entity.Property(e => e.SourceDeliveryRequestId).HasColumnName("source_delivery_request_id");
            entity.Property(e => e.ReturnedAt).HasColumnName("returned_at");
            entity.Property(e => e.ReturnedById).HasColumnName("returned_by");
            entity.Property(e => e.ProofReceivedBy).HasMaxLength(150).HasColumnName("proof_received_by");
            entity.Property(e => e.ProofAt).HasColumnName("proof_at");
            entity.Property(e => e.ProofById).HasColumnName("proof_by");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.CompletedAutomatically).HasDefaultValue(false).HasColumnName("completed_automatically");
        });

        ConfigurePurchaseFlow(modelBuilder);
        ConfigureUserWarehouses(modelBuilder);
        ConfigureSystemSettings(modelBuilder);
    }
}
