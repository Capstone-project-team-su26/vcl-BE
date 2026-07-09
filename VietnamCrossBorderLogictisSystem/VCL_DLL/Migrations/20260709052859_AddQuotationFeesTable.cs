using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationFeesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "service_pricing_id",
                table: "QUOTATIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QUOTATION_FEES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    quotation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    purchase_quotation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    pricing_rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    fee_name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    fee_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    calculation_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUOTATION_FEES", x => x.id);
                    table.ForeignKey(
                        name: "FK_QF_PRICING_RULE",
                        column: x => x.pricing_rule_id,
                        principalTable: "PRICING_RULES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QF_PURCHASE_QUOTE",
                        column: x => x.purchase_quotation_id,
                        principalTable: "PURCHASE_QUOTATIONS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QF_QUOTE",
                        column: x => x.quotation_id,
                        principalTable: "QUOTATIONS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATIONS_service_pricing_id",
                table: "QUOTATIONS",
                column: "service_pricing_id");

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATION_FEES_pricing_rule_id",
                table: "QUOTATION_FEES",
                column: "pricing_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATION_FEES_purchase_quotation_id",
                table: "QUOTATION_FEES",
                column: "purchase_quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATION_FEES_quotation_id",
                table: "QUOTATION_FEES",
                column: "quotation_id");

            migrationBuilder.AddForeignKey(
                name: "FK_QUOTE_SERVICE_PRICING",
                table: "QUOTATIONS",
                column: "service_pricing_id",
                principalTable: "SERVICE_PRICINGS",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QUOTE_SERVICE_PRICING",
                table: "QUOTATIONS");

            migrationBuilder.DropTable(
                name: "QUOTATION_FEES");

            migrationBuilder.DropIndex(
                name: "IX_QUOTATIONS_service_pricing_id",
                table: "QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "service_pricing_id",
                table: "QUOTATIONS");
        }
    }
}
