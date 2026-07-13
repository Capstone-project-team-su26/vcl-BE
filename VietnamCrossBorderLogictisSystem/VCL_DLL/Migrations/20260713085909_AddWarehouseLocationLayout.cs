using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseLocationLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WAREHOUSE_LOCATION_LAYOUTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    zone_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    shelf_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_index = table.Column<int>(type: "int", nullable: false),
                    column_index = table.Column<int>(type: "int", nullable: false),
                    display_label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    layout_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "BIN"),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "ACTIVE"),
                    width = table.Column<int>(type: "int", nullable: true),
                    height = table.Column<int>(type: "int", nullable: true),
                    color_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_LOCATION_LAYOUTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_WH_LAYOUT_BIN",
                        column: x => x.bin_id,
                        principalTable: "BINS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WH_LAYOUT_SHELF",
                        column: x => x.shelf_id,
                        principalTable: "SHELVES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WH_LAYOUT_WH",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WH_LAYOUT_ZONE",
                        column: x => x.zone_id,
                        principalTable: "WAREHOUSE_ZONES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_LOCATION_LAYOUTS_bin_id",
                table: "WAREHOUSE_LOCATION_LAYOUTS",
                column: "bin_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_LOCATION_LAYOUTS_shelf_id",
                table: "WAREHOUSE_LOCATION_LAYOUTS",
                column: "shelf_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_LOCATION_LAYOUTS_zone_id",
                table: "WAREHOUSE_LOCATION_LAYOUTS",
                column: "zone_id");

            migrationBuilder.CreateIndex(
                name: "UQ_WH_LAYOUT_COORDS",
                table: "WAREHOUSE_LOCATION_LAYOUTS",
                columns: new[] { "warehouse_id", "row_index", "column_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_LOCATION_LAYOUTS");
        }
    }
}
