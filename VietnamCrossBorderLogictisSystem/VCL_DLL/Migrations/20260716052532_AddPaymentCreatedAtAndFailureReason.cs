using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCreatedAtAndFailureReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "PAYMENTS",
                type: "datetime2",
                nullable: true,
                defaultValueSql: "(getutcdate())");

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                table: "PAYMENTS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                table: "PAYMENTS");
        }
    }
}
