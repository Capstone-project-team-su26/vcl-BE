using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseRefundLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_REFUND_LINES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    purchase_payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    purchase_request_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    product_name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    reason_code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    goods_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    price_difference_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    service_fee_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    vat_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    import_tax_adjustment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    import_tax_in_refund = table.Column<bool>(type: "bit", nullable: false),
                    cancel_fee_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    formula = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_REFUND_LINES", x => x.id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_REFUND_LINES_PAYMENT",
                        column: x => x.purchase_payment_id,
                        principalTable: "PURCHASE_PAYMENTS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REFUND_LINES_ORDER",
                table: "PURCHASE_REFUND_LINES",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REFUND_LINES_PAYMENT",
                table: "PURCHASE_REFUND_LINES",
                column: "purchase_payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_REFUND_LINES_REQUEST",
                table: "PURCHASE_REFUND_LINES",
                column: "purchase_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_REFUND_LINES");
        }
    }
}
