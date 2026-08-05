using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class SeedPurchaseFeePricingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PRICING_RULES",
                columns: new[] { "id", "calculation_type", "condition_type", "condition_value", "created_at", "description", "max_amount", "min_amount", "rule_code", "rule_name", "rule_type", "service_pricing_id", "status", "updated_at", "value" },
                values: new object[,]
                {
                    { new Guid("e9b3a1d4-8f2c-4e61-9c35-1a2b3c4d5e6f"), "PERCENTAGE", null, null, new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Phí dịch vụ mua hộ tiêu chuẩn (5% tổng giá trị sản phẩm)", null, null, "PURCHASE_FEE_PERCENT", "Phí dịch vụ mua hộ (5%)", "PURCHASE_FEE", null, "ACTIVE", null, 5.00m },
                    { new Guid("f1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5c"), "FIXED", null, null, new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Phí dịch vụ mua hộ cố định 50.000 VNĐ mỗi đơn hàng", null, null, "PURCHASE_FEE_FIXED", "Phí dịch vụ mua hộ cố định (50.000 VNĐ)", "PURCHASE_FEE", null, "ACTIVE", null, 50000.00m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PRICING_RULES",
                keyColumn: "id",
                keyValue: new Guid("e9b3a1d4-8f2c-4e61-9c35-1a2b3c4d5e6f"));

            migrationBuilder.DeleteData(
                table: "PRICING_RULES",
                keyColumn: "id",
                keyValue: new Guid("f1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5c"));
        }
    }
}
