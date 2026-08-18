using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PARCEL_RETURNS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    return_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    delivery_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tracking_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    carrier_status_text = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    opened_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    received_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolution = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    fee_bearer = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    waive_storage_fee = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    resolution_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PARCEL_RETURNS", x => x.id);
                    table.ForeignKey(
                        name: "FK_RTN_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_RTN_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_RTN_WAREHOUSE",
                        column: x => x.warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_RETURNS_order_id",
                table: "PARCEL_RETURNS",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_RETURNS_parcel_id",
                table: "PARCEL_RETURNS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "IX_PARCEL_RETURNS_warehouse_id",
                table: "PARCEL_RETURNS",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PARCEL_RETURNS");
        }
    }
}
