using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultCNVNServicePricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SERVICE_PRICINGS",
                columns: new[] { "id", "carrier_id", "currency", "destination_country", "effective_date", "origin_country", "price", "service_type", "unit_type" },
                values: new object[,]
                {
                    { new Guid("22222222-1111-1111-1111-111111111111"), null, "VND", "VN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "CN", 50000m, "Express", "KG" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), null, "VND", "VN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "CN", 30000m, "Standard", "KG" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SERVICE_PRICINGS",
                keyColumn: "id",
                keyValue: new Guid("22222222-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "SERVICE_PRICINGS",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));
        }
    }
}
