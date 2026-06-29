using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePricingRuleForSurcharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consignment_type",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "price_per_weight",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "unit_type",
                table: "PRICING_RULES");

            migrationBuilder.RenameColumn(
                name: "shipping_service_type",
                table: "PRICING_RULES",
                newName: "condition_type");

            migrationBuilder.RenameColumn(
                name: "service_fee",
                table: "PRICING_RULES",
                newName: "value");

            migrationBuilder.RenameColumn(
                name: "route",
                table: "PRICING_RULES",
                newName: "condition_value");

            migrationBuilder.RenameColumn(
                name: "price_per_volume",
                table: "PRICING_RULES",
                newName: "min_amount");

            migrationBuilder.AddColumn<string>(
                name: "calculation_type",
                table: "PRICING_RULES",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "PRICING_RULES",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_required",
                table: "PRICING_RULES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "max_amount",
                table: "PRICING_RULES",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rule_code",
                table: "PRICING_RULES",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "rule_name",
                table: "PRICING_RULES",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "rule_type",
                table: "PRICING_RULES",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "service_pricing_id",
                table: "PRICING_RULES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "PRICING_RULES",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "ACTIVE");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "PRICING_RULES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRICING_RULES_service_pricing_id",
                table: "PRICING_RULES",
                column: "service_pricing_id");

            migrationBuilder.CreateIndex(
                name: "UQ_PRICING_RULES_RULE_CODE",
                table: "PRICING_RULES",
                column: "rule_code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PRICING_RULES_SERVICE_PRICING",
                table: "PRICING_RULES",
                column: "service_pricing_id",
                principalTable: "SERVICE_PRICINGS",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PRICING_RULES_SERVICE_PRICING",
                table: "PRICING_RULES");

            migrationBuilder.DropIndex(
                name: "IX_PRICING_RULES_service_pricing_id",
                table: "PRICING_RULES");

            migrationBuilder.DropIndex(
                name: "UQ_PRICING_RULES_RULE_CODE",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "calculation_type",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "description",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "is_required",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "max_amount",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "rule_code",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "rule_name",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "rule_type",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "service_pricing_id",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "status",
                table: "PRICING_RULES");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "PRICING_RULES");

            migrationBuilder.RenameColumn(
                name: "value",
                table: "PRICING_RULES",
                newName: "service_fee");

            migrationBuilder.RenameColumn(
                name: "min_amount",
                table: "PRICING_RULES",
                newName: "price_per_volume");

            migrationBuilder.RenameColumn(
                name: "condition_value",
                table: "PRICING_RULES",
                newName: "route");

            migrationBuilder.RenameColumn(
                name: "condition_type",
                table: "PRICING_RULES",
                newName: "shipping_service_type");

            migrationBuilder.AddColumn<string>(
                name: "consignment_type",
                table: "PRICING_RULES",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "PRICING_RULES",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_per_weight",
                table: "PRICING_RULES",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "unit_type",
                table: "PRICING_RULES",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
