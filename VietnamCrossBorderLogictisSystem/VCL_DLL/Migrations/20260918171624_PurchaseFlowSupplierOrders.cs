using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseFlowSupplierOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_ORDERS_purchase_request_id",
                table: "PURCHASE_ORDERS");

            migrationBuilder.AddColumn<decimal>(
                name: "domestic_shipping_fee",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_later_amount",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_weight",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "freight_rate_per_kg",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "prepay_amount",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_pricing_id",
                table: "PURCHASE_QUOTATIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_rate",
                table: "PURCHASE_QUOTATIONS",
                type: "decimal(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_order_id",
                table: "PURCHASE_PAYMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "PURCHASE_ORDERS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "PURCHASE_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by",
                table: "PURCHASE_ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "PURCHASE_ORDERS",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "customer_decision_at",
                table: "PURCHASE_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_reject_reason",
                table: "PURCHASE_ORDERS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "decided_at",
                table: "PURCHASE_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                table: "PURCHASE_ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "PURCHASE_ORDERS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "domestic_carrier",
                table: "PURCHASE_ORDERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "domestic_tracking_code",
                table: "PURCHASE_ORDERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "PURCHASE_ORDERS",
                type: "decimal(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ordered_at",
                table: "PURCHASE_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ordered_by",
                table: "PURCHASE_ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_difference_amount",
                table: "PURCHASE_ORDERS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quoted_goods_amount",
                table: "PURCHASE_ORDERS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sequence_no",
                table: "PURCHASE_ORDERS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "PURCHASE_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by",
                table: "PURCHASE_ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "supplier_confirmed_at",
                table: "PURCHASE_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_id",
                table: "PURCHASE_ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_order_code",
                table: "PURCHASE_ORDERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "supplier_shipped_at",
                table: "PURCHASE_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "warehouse_id",
                table: "PURCHASE_ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quoted_unit_price",
                table: "PURCHASE_ORDER_ITEMS",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price_original",
                table: "PURCHASE_ORDER_ITEMS",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_order_id",
                table: "ORDERS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_PAYMENTS_PURCHASE_ORDER",
                table: "PURCHASE_PAYMENTS",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDERS_REQUEST_STATUS",
                table: "PURCHASE_ORDERS",
                columns: new[] { "purchase_request_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDERS_supplier_id",
                table: "PURCHASE_ORDERS",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDERS_warehouse_id",
                table: "PURCHASE_ORDERS",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_ORDERS_PURCHASE_ORDER",
                table: "ORDERS",
                column: "purchase_order_id");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_ORDERS_SUPPLIER",
                table: "PURCHASE_ORDERS",
                column: "supplier_id",
                principalTable: "SUPPLIERS",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_ORDERS_WAREHOUSE",
                table: "PURCHASE_ORDERS",
                column: "warehouse_id",
                principalTable: "WAREHOUSES",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_ORDERS_SUPPLIER",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_ORDERS_WAREHOUSE",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_PAYMENTS_PURCHASE_ORDER",
                table: "PURCHASE_PAYMENTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_ORDERS_REQUEST_STATUS",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_ORDERS_supplier_id",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_ORDERS_warehouse_id",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropIndex(
                name: "IX_ORDERS_PURCHASE_ORDER",
                table: "ORDERS");

            migrationBuilder.DropColumn(
                name: "domestic_shipping_fee",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "estimated_later_amount",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "estimated_weight",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "freight_rate_per_kg",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "prepay_amount",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "service_pricing_id",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "vat_rate",
                table: "PURCHASE_QUOTATIONS");

            migrationBuilder.DropColumn(
                name: "purchase_order_id",
                table: "PURCHASE_PAYMENTS");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "customer_decision_at",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "customer_reject_reason",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "domestic_carrier",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "domestic_tracking_code",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "ordered_at",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "ordered_by",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "price_difference_amount",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "quoted_goods_amount",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "sequence_no",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "submitted_by",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "supplier_confirmed_at",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "supplier_order_code",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "supplier_shipped_at",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                table: "PURCHASE_ORDERS");

            migrationBuilder.DropColumn(
                name: "quoted_unit_price",
                table: "PURCHASE_ORDER_ITEMS");

            migrationBuilder.DropColumn(
                name: "unit_price_original",
                table: "PURCHASE_ORDER_ITEMS");

            migrationBuilder.DropColumn(
                name: "purchase_order_id",
                table: "ORDERS");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDERS_purchase_request_id",
                table: "PURCHASE_ORDERS",
                column: "purchase_request_id");
        }
    }
}
