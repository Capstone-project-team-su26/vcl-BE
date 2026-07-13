using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddPutAwayHistoryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PUT_AWAY_HISTORIES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    parcel_weight = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    volume = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    zone_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    shelf_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bin_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    chosen_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PUT_AWAY_HISTORIES", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "PUT_AWAY_HISTORIES",
                columns: new[] { "id", "bin_id", "category", "chosen_at", "parcel_weight", "shelf_id", "volume", "warehouse_id", "zone_id" },
                values: new object[,]
                {
                    { new Guid("f1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5c"), new Guid("33333333-3333-3333-3333-333333333333"), "Shoes", new DateTime(2026, 6, 20, 10, 0, 0, 0, DateTimeKind.Utc), 12.0m, new Guid("22222222-2222-2222-2222-222222222222"), 0.08m, new Guid("df34760c-0c3e-48ad-bdd6-3a30a4ec402a"), new Guid("11111111-1111-1111-1111-111111111111") },
                    { new Guid("f2a3b4c5-d6e7-5f8a-9b0c-1d2e3f4a5b6c"), new Guid("33333333-3333-3333-3333-333333333333"), "Electronics", new DateTime(2026, 6, 20, 10, 5, 0, 0, DateTimeKind.Utc), 30.0m, new Guid("22222222-2222-2222-2222-222222222222"), 0.15m, new Guid("df34760c-0c3e-48ad-bdd6-3a30a4ec402a"), new Guid("11111111-1111-1111-1111-111111111111") },
                    { new Guid("f3a4b5c6-d7e8-6f9a-0b1c-2d3e4f5a6b7c"), new Guid("33333333-3333-3333-3333-333333333333"), "Fragile", new DateTime(2026, 6, 20, 10, 10, 0, 0, DateTimeKind.Utc), 8.0m, new Guid("22222222-2222-2222-2222-222222222222"), 0.03m, new Guid("df34760c-0c3e-48ad-bdd6-3a30a4ec402a"), new Guid("11111111-1111-1111-1111-111111111111") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PUT_AWAY_HISTORIES");
        }
    }
}
