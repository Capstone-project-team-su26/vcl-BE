using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseInboundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WAREHOUSE_INBOUND_REQUESTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    inbound_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    shipment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_INBOUND_REQUESTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_WIN_SHIPMENT",
                        column: x => x.shipment_id,
                        principalTable: "SHIPMENTS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_WIN_WAREHOUSE",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_INBOUND_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    inbound_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_INBOUND_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_WIN_ITEM_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_WIN_ITEM_REQUEST",
                        column: x => x.inbound_request_id,
                        principalTable: "WAREHOUSE_INBOUND_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_INBOUND_ITEMS_inbound_request_id",
                table: "WAREHOUSE_INBOUND_ITEMS",
                column: "inbound_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_INBOUND_ITEMS_parcel_id",
                table: "WAREHOUSE_INBOUND_ITEMS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_INBOUND_REQUESTS_shipment_id",
                table: "WAREHOUSE_INBOUND_REQUESTS",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_INBOUND_REQUESTS_warehouse_id",
                table: "WAREHOUSE_INBOUND_REQUESTS",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_INBOUND_ITEMS");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_INBOUND_REQUESTS");
        }
    }
}
