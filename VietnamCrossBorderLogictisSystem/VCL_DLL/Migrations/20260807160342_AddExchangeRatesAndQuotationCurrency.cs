using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeRatesAndQuotationCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "PURCHASE_QUOTATIONS",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "exchange_rate_at",
                table: "PURCHASE_QUOTATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price_original",
                table: "PURCHASE_QUOTATION_ITEMS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EXCHANGE_RATES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    currency_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    currency_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    rate_to_vnd = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EXCHANGE_RATES", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_EXCHANGE_RATES_CURRENCY_CODE",
                table: "EXCHANGE_RATES",
                column: "currency_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EXCHANGE_RATES");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "exchange_rate_at",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "unit_price_original",
                table: "PURCHASE_QUOTATION_ITEMS");
        }
    }
}
