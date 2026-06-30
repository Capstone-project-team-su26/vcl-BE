using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseReceivingNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "WAREHOUSES",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "warehouse_code",
                table: "WAREHOUSES",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_RECEIVING_NOTES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    receiving_note_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    consignment_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    warehouse_note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "ACTIVE"),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_RECEIVING_NOTES", x => x.id);
                    table.ForeignKey(
                        name: "FK_WRN_ORDER",
                        column: x => x.consignment_order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WRN_WAREHOUSE",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECEIVING_NOTES_consignment_order_id",
                table: "WAREHOUSE_RECEIVING_NOTES",
                column: "consignment_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECEIVING_NOTES_warehouse_id",
                table: "WAREHOUSE_RECEIVING_NOTES",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_RECEIVING_NOTES");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "WAREHOUSES");

            migrationBuilder.DropColumn(
                name: "warehouse_code",
                table: "WAREHOUSES");
        }
    }
}
