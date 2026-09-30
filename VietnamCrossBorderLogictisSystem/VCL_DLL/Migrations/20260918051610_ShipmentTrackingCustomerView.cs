using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentTrackingCustomerView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TRACKINGS_shipment_id",
                table: "TRACKINGS");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                table: "TRACKINGS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_message",
                table: "TRACKINGS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRACKINGS_SHIPMENT_TIME",
                table: "TRACKINGS",
                columns: new[] { "shipment_id", "tracking_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TRACKINGS_SHIPMENT_TIME",
                table: "TRACKINGS");

            migrationBuilder.DropColumn(
                name: "created_by",
                table: "TRACKINGS");

            migrationBuilder.DropColumn(
                name: "customer_message",
                table: "TRACKINGS");

            migrationBuilder.CreateIndex(
                name: "IX_TRACKINGS_shipment_id",
                table: "TRACKINGS",
                column: "shipment_id");
        }
    }
}
