using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWroItemsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WRO_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    wro_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    inventory_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WRO_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_WRO_ITEM_INVENTORY",
                        column: x => x.inventory_id,
                        principalTable: "INVENTORIES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_WRO_ITEM_REQUEST",
                        column: x => x.wro_request_id,
                        principalTable: "WRO_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WRO_ITEMS_inventory_id",
                table: "WRO_ITEMS",
                column: "inventory_id");

            migrationBuilder.CreateIndex(
                name: "IX_WRO_ITEMS_wro_request_id",
                table: "WRO_ITEMS",
                column: "wro_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WRO_ITEMS");
        }
    }
}
