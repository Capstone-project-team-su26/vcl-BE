using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierAndShippingRouteTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SUPPLIERS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    supplier_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    supplier_type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    contact_person = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SUPPLIERS", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "SHIPPING_ROUTES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    route_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    route_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    origin_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    origin_warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    destination_country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    destination_warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    transport_mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    carrier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    supplier_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    estimated_transit_days = table.Column<int>(type: "int", nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPPING_ROUTES", x => x.id);
                    table.ForeignKey(
                        name: "FK_SHIPPING_ROUTE_CARRIER",
                        column: x => x.carrier_id,
                        principalTable: "CARRIERS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIPPING_ROUTE_DESTINATION_WAREHOUSE",
                        column: x => x.destination_warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIPPING_ROUTE_ORIGIN_WAREHOUSE",
                        column: x => x.origin_warehouse_id,
                        principalTable: "WAREHOUSES",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_SHIPPING_ROUTE_SUPPLIER",
                        column: x => x.supplier_id,
                        principalTable: "SUPPLIERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_ROUTES_carrier_id",
                table: "SHIPPING_ROUTES",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_ROUTES_destination_warehouse_id",
                table: "SHIPPING_ROUTES",
                column: "destination_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_ROUTES_origin_warehouse_id",
                table: "SHIPPING_ROUTES",
                column: "origin_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_ROUTES_supplier_id",
                table: "SHIPPING_ROUTES",
                column: "supplier_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPPING_ROUTES");

            migrationBuilder.DropTable(
                name: "SUPPLIERS");
        }
    }
}
