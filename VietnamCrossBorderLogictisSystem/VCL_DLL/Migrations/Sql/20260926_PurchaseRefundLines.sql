BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926084247_PurchaseRefundLines'
)
BEGIN
    CREATE TABLE [PURCHASE_REFUND_LINES] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [purchase_payment_id] uniqueidentifier NOT NULL,
        [purchase_request_id] uniqueidentifier NOT NULL,
        [purchase_order_id] uniqueidentifier NULL,
        [purchase_request_item_id] uniqueidentifier NULL,
        [product_name] nvarchar(500) NULL,
        [reason_code] nvarchar(40) NOT NULL,
        [quantity] int NOT NULL,
        [unit_price] decimal(18,2) NOT NULL,
        [goods_amount] decimal(18,2) NOT NULL,
        [price_difference_amount] decimal(18,2) NOT NULL,
        [service_fee_amount] decimal(18,2) NOT NULL,
        [vat_amount] decimal(18,2) NOT NULL,
        [import_tax_adjustment] decimal(18,2) NOT NULL,
        [import_tax_in_refund] bit NOT NULL,
        [cancel_fee_amount] decimal(18,2) NOT NULL,
        [amount] decimal(18,2) NOT NULL,
        [formula] nvarchar(1000) NULL,
        [note] nvarchar(1000) NULL,
        [created_by] uniqueidentifier NULL,
        [created_at] datetime2 NOT NULL,
        CONSTRAINT [PK_PURCHASE_REFUND_LINES] PRIMARY KEY ([id]),
        CONSTRAINT [FK_PURCHASE_REFUND_LINES_PAYMENT] FOREIGN KEY ([purchase_payment_id]) REFERENCES [PURCHASE_PAYMENTS] ([id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926084247_PurchaseRefundLines'
)
BEGIN
    CREATE INDEX [IX_PURCHASE_REFUND_LINES_ORDER] ON [PURCHASE_REFUND_LINES] ([purchase_order_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926084247_PurchaseRefundLines'
)
BEGIN
    CREATE INDEX [IX_PURCHASE_REFUND_LINES_PAYMENT] ON [PURCHASE_REFUND_LINES] ([purchase_payment_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926084247_PurchaseRefundLines'
)
BEGIN
    CREATE INDEX [IX_PURCHASE_REFUND_LINES_REQUEST] ON [PURCHASE_REFUND_LINES] ([purchase_request_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926084247_PurchaseRefundLines'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926084247_PurchaseRefundLines', N'8.0.27');
END;
GO

COMMIT;
GO

