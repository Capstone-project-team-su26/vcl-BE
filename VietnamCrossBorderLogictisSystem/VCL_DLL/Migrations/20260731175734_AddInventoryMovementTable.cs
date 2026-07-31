using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryMovementTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "INVENTORY_MOVEMENTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    inventory_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    to_bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    movement_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    moved_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    moved_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVENTORY_MOVEMENTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_INVENTORY_MOVEMENT_FROM_BIN",
                        column: x => x.from_bin_id,
                        principalTable: "BINS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_MOVEMENT_INVENTORY",
                        column: x => x.inventory_id,
                        principalTable: "INVENTORIES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_MOVEMENT_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_MOVEMENT_TO_BIN",
                        column: x => x.to_bin_id,
                        principalTable: "BINS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_INVENTORY_MOVEMENT_USER",
                        column: x => x.moved_by_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_MOVEMENTS_from_bin_id",
                table: "INVENTORY_MOVEMENTS",
                column: "from_bin_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_MOVEMENTS_inventory_id",
                table: "INVENTORY_MOVEMENTS",
                column: "inventory_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_MOVEMENTS_moved_by_id",
                table: "INVENTORY_MOVEMENTS",
                column: "moved_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_MOVEMENTS_parcel_id",
                table: "INVENTORY_MOVEMENTS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_MOVEMENTS_to_bin_id",
                table: "INVENTORY_MOVEMENTS",
                column: "to_bin_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "INVENTORY_MOVEMENTS");
        }
    }
}
