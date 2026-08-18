using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderStorageEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "storage_eligible_at",
                table: "ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "storage_eligible_by_id",
                table: "ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_eligible_note",
                table: "ORDERS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "storage_eligible_at",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "storage_eligible_by_id",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "storage_eligible_note",
                table: "ORDERS");
        }
    }
}
