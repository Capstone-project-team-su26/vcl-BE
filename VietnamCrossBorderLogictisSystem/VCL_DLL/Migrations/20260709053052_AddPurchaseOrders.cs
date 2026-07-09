using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_ORDERS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    purchase_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_quotation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    purchase_order_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    supplier_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    purchase_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    status_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())"),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_ORDERS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_ORDER_REQUEST",
                        column: x => x.purchase_request_id,
                        principalTable: "PURCHASE_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_ORDER_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    purchase_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_request_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    line_total = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_ORDER_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_ORDER_ITEM_ORDER",
                        column: x => x.purchase_order_id,
                        principalTable: "PURCHASE_ORDERS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDER_ITEMS_purchase_order_id",
                table: "PURCHASE_ORDER_ITEMS",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDERS_purchase_request_id",
                table: "PURCHASE_ORDERS",
                column: "purchase_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_ORDER_ITEMS");

            migrationBuilder.DropTable(
                name: "PURCHASE_ORDERS");
        }
    }
}
