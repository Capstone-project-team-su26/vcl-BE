using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AllowWroAcrossMultipleShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_WRO_REQUESTS_shipment_id",
                table: "SHIPMENT_WRO_REQUESTS");

            migrationBuilder.DropIndex(
                name: "UQ_SHIPMENT_WRO_REQUEST",
                table: "SHIPMENT_WRO_REQUESTS");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_WRO_REQUESTS_wro_request_id",
                table: "SHIPMENT_WRO_REQUESTS",
                column: "wro_request_id");

            migrationBuilder.CreateIndex(
                name: "UQ_SHIPMENT_WRO_REQUEST",
                table: "SHIPMENT_WRO_REQUESTS",
                columns: new[] { "shipment_id", "wro_request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_WRO_REQUESTS_wro_request_id",
                table: "SHIPMENT_WRO_REQUESTS");

            migrationBuilder.DropIndex(
                name: "UQ_SHIPMENT_WRO_REQUEST",
                table: "SHIPMENT_WRO_REQUESTS");

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
    }
}
