using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "INVENTORIES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVENTORIES", x => x.id);
                    table.ForeignKey(
                        name: "FK_INVENTORY_BIN",
                        column: x => x.bin_id,
                        principalTable: "BINS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_CUSTOMER",
                        column: x => x.customer_id,
                        principalTable: "CUSTOMERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_ORDER_ITEM",
                        column: x => x.order_item_id,
                        principalTable: "ORDER_ITEMS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORIES_bin_id",
                table: "INVENTORIES",
                column: "bin_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORIES_customer_id",
                table: "INVENTORIES",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORIES_order_id",
                table: "INVENTORIES",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORIES_order_item_id",
                table: "INVENTORIES",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORIES_parcel_id",
                table: "INVENTORIES",
                column: "parcel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "INVENTORIES");
        }
    }
}
