using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class SeedKRandJPInternationalRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SERVICE_PRICINGS",
                columns: new[] { "id", "carrier_id", "currency", "destination_country", "effective_date", "origin_country", "price", "service_type", "unit_type" },
                values: new object[,]
                {
                    // South Korea -> Vietnam
                    { new Guid("22222222-3333-3333-3333-333333333333"), null, "VND", "VN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "KR", 120000m, "Express", "KG" },
                    { new Guid("22222222-4444-4444-4444-444444444444"), null, "VND", "VN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "KR", 80000m, "Standard", "KG" },

                    // Japan -> Vietnam
                    { new Guid("22222222-5555-5555-5555-555555555555"), null, "VND", "VN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "JP", 150000m, "Express", "KG" },
                    { new Guid("22222222-6666-6666-6666-666666666666"), null, "VND", "VN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "JP", 100000m, "Standard", "KG" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "SERVICE_PRICINGS", keyColumn: "id", keyValue: new Guid("22222222-3333-3333-3333-333333333333"));
            migrationBuilder.DeleteData(table: "SERVICE_PRICINGS", keyColumn: "id", keyValue: new Guid("22222222-4444-4444-4444-444444444444"));
            migrationBuilder.DeleteData(table: "SERVICE_PRICINGS", keyColumn: "id", keyValue: new Guid("22222222-5555-5555-5555-555555555555"));
            migrationBuilder.DeleteData(table: "SERVICE_PRICINGS", keyColumn: "id", keyValue: new Guid("22222222-6666-6666-6666-666666666666"));
        }
    }
}
