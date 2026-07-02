using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddWroRequestReleaseFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "released_at",
                table: "WRO_REQUESTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "released_by",
                table: "WRO_REQUESTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WRO_REQUESTS_released_by",
                table: "WRO_REQUESTS",
                column: "released_by");

            migrationBuilder.AddForeignKey(
                name: "FK_WRO_RELEASED_USER",
                table: "WRO_REQUESTS",
                column: "released_by",
                principalTable: "USERS",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WRO_RELEASED_USER",
                table: "WRO_REQUESTS");

            migrationBuilder.DropIndex(
                name: "IX_WRO_REQUESTS_released_by",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "released_at",
                table: "WRO_REQUESTS");

            migrationBuilder.DropColumn(
                name: "released_by",
                table: "WRO_REQUESTS");
        }
    }
}
