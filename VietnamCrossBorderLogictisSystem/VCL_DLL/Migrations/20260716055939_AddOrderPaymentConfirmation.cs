using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_confirmation_note",
                table: "ORDERS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_confirmed_at",
                table: "ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "payment_confirmed_by_id",
                table: "ORDERS",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_confirmation_note",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "payment_confirmed_at",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "payment_confirmed_by_id",
                table: "ORDERS");
        }
    }
}
