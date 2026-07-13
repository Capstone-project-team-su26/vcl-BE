using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class UpdateQuotationWithVatAndImportTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "import_tax",
                table: "QUOTATIONS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat",
                table: "QUOTATIONS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "import_tax",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "import_tax_rate",
                table: "PRODUCT_TYPES",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000001"),
                column: "import_tax_rate",
                value: 0.10m);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000002"),
                column: "import_tax_rate",
                value: 0.08m);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000003"),
                column: "import_tax_rate",
                value: 0.10m);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000004"),
                column: "import_tax_rate",
                value: 0.05m);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000005"),
                column: "import_tax_rate",
                value: 0.05m);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000006"),
                column: "import_tax_rate",
                value: 0.05m);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000007"),
                column: "import_tax_rate",
                value: 0.05m);

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000008"),
                column: "import_tax_rate",
                value: 0.08m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "import_tax",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "vat",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "import_tax",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "vat",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "import_tax_rate",
                table: "PRODUCT_TYPES");
        }
    }
}
