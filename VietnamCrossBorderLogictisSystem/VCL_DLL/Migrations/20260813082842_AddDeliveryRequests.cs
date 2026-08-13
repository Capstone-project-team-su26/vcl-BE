using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DELIVERY_REQUESTS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    delivery_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    receiver_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    receiver_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    address_detail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    province = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    district = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ward = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    scheduled_date = table.Column<DateTime>(type: "datetime2", nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    carrier_tracking_code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    dispatched_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DELIVERY_REQUESTS", x => x.id);
                    table.ForeignKey(
                        name: "FK_DELIVERY_ORDER",
                        column: x => x.order_id,
                        principalTable: "ORDERS",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "DELIVERY_REQUEST_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    delivery_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pick_source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DELIVERY_REQUEST_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_DELIVERY_ITEM_PARCEL",
                        column: x => x.parcel_id,
                        principalTable: "PARCELS",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_DELIVERY_ITEM_REQUEST",
                        column: x => x.delivery_request_id,
                        principalTable: "DELIVERY_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_REQUEST_ITEMS_delivery_request_id",
                table: "DELIVERY_REQUEST_ITEMS",
                column: "delivery_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_REQUEST_ITEMS_parcel_id",
                table: "DELIVERY_REQUEST_ITEMS",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_REQUESTS_order_id",
                table: "DELIVERY_REQUESTS",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DELIVERY_REQUEST_ITEMS");

            migrationBuilder.DropTable(
                name: "DELIVERY_REQUESTS");
        }
    }
}
