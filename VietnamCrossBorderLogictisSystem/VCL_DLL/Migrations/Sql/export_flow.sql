BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [decided_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [decided_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [decision_note] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [destination_warehouse_id] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [is_legacy] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [origin_warehouse_id] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [row_version] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [split_from_id] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [submitted_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD [submitted_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPPING_ROUTES] ADD [requires_customs_export] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENTS] ADD [created_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENTS] ADD [handed_over_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENTS] ADD [row_version] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENT_HANDOVERS] ADD [handed_over_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENT_HANDOVERS] ADD [note] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENT_HANDOVERS] ADD [recipient_name] nvarchar(150) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENT_HANDOVERS] ADD [recipient_phone] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENT_HANDOVERS] ADD [seal_number] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENT_HANDOVERS] ADD [vehicle_number] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [SHIPMENT_HANDOVERS] ADD [waybill_code] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [ORDERS] ADD [export_hold] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [ORDERS] ADD [export_hold_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [ORDERS] ADD [export_hold_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [ORDERS] ADD [export_hold_reason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    CREATE TABLE [ATTACHMENTS] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [entity_type] nvarchar(30) NULL,
        [entity_id] uniqueidentifier NOT NULL,
        [document_type] nvarchar(40) NULL,
        [file_name] nvarchar(255) NULL,
        [content_type] nvarchar(120) NULL,
        [size] bigint NOT NULL,
        [sha256] nvarchar(64) NULL,
        [storage_key] nvarchar(400) NULL,
        [uploaded_by] uniqueidentifier NULL,
        [uploaded_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [note] nvarchar(500) NULL,
        CONSTRAINT [PK_ATTACHMENTS] PRIMARY KEY ([id]),
        CONSTRAINT [FK_ATTACHMENTS_UPLOADER] FOREIGN KEY ([uploaded_by]) REFERENCES [USERS] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    CREATE TABLE [WRO_PARCELS] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [wro_request_id] uniqueidentifier NOT NULL,
        [parcel_id] uniqueidentifier NOT NULL,
        [status] nvarchar(30) NULL,
        [source_bin_id] uniqueidentifier NULL,
        [staging_bin_id] uniqueidentifier NULL,
        [picked_by] uniqueidentifier NULL,
        [picked_at] datetime2 NULL,
        [removed_by] uniqueidentifier NULL,
        [removed_at] datetime2 NULL,
        [removed_reason] nvarchar(500) NULL,
        [created_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_WRO_PARCELS] PRIMARY KEY ([id]),
        CONSTRAINT [FK_WRO_PARCELS_PARCEL] FOREIGN KEY ([parcel_id]) REFERENCES [PARCELS] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WRO_PARCELS_REQUEST] FOREIGN KEY ([wro_request_id]) REFERENCES [WRO_REQUESTS] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    CREATE INDEX [IX_WRO_REQUESTS_destination_warehouse_id] ON [WRO_REQUESTS] ([destination_warehouse_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    CREATE INDEX [IX_WRO_REQUESTS_ORIGIN_WAREHOUSE] ON [WRO_REQUESTS] ([origin_warehouse_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    CREATE INDEX [IX_ATTACHMENTS_ENTITY] ON [ATTACHMENTS] ([entity_type], [entity_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    CREATE INDEX [IX_ATTACHMENTS_uploaded_by] ON [ATTACHMENTS] ([uploaded_by]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    CREATE INDEX [IX_WRO_PARCELS_REQUEST] ON [WRO_PARCELS] ([wro_request_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_WRO_PARCELS_ACTIVE_PARCEL] ON [WRO_PARCELS] ([parcel_id]) WHERE [status] <> ''REMOVED''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD CONSTRAINT [FK_WRO_DESTINATION_WAREHOUSE] FOREIGN KEY ([destination_warehouse_id]) REFERENCES [WAREHOUSES] ([id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    ALTER TABLE [WRO_REQUESTS] ADD CONSTRAINT [FK_WRO_ORIGIN_WAREHOUSE] FOREIGN KEY ([origin_warehouse_id]) REFERENCES [WAREHOUSES] ([id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182507_ExportFlowWroParcels'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260917182507_ExportFlowWroParcels', N'8.0.27');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182611_ExportFlowLegacyData'
)
BEGIN

    -- 1. Kho nguồn / kho đích của phiếu: lấy theo tuyến đã gắn.
    UPDATE w
    SET w.origin_warehouse_id = r.origin_warehouse_id,
        w.destination_warehouse_id = r.destination_warehouse_id
    FROM WRO_REQUESTS w
    JOIN SHIPPING_ROUTES r ON r.id = w.shipping_route_id
    WHERE w.origin_warehouse_id IS NULL;

    -- Phiếu không có tuyến: suy kho từ ô kệ mà kiện của phiếu đang nằm.
    ;WITH parcel_wh AS (
        SELECT wi.wro_request_id, z.warehouse_id,
               ROW_NUMBER() OVER (PARTITION BY wi.wro_request_id ORDER BY COUNT(*) DESC) AS rn
        FROM WRO_ITEMS wi
        JOIN INVENTORIES i ON i.id = wi.inventory_id
        JOIN PARCELS p ON p.id = i.parcel_id
        JOIN BINS b ON b.id = p.bin_id
        JOIN SHELVES s ON s.id = b.shelf_id
        JOIN WAREHOUSE_ZONES z ON z.id = s.zone_id
        GROUP BY wi.wro_request_id, z.warehouse_id
    )
    UPDATE w SET w.origin_warehouse_id = pw.warehouse_id
    FROM WRO_REQUESTS w
    JOIN parcel_wh pw ON pw.wro_request_id = w.id AND pw.rn = 1
    WHERE w.origin_warehouse_id IS NULL;

    -- 2. Dựng WRO_PARCELS từ dữ liệu cũ. Một kiện chỉ được thuộc một phiếu còn hiệu lực, nên khi
    --    kiện xuất hiện ở nhiều phiếu thì giữ phiếu mới nhất và bỏ qua phần còn lại.
    ;WITH ranked AS (
        SELECT wi.wro_request_id, i.parcel_id, w.status AS wro_status, i.status AS inv_status, p.bin_id,
               ROW_NUMBER() OVER (PARTITION BY i.parcel_id ORDER BY w.created_at DESC) AS rn
        FROM WRO_ITEMS wi
        JOIN WRO_REQUESTS w ON w.id = wi.wro_request_id
        JOIN INVENTORIES i ON i.id = wi.inventory_id
        JOIN PARCELS p ON p.id = i.parcel_id
        WHERE UPPER(LTRIM(RTRIM(w.status))) NOT IN (N'RELEASE_REJECTED', N'CANCELLED', N'REJECTED')
    )
    INSERT INTO WRO_PARCELS (id, wro_request_id, parcel_id, status, source_bin_id, created_at)
    SELECT NEWID(), r.wro_request_id, r.parcel_id,
           CASE
               WHEN UPPER(LTRIM(RTRIM(r.wro_status))) IN (N'HANDED_OVER', N'IN_TRANSIT', N'ARRIVED_IN_VN', N'RELEASED')
                    THEN N'HANDED_OVER'
               WHEN UPPER(LTRIM(RTRIM(r.wro_status))) IN (N'PICKED', N'PACKING', N'PACKED')
                    OR UPPER(LTRIM(RTRIM(r.inv_status))) = N'PICKED'
                    THEN N'PICKED'
               ELSE N'RESERVED'
           END,
           r.bin_id,
           GETUTCDATE()
    FROM ranked r
    WHERE r.rn = 1
      AND NOT EXISTS (SELECT 1 FROM WRO_PARCELS wp WHERE wp.parcel_id = r.parcel_id AND wp.status <> N'REMOVED');

    -- 3. Trạng thái phiếu theo vòng đời mới.
    UPDATE WRO_REQUESTS SET is_legacy = 1;

    UPDATE WRO_REQUESTS SET status = N'PENDING_APPROVAL' WHERE UPPER(LTRIM(RTRIM(status))) = N'RELEASE_PENDING';
    UPDATE WRO_REQUESTS SET status = N'APPROVED'         WHERE UPPER(LTRIM(RTRIM(status))) = N'RELEASE_APPROVED';
    UPDATE WRO_REQUESTS SET status = N'REJECTED'         WHERE UPPER(LTRIM(RTRIM(status))) = N'RELEASE_REJECTED';
    UPDATE WRO_REQUESTS SET status = N'READY'            WHERE UPPER(LTRIM(RTRIM(status))) IN (N'PICKED', N'PACKING', N'PACKED');
    UPDATE WRO_REQUESTS SET status = N'HANDED_OVER'      WHERE UPPER(LTRIM(RTRIM(status))) IN (N'IN_TRANSIT', N'ARRIVED_IN_VN', N'RELEASED');

    -- Phiếu đã nằm trong một lô thì trạng thái phải nói đúng chuyện đó.
    UPDATE w SET w.status = N'IN_SHIPMENT'
    FROM WRO_REQUESTS w
    JOIN SHIPMENT_WRO_REQUESTS swr ON swr.wro_request_id = w.id
    WHERE UPPER(LTRIM(RTRIM(w.status))) = N'READY';

    -- 4. Trạng thái lô theo vòng đời mới.
    UPDATE SHIPMENTS SET status = N'DRAFT'
    WHERE UPPER(LTRIM(RTRIM(status))) IN (N'CREATED', N'MANIFESTED', N'LOT_CREATED', N'PREPARING', N'READY_TO_SHIP', N'CUSTOMS_EXPORT_PENDING');

    UPDATE SHIPMENTS SET status = N'IN_TRANSIT' WHERE UPPER(LTRIM(RTRIM(status))) = N'CUSTOMS_IMPORT_PENDING';
    UPDATE SHIPMENTS SET status = N'ON_HOLD'    WHERE UPPER(LTRIM(RTRIM(status))) IN (N'HOLD', N'ISSUE');

    -- Lô đang đi thì thời điểm bàn giao lấy theo lúc rời kho đã ghi trước đây.
    UPDATE SHIPMENTS SET handed_over_at = shipped_at
    WHERE handed_over_at IS NULL AND shipped_at IS NOT NULL
      AND UPPER(LTRIM(RTRIM(status))) IN (N'HANDED_OVER', N'DEPARTED', N'IN_TRANSIT', N'ARRIVED_VN', N'ARRIVED_DESTINATION');

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917182611_ExportFlowLegacyData'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260917182611_ExportFlowLegacyData', N'8.0.27');
END;
GO

COMMIT;
GO

