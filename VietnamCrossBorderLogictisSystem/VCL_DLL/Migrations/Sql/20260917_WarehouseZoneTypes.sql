BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917151720_WarehouseZoneTypes'
)
BEGIN

    UPDATE WAREHOUSE_ZONES SET zone_type = UPPER(LTRIM(RTRIM(zone_type)))
    WHERE UPPER(LTRIM(RTRIM(zone_type))) IN (N'RECEIVING', N'QUARANTINE', N'STORAGE', N'OUTBOUND')
      AND zone_type <> UPPER(LTRIM(RTRIM(zone_type))) COLLATE Latin1_General_CS_AS;

    UPDATE WAREHOUSE_ZONES SET zone_type = N'RECEIVING'
    WHERE zone_type IS NULL AND LTRIM(RTRIM(zone_name)) IN (N'Khu nhận', N'Receiving');

    UPDATE WAREHOUSE_ZONES SET zone_type = N'OUTBOUND'
    WHERE zone_type IS NULL AND LTRIM(RTRIM(zone_name)) IN (N'Khu xuất', N'Xuất hàng');

    UPDATE WAREHOUSE_ZONES SET zone_type = N'STORAGE'
    WHERE zone_type IS NULL AND LTRIM(RTRIM(zone_name)) IN (N'Khu A', N'Khu Vực A', N'Khu Vực A - Updated');

    UPDATE WAREHOUSE_ZONES SET status = N'ACTIVE'
    WHERE status IS NULL OR UPPER(LTRIM(RTRIM(status))) = N'ACTIVE';

    UPDATE WAREHOUSE_ZONES SET status = N'INACTIVE'
    WHERE UPPER(LTRIM(RTRIM(status))) = N'INACTIVE';

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917151720_WarehouseZoneTypes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260917151720_WarehouseZoneTypes', N'8.0.27');
END;
GO

COMMIT;
GO

