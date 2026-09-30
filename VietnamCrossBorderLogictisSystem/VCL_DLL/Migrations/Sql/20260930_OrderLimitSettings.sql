BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN
    CREATE TABLE [SYSTEM_SETTINGS] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [setting_key] nvarchar(100) NOT NULL,
        [setting_group] nvarchar(50) NOT NULL,
        [decimal_value] decimal(18,2) NULL,
        [description] nvarchar(500) NULL,
        [updated_by] uniqueidentifier NULL,
        [updated_at] datetime2 NULL,
        [created_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        CONSTRAINT [PK_SYSTEM_SETTINGS] PRIMARY KEY ([id]),
        CONSTRAINT [FK_SYSTEM_SETTINGS_UPDATED_BY] FOREIGN KEY ([updated_by]) REFERENCES [USERS] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN
    CREATE TABLE [SYSTEM_SETTING_HISTORIES] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [setting_key] nvarchar(100) NOT NULL,
        [old_value] decimal(18,2) NULL,
        [new_value] decimal(18,2) NULL,
        [changed_by] uniqueidentifier NULL,
        [changed_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [note] nvarchar(500) NULL,
        CONSTRAINT [PK_SYSTEM_SETTING_HISTORIES] PRIMARY KEY ([id]),
        CONSTRAINT [FK_SYSTEM_SETTING_HISTORIES_CHANGED_BY] FOREIGN KEY ([changed_by]) REFERENCES [USERS] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN
    CREATE INDEX [IX_SYSTEM_SETTING_HISTORIES_changed_by] ON [SYSTEM_SETTING_HISTORIES] ([changed_by]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN
    CREATE INDEX [IX_SYSTEM_SETTING_HISTORIES_KEY_TIME] ON [SYSTEM_SETTING_HISTORIES] ([setting_key], [changed_at]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN
    CREATE INDEX [IX_SYSTEM_SETTINGS_updated_by] ON [SYSTEM_SETTINGS] ([updated_by]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN
    CREATE UNIQUE INDEX [UX_SYSTEM_SETTINGS_KEY] ON [SYSTEM_SETTINGS] ([setting_key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN

    INSERT INTO [SYSTEM_SETTINGS] ([setting_key], [setting_group], [decimal_value], [description])
    SELECT v.[setting_key], N'ORDER_LIMITS', v.[decimal_value], v.[description]
    FROM (VALUES
        (N'ORDER_MAX_TOTAL_WEIGHT_KG',      CAST(5 AS decimal(18,2)),        N'Tổng cân nặng tối đa của đơn ký gửi'),
        (N'ORDER_MAX_TOTAL_DECLARED_VALUE', CAST(10000000 AS decimal(18,2)), N'Tổng giá trị khai báo tối đa của đơn ký gửi'),
        (N'ORDER_MAX_PARCEL_WEIGHT_KG',     CAST(3 AS decimal(18,2)),        N'Cân nặng tối đa mỗi kiện'),
        (N'ORDER_MAX_ITEM_DECLARED_VALUE',  CAST(6000000 AS decimal(18,2)),  N'Giá trị khai báo tối đa mỗi kiện'),
        (N'ORDER_MAX_PARCEL_QUANTITY',      CAST(5 AS decimal(18,2)),        N'Số sản phẩm tối đa mỗi kiện'),
        (N'ORDER_MAX_PARCEL_LENGTH_CM',     CAST(100 AS decimal(18,2)),      N'Chiều dài tối đa mỗi kiện'),
        (N'ORDER_MAX_PARCEL_WIDTH_CM',      CAST(200 AS decimal(18,2)),      N'Chiều rộng tối đa mỗi kiện'),
        (N'ORDER_MAX_PARCEL_HEIGHT_CM',     CAST(50 AS decimal(18,2)),       N'Chiều cao tối đa mỗi kiện'),
        (N'ORDER_MAX_PACKAGES',             CAST(50 AS decimal(18,2)),       N'Số kiện tối đa của một đơn ký gửi'),
        (N'PURCHASE_MAX_ITEMS',             CAST(50 AS decimal(18,2)),       N'Số dòng sản phẩm tối đa của một yêu cầu mua hộ'),
        (N'PURCHASE_MAX_ITEM_QUANTITY',     CAST(999 AS decimal(18,2)),      N'Số lượng tối đa mỗi sản phẩm mua hộ')
    ) AS v([setting_key], [decimal_value], [description])
    WHERE NOT EXISTS (SELECT 1 FROM [SYSTEM_SETTINGS] s WHERE s.[setting_key] = v.[setting_key]);

END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930154030_OrderLimitSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260930154030_OrderLimitSettings', N'8.0.27');
END;
GO

COMMIT;
GO

