using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelCheckInAndStorageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bin_id",
                table: "PARCELS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "check_in_time",
                table: "PARCELS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PARCELS_bin_id",
                table: "PARCELS",
                column: "bin_id");

            migrationBuilder.AddForeignKey(
                name: "FK_PARCEL_BIN",
                table: "PARCELS",
                column: "bin_id",
                principalTable: "BINS",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PARCEL_BIN",
                table: "PARCELS");

            migrationBuilder.DropIndex(
                name: "IX_PARCELS_bin_id",
                table: "PARCELS");

            migrationBuilder.DropColumn(
                name: "bin_id",
                table: "PARCELS");

            migrationBuilder.DropColumn(
                name: "check_in_time",
                table: "PARCELS");
        }
    }
}
