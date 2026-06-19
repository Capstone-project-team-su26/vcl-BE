using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingRulesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PRICING_RULES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    shipping_service_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    consignment_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    route = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    unit_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    price_per_weight = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    price_per_volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    service_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRICING_RULES", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PRICING_RULES");
        }
    }
}
