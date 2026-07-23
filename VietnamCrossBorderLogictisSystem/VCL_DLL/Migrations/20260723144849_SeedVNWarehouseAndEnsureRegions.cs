using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class SeedVNWarehouseAndEnsureRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "WAREHOUSES",
                columns: new[] { "id", "warehouse_name", "warehouse_code", "region", "country", "city", "address", "warehouse_type", "is_active" },
                values: new object[,]
                {
                    { new System.Guid("8f1a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"), "Kho Hồ Chí Minh (Việt Nam)", "VN_WH", "VN", "Vietnam", "Ho Chi Minh", "TP. Hồ Chí Minh, Việt Nam", "DESTINATION", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "WAREHOUSES", keyColumn: "id", keyValue: new System.Guid("8f1a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"));
        }
    }
}
