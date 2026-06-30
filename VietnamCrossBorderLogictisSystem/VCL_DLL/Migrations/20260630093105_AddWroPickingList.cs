using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWroPickingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PICKING_LISTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    wro_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true, defaultValueSql: "('PENDING')"),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PICKING_LISTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PICKING_LIST_USER",
                        column: x => x.created_by,
                        principalTable: "USERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PICKING_LIST_WRO_REQUEST",
                        column: x => x.wro_request_id,
                        principalTable: "WRO_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PICKING_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    picking_list_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    inventory_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PICKING_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PICKING_ITEM_INVENTORY",
                        column: x => x.inventory_id,
                        principalTable: "INVENTORIES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PICKING_ITEM_LIST",
                        column: x => x.picking_list_id,
                        principalTable: "PICKING_LISTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEMS_inventory_id",
                table: "PICKING_ITEMS",
                column: "inventory_id");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEMS_picking_list_id",
                table: "PICKING_ITEMS",
                column: "picking_list_id");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LISTS_created_by",
                table: "PICKING_LISTS",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LISTS_wro_request_id",
                table: "PICKING_LISTS",
                column: "wro_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PICKING_ITEMS");

            migrationBuilder.DropTable(
                name: "PICKING_LISTS");
        }
    }
}
