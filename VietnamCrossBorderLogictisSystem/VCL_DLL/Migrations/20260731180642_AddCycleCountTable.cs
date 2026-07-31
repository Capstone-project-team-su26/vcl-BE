using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleCountTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CYCLE_COUNTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expected_count = table.Column<int>(type: "int", nullable: false),
                    actual_count = table.Column<int>(type: "int", nullable: false),
                    difference = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    counted_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    counted_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    resolved_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolution_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CYCLE_COUNTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_CYCLE_COUNT_BIN",
                        column: x => x.bin_id,
                        principalTable: "BINS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_CYCLE_COUNT_COUNTED_BY",
                        column: x => x.counted_by_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_CYCLE_COUNT_RESOLVED_BY",
                        column: x => x.resolved_by_id,
                        principalTable: "USERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_CYCLE_COUNT_WAREHOUSE",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CYCLE_COUNTS_bin_id",
                table: "CYCLE_COUNTS",
                column: "bin_id");

            migrationBuilder.CreateIndex(
                name: "IX_CYCLE_COUNTS_counted_by_id",
                table: "CYCLE_COUNTS",
                column: "counted_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_CYCLE_COUNTS_resolved_by_id",
                table: "CYCLE_COUNTS",
                column: "resolved_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_CYCLE_COUNTS_warehouse_id",
                table: "CYCLE_COUNTS",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CYCLE_COUNTS");
        }
    }
}
