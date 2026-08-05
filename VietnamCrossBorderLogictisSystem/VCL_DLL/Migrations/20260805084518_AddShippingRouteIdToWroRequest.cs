using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingRouteIdToWroRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "shipping_route_id",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WRO_REQUESTS_shipping_route_id",
                table: "WRO_REQUESTS",
                column: "shipping_route_id");

            migrationBuilder.AddForeignKey(
                name: "FK_WRO_SHIPPING_ROUTE",
                table: "WRO_REQUESTS",
                column: "shipping_route_id",
                principalTable: "SHIPPING_ROUTES",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WRO_SHIPPING_ROUTE",
                table: "WRO_REQUESTS");

            migrationBuilder.DropIndex(
                name: "IX_WRO_REQUESTS_shipping_route_id",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "shipping_route_id",
                table: "WRO_REQUESTS");
        }
    }
}
