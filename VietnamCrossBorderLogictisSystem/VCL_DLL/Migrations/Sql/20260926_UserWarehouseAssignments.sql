BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926145836_UserWarehouseAssignments'
)
BEGIN
    CREATE TABLE [USER_WAREHOUSES] (
        [id] uniqueidentifier NOT NULL DEFAULT ((newid())),
        [user_id] uniqueidentifier NOT NULL,
        [warehouse_id] uniqueidentifier NOT NULL,
        [assigned_by] uniqueidentifier NULL,
        [assigned_at] datetime2 NOT NULL DEFAULT ((getutcdate())),
        [note] nvarchar(500) NULL,
        CONSTRAINT [PK_USER_WAREHOUSES] PRIMARY KEY ([id]),
        CONSTRAINT [FK_USER_WAREHOUSES_USER] FOREIGN KEY ([user_id]) REFERENCES [USERS] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_USER_WAREHOUSES_WAREHOUSE] FOREIGN KEY ([warehouse_id]) REFERENCES [WAREHOUSES] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926145836_UserWarehouseAssignments'
)
BEGIN
    CREATE INDEX [IX_USER_WAREHOUSES_WAREHOUSE] ON [USER_WAREHOUSES] ([warehouse_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926145836_UserWarehouseAssignments'
)
BEGIN
    CREATE UNIQUE INDEX [UX_USER_WAREHOUSES_USER_WAREHOUSE] ON [USER_WAREHOUSES] ([user_id], [warehouse_id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260926145836_UserWarehouseAssignments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260926145836_UserWarehouseAssignments', N'8.0.27');
END;
GO

COMMIT;
GO

