using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class ConsignmentControlsWave1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "manager_id",
                table: "WAREHOUSES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_note",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "receive_approved_at",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receive_approved_by",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "received_at",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "received_by",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "WAREHOUSE_RECEIVING_NOTES",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "accepted_at",
                table: "QUOTATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "accepted_by",
                table: "QUOTATIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "QUOTATIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_reason",
                table: "QUOTATIONS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "price_approval_note",
                table: "QUOTATIONS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "price_approval_status",
                table: "QUOTATIONS",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "price_approved_at",
                table: "QUOTATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "price_approved_by",
                table: "QUOTATIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "QUOTATIONS",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_override",
                table: "QUOTATION_FEES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "original_amount",
                table: "QUOTATION_FEES",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_source",
                table: "PAYMENTS",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "confirmed_at",
                table: "PAYMENTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "confirmed_by",
                table: "PAYMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "received_amount",
                table: "PAYMENTS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "PAYMENTS",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "ORDERS",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "INVENTORIES",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ORDER_STATUS_HISTORIES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    to_status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    event_code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    actor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORDER_STATUS_HISTORIES", x => x.id);
                    table.ForeignKey(
                        name: "FK_OSH_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_MANAGER_HISTORIES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    manager_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    previous_manager_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    changed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    changed_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_MANAGER_HISTORIES", x => x.id);
                    table.ForeignKey(
                        name: "FK_WMH_WAREHOUSE",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSES_manager_id",
                table: "WAREHOUSES",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "UX_PAYMENTS_ORDER_CODE",
                table: "PAYMENTS",
                column: "order_code",
                unique: true,
                filter: "[order_code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_STATUS_HISTORIES_ORDER",
                table: "ORDER_STATUS_HISTORIES",
                columns: new[] { "order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_MANAGER_HISTORIES_WAREHOUSE",
                table: "WAREHOUSE_MANAGER_HISTORIES",
                column: "warehouse_id");

            migrationBuilder.AddForeignKey(
                name: "FK_WAREHOUSE_MANAGER",
                table: "WAREHOUSES",
                column: "manager_id",
                principalTable: "USERS",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Dữ liệu cũ: đánh dấu là đã qua kiểm soát theo cách cũ để quy tắc mới không chặn
            // ngược những việc đã làm xong trước khi triển khai.

            // Phiếu tiếp nhận có sẵn coi như đã được cho phép nhận hàng từ lúc lập.
            migrationBuilder.Sql(@"
                UPDATE WAREHOUSE_RECEIVING_NOTES
                SET receive_approved_at = created_at,
                    approval_note = N'Phiếu lập trước khi áp dụng duyệt trước nhận hàng'
                WHERE receive_approved_at IS NULL AND status <> 'REJECTED';");

            // Khoản đã thu: số thực nhận lấy bằng số phải thu, nguồn xác nhận ghi LEGACY.
            migrationBuilder.Sql(@"
                UPDATE PAYMENTS
                SET received_amount = amount,
                    confirmed_at = paid_at,
                    confirmation_source = 'LEGACY'
                WHERE payment_status = 'PAID' AND confirmation_source IS NULL;");

            // Báo giá cũ không đi qua bước duyệt giá ngoại lệ.
            migrationBuilder.Sql(@"
                UPDATE QUOTATIONS
                SET price_approval_status = 'NOT_REQUIRED'
                WHERE price_approval_status IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WAREHOUSE_MANAGER",
                table: "WAREHOUSES");

            migrationBuilder.DropTable(
                name: "ORDER_STATUS_HISTORIES");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_MANAGER_HISTORIES");

            migrationBuilder.DropIndex(
                name: "IX_WAREHOUSES_manager_id",
                table: "WAREHOUSES");

            migrationBuilder.DropIndex(
                name: "UX_PAYMENTS_ORDER_CODE",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "manager_id",
                table: "WAREHOUSES");

            migrationBuilder.DropColumn(
                name: "approval_note",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "receive_approved_at",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "receive_approved_by",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "received_by",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "accepted_at",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "accepted_by",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "override_reason",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "price_approval_note",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "price_approval_status",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "price_approved_at",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "price_approved_by",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "is_override",
                table: "QUOTATION_FEES");

            migrationBuilder.DropColumn(
                name: "original_amount",
                table: "QUOTATION_FEES");

            migrationBuilder.DropColumn(
                name: "confirmation_source",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "confirmed_by",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "received_amount",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "INVENTORIES");
        }
    }
}
