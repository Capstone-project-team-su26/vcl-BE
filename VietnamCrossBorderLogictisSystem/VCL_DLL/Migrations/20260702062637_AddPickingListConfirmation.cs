using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingListConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "confirmed_at",
                table: "PICKING_LISTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "confirmed_by",
                table: "PICKING_LISTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LISTS_confirmed_by",
                table: "PICKING_LISTS",
                column: "confirmed_by");

            migrationBuilder.AddForeignKey(
                name: "FK_PICKING_LIST_CONFIRMED_USER",
                table: "PICKING_LISTS",
                column: "confirmed_by",
                principalTable: "USERS",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PICKING_LIST_CONFIRMED_USER",
                table: "PICKING_LISTS");

            migrationBuilder.DropIndex(
                name: "IX_PICKING_LISTS_confirmed_by",
                table: "PICKING_LISTS");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                table: "PICKING_LISTS");

            migrationBuilder.DropColumn(
                name: "confirmed_by",
                table: "PICKING_LISTS");
        }
    }
}
