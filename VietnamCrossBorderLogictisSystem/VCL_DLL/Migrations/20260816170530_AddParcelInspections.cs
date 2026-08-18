using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PARCEL_INSPECTIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    declared_quantity = table.Column<int>(type: "int", nullable: true),
                    actual_quantity = table.Column<int>(type: "int", nullable: true),
                    declared_weight = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    actual_weight = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    condition = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    has_discrepancy = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    inspected_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    inspected_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PARCEL_INSPECTIONS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PINS_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PINS_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PINS_SHIPMENT",
                        column: x => x.shipment_id,
                        principalTable: "SHIPMENTS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_INSPECTIONS_order_id",
                table: "PARCEL_INSPECTIONS",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_INSPECTIONS_parcel_id",
                table: "PARCEL_INSPECTIONS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_INSPECTIONS_shipment_id",
                table: "PARCEL_INSPECTIONS",
                column: "shipment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PARCEL_INSPECTIONS");
        }
    }
}
