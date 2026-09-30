BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [PARCEL_INSPECTIONS] ADD [height] decimal(10,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [PARCEL_INSPECTIONS] ADD [length] decimal(10,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [PARCEL_INSPECTIONS] ADD [width] decimal(10,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [ORDERS] ADD [completed_automatically] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [proof_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [proof_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [proof_received_by] nvarchar(150) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [redelivery_fee] decimal(18,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [redelivery_fee_payment_id] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [returned_at] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [returned_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    ALTER TABLE [DELIVERY_REQUESTS] ADD [source_delivery_request_id] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    CREATE TABLE [PARCEL_INCIDENTS] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [incident_code] nvarchar(40) NULL,
        [parcel_id] uniqueidentifier NOT NULL,
        [order_id] uniqueidentifier NULL,
        [incident_type] nvarchar(30) NULL,
        [status] nvarchar(30) NULL,
        [stage] nvarchar(30) NULL,
        [description] nvarchar(1000) NULL,
        [reported_by] uniqueidentifier NULL,
        [reported_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [customer_choice] nvarchar(30) NULL,
        [customer_note] nvarchar(1000) NULL,
        [customer_responded_at] datetime2 NULL,
        [resolution] nvarchar(30) NULL,
        [resolution_note] nvarchar(1000) NULL,
        [compensation_amount] decimal(18,2) NULL,
        [resolved_by] uniqueidentifier NULL,
        [resolved_at] datetime2 NULL,
        [compensation_paid_at] datetime2 NULL,
        [compensation_paid_by] uniqueidentifier NULL,
        [compensation_reference] nvarchar(100) NULL,
        [row_version] rowversion NULL,
        CONSTRAINT [PK_PARCEL_INCIDENTS] PRIMARY KEY ([id]),
        CONSTRAINT [FK_PARCEL_INCIDENTS_ORDER] FOREIGN KEY ([order_id]) REFERENCES [ORDERS] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PARCEL_INCIDENTS_PARCEL] FOREIGN KEY ([parcel_id]) REFERENCES [PARCELS] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    CREATE INDEX [IX_PARCEL_INCIDENTS_ORDER_STATUS] ON [PARCEL_INCIDENTS] ([order_id], [status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    CREATE INDEX [IX_PARCEL_INCIDENTS_PARCEL] ON [PARCEL_INCIDENTS] ([parcel_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_PARCEL_INCIDENTS_CODE] ON [PARCEL_INCIDENTS] ([incident_code]) WHERE [incident_code] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918134107_VnArrivalFlow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918134107_VnArrivalFlow', N'8.0.27');
END;
GO

COMMIT;
GO

