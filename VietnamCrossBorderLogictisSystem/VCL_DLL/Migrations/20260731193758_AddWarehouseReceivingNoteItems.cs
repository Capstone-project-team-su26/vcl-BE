using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseReceivingNoteItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WAREHOUSE_RECEIVING_NOTE_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    receiving_note_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    product_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    product_type = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    declared_quantity = table.Column<int>(type: "int", nullable: false),
                    actual_quantity = table.Column<int>(type: "int", nullable: false),
                    quantity_difference = table.Column<int>(type: "int", nullable: false),
                    declared_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    actual_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    weight_difference = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_RECEIVING_NOTE_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_WRN_ITEM_ORDER_ITEM",
                        column: x => x.order_item_id,
                        principalTable: "ORDER_ITEMS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_WRN_ITEM_RECEIVING_NOTE",
                        column: x => x.receiving_note_id,
                        principalTable: "WAREHOUSE_RECEIVING_NOTES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECEIVING_NOTE_ITEMS_order_item_id",
                table: "WAREHOUSE_RECEIVING_NOTE_ITEMS",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECEIVING_NOTE_ITEMS_receiving_note_id",
                table: "WAREHOUSE_RECEIVING_NOTE_ITEMS",
                column: "receiving_note_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_RECEIVING_NOTE_ITEMS");
        }
    }
}
