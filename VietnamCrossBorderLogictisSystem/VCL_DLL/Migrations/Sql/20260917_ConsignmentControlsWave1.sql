BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSES] ADD [manager_id] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSE_RECEIVING_NOTES] ADD [approval_note] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSE_RECEIVING_NOTES] ADD [created_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSE_RECEIVING_NOTES] ADD [receive_approved_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSE_RECEIVING_NOTES] ADD [receive_approved_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSE_RECEIVING_NOTES] ADD [received_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSE_RECEIVING_NOTES] ADD [received_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSE_RECEIVING_NOTES] ADD [row_version] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [accepted_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [accepted_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [created_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [override_reason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [price_approval_note] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [price_approval_status] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [price_approved_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [price_approved_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATIONS] ADD [row_version] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATION_FEES] ADD [is_override] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [QUOTATION_FEES] ADD [original_amount] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [PAYMENTS] ADD [confirmation_source] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [PAYMENTS] ADD [confirmed_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [PAYMENTS] ADD [confirmed_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [PAYMENTS] ADD [received_amount] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [PAYMENTS] ADD [row_version] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [ORDERS] ADD [row_version] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [INVENTORIES] ADD [row_version] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    CREATE TABLE [ORDER_STATUS_HISTORIES] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [order_id] uniqueidentifier NOT NULL,
        [from_status] nvarchar(50) NULL,
        [to_status] nvarchar(50) NULL,
        [event_code] nvarchar(60) NULL,
        [note] nvarchar(1000) NULL,
        [actor_id] uniqueidentifier NULL,
        [created_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_ORDER_STATUS_HISTORIES] PRIMARY KEY ([id]),
        CONSTRAINT [FK_OSH_ORDER] FOREIGN KEY ([order_id]) REFERENCES [ORDERS] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    CREATE TABLE [WAREHOUSE_MANAGER_HISTORIES] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [warehouse_id] uniqueidentifier NOT NULL,
        [manager_id] uniqueidentifier NULL,
        [previous_manager_id] uniqueidentifier NULL,
        [changed_by] uniqueidentifier NULL,
        [reason] nvarchar(500) NULL,
        [changed_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_WAREHOUSE_MANAGER_HISTORIES] PRIMARY KEY ([id]),
        CONSTRAINT [FK_WMH_WAREHOUSE] FOREIGN KEY ([warehouse_id]) REFERENCES [WAREHOUSES] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    CREATE INDEX [IX_WAREHOUSES_manager_id] ON [WAREHOUSES] ([manager_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_PAYMENTS_ORDER_CODE] ON [PAYMENTS] ([order_code]) WHERE [order_code] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    CREATE INDEX [IX_ORDER_STATUS_HISTORIES_ORDER] ON [ORDER_STATUS_HISTORIES] ([order_id], [created_at]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    CREATE INDEX [IX_WAREHOUSE_MANAGER_HISTORIES_WAREHOUSE] ON [WAREHOUSE_MANAGER_HISTORIES] ([warehouse_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    ALTER TABLE [WAREHOUSES] ADD CONSTRAINT [FK_WAREHOUSE_MANAGER] FOREIGN KEY ([manager_id]) REFERENCES [USERS] ([id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN

                    UPDATE WAREHOUSE_RECEIVING_NOTES
                    SET receive_approved_at = created_at,
                        approval_note = N'Phiếu lập trước khi áp dụng duyệt trước nhận hàng'
                    WHERE receive_approved_at IS NULL AND status <> 'REJECTED';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN

                    UPDATE PAYMENTS
                    SET received_amount = amount,
                        confirmed_at = paid_at,
                        confirmation_source = 'LEGACY'
                    WHERE payment_status = 'PAID' AND confirmation_source IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN

                    UPDATE QUOTATIONS
                    SET price_approval_status = 'NOT_REQUIRED'
                    WHERE price_approval_status IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917081003_ConsignmentControlsWave1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260917081003_ConsignmentControlsWave1', N'8.0.27');
END;
GO

COMMIT;
GO

