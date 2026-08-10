using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderClosingTimestampsAndShipmentCustomsDocs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customs_document_urls",
                table: "SHIPMENTS",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at",
                table: "ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "completed_by_id",
                table: "ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "customer_confirmed_at",
                table: "ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "delivered_at",
                table: "ORDERS",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customs_document_urls",
                table: "SHIPMENTS");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "completed_by_id",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "customer_confirmed_at",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                table: "ORDERS");
        }
    }
}
