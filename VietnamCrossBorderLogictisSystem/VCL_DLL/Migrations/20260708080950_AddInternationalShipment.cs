using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddInternationalShipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shipping_method",
                table: "SHIPMENTS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_packages",
                table: "SHIPMENTS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_weight",
                table: "SHIPMENTS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SHIPMENT_PARCELS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_PARCELS", x => x.id);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_PARCEL_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_PARCEL_SHIPMENT",
                        column: x => x.shipment_id,
                        principalTable: "SHIPMENTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SHIPMENT_WRO_REQUESTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    wro_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_WRO_REQUESTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_WRO_REQUEST",
                        column: x => x.wro_request_id,
                        principalTable: "WRO_REQUESTS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_WRO_SHIPMENT",
                        column: x => x.shipment_id,
                        principalTable: "SHIPMENTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_PARCELS_parcel_id",
                table: "SHIPMENT_PARCELS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_PARCELS_shipment_id",
                table: "SHIPMENT_PARCELS",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_WRO_REQUESTS_shipment_id",
                table: "SHIPMENT_WRO_REQUESTS",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "UQ_SHIPMENT_WRO_REQUEST",
                table: "SHIPMENT_WRO_REQUESTS",
                column: "wro_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPMENT_PARCELS");

            migrationBuilder.DropTable(
                name: "SHIPMENT_WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "shipping_method",
                table: "SHIPMENTS");

            migrationBuilder.DropColumn(
                name: "total_packages",
                table: "SHIPMENTS");

            migrationBuilder.DropColumn(
                name: "total_weight",
                table: "SHIPMENTS");
        }
    }
}
