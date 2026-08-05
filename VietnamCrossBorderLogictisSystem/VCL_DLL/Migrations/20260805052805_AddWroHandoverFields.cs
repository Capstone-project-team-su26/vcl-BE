using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWroHandoverFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "carrier_id",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_name",
                table: "WRO_REQUESTS",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_phone",
                table: "WRO_REQUESTS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "handover_at",
                table: "WRO_REQUESTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handover_notes",
                table: "WRO_REQUESTS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_number",
                table: "WRO_REQUESTS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vehicle_number",
                table: "WRO_REQUESTS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WRO_REQUESTS_carrier_id",
                table: "WRO_REQUESTS",
                column: "carrier_id");

            migrationBuilder.AddForeignKey(
                name: "FK_WRO_CARRIER",
                table: "WRO_REQUESTS",
                column: "carrier_id",
                principalTable: "CARRIERS",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WRO_CARRIER",
                table: "WRO_REQUESTS");

            migrationBuilder.DropIndex(
                name: "IX_WRO_REQUESTS_carrier_id",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "carrier_id",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "driver_name",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "driver_phone",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "handover_at",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "handover_notes",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "tracking_number",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "vehicle_number",
                table: "WRO_REQUESTS");
        }
    }
}
