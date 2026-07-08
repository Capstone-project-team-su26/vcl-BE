using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseQuotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_QUOTATIONS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    purchase_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    products_subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    purchase_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    shipping_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    total_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_QUOTATIONS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_QUOTATION_REQUEST",
                        column: x => x.purchase_request_id,
                        principalTable: "PURCHASE_REQUESTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_QUOTATION_ITEMS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    purchase_quotation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_request_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    line_total = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_QUOTATION_ITEMS", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_QUOTATION_ITEM_QUOTATION",
                        column: x => x.purchase_quotation_id,
                        principalTable: "PURCHASE_QUOTATIONS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_QUOTATION_ITEMS_purchase_quotation_id",
                table: "PURCHASE_QUOTATION_ITEMS",
                column: "purchase_quotation_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_QUOTATIONS_purchase_request_id",
                table: "PURCHASE_QUOTATIONS",
                column: "purchase_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_QUOTATION_ITEMS");

            migrationBuilder.DropTable(
                name: "PURCHASE_QUOTATIONS");
        }
    }
}
