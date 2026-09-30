using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class ItemServicesPerParcel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "order_item_id",
                table: "QUOTATION_FEES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ORDER_ITEM_SERVICES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    order_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pricing_rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORDER_ITEM_SERVICES", x => x.id);
                    table.ForeignKey(
                        name: "FK_OIS_ORDER_ITEM",
                        column: x => x.order_item_id,
                        principalTable: "ORDER_ITEMS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OIS_PRICING_RULE",
                        column: x => x.pricing_rule_id,
                        principalTable: "PRICING_RULES",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QUOTATION_FEES_ORDER_ITEM",
                table: "QUOTATION_FEES",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_ORDER_ITEM_SERVICES_pricing_rule_id",
                table: "ORDER_ITEM_SERVICES",
                column: "pricing_rule_id");

            migrationBuilder.CreateIndex(
                name: "UX_ORDER_ITEM_SERVICES_ITEM_RULE",
                table: "ORDER_ITEM_SERVICES",
                columns: new[] { "order_item_id", "pricing_rule_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QUOTATION_FEES_ORDER_ITEM",
                table: "QUOTATION_FEES",
                column: "order_item_id",
                principalTable: "ORDER_ITEMS",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QUOTATION_FEES_ORDER_ITEM",
                table: "QUOTATION_FEES");

            migrationBuilder.DropTable(
                name: "ORDER_ITEM_SERVICES");

            migrationBuilder.DropIndex(
                name: "IX_QUOTATION_FEES_ORDER_ITEM",
                table: "QUOTATION_FEES");

            migrationBuilder.DropColumn(
                name: "order_item_id",
                table: "QUOTATION_FEES");
        }
    }
}
