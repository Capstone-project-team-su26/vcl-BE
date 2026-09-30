BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917141349_ItemServicesPerParcel'
)
BEGIN
    ALTER TABLE [QUOTATION_FEES] ADD [order_item_id] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917141349_ItemServicesPerParcel'
)
BEGIN
    CREATE TABLE [ORDER_ITEM_SERVICES] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [order_item_id] uniqueidentifier NOT NULL,
        [pricing_rule_id] uniqueidentifier NOT NULL,
        [created_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_ORDER_ITEM_SERVICES] PRIMARY KEY ([id]),
        CONSTRAINT [FK_OIS_ORDER_ITEM] FOREIGN KEY ([order_item_id]) REFERENCES [ORDER_ITEMS] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_OIS_PRICING_RULE] FOREIGN KEY ([pricing_rule_id]) REFERENCES [PRICING_RULES] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917141349_ItemServicesPerParcel'
)
BEGIN
    CREATE INDEX [IX_QUOTATION_FEES_ORDER_ITEM] ON [QUOTATION_FEES] ([order_item_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917141349_ItemServicesPerParcel'
)
BEGIN
    CREATE INDEX [IX_ORDER_ITEM_SERVICES_pricing_rule_id] ON [ORDER_ITEM_SERVICES] ([pricing_rule_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917141349_ItemServicesPerParcel'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ORDER_ITEM_SERVICES_ITEM_RULE] ON [ORDER_ITEM_SERVICES] ([order_item_id], [pricing_rule_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917141349_ItemServicesPerParcel'
)
BEGIN
    ALTER TABLE [QUOTATION_FEES] ADD CONSTRAINT [FK_QUOTATION_FEES_ORDER_ITEM] FOREIGN KEY ([order_item_id]) REFERENCES [ORDER_ITEMS] ([id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260917141349_ItemServicesPerParcel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260917141349_ItemServicesPerParcel', N'8.0.27');
END;
GO

COMMIT;
GO

