using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderReceiptPdfFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "receipt_generated_at",
                table: "ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_generated_by_id",
                table: "ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_pdf_url",
                table: "ORDERS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receipt_generated_at",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "receipt_generated_by_id",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "receipt_pdf_url",
                table: "ORDERS");
        }
    }
}
