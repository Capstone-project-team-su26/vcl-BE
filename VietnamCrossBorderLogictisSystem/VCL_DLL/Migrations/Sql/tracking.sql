BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918051610_ShipmentTrackingCustomerView'
)
BEGIN
    DROP INDEX [IX_TRACKINGS_shipment_id] ON [TRACKINGS];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918051610_ShipmentTrackingCustomerView'
)
BEGIN
    ALTER TABLE [TRACKINGS] ADD [created_by] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918051610_ShipmentTrackingCustomerView'
)
BEGIN
    ALTER TABLE [TRACKINGS] ADD [customer_message] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918051610_ShipmentTrackingCustomerView'
)
BEGIN
    CREATE INDEX [IX_TRACKINGS_SHIPMENT_TIME] ON [TRACKINGS] ([shipment_id], [tracking_time]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260918051610_ShipmentTrackingCustomerView'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260918051610_ShipmentTrackingCustomerView', N'8.0.27');
END;
GO

COMMIT;
GO

