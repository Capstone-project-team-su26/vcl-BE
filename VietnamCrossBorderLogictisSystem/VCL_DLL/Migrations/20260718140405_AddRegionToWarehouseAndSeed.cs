using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionToWarehouseAndSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "region",
                table: "WAREHOUSES",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.InsertData(
                table: "WAREHOUSES",
                columns: new[] { "id", "warehouse_name", "warehouse_code", "region", "country", "city", "address", "warehouse_type", "is_active" },
                values: new object[,]
                {
                    { new Guid("5f8a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"), "Kho Quảng Châu (Trung Quốc)", "CN_WH", "CN", "China", "Guangzhou", "Quảng Châu, Trung Quốc", "ORIGIN", true },
                    { new Guid("6f9a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"), "Kho Tokyo (Nhật Bản)", "JP_WH", "JP", "Japan", "Tokyo", "Tokyo, Nhật Bản", "ORIGIN", true },
                    { new Guid("7f0a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"), "Kho Seoul (Hàn Quốc)", "KR_WH", "KR", "South Korea", "Seoul", "Seoul, Hàn Quốc", "ORIGIN", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "WAREHOUSES", keyColumn: "id", keyValue: new Guid("5f8a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"));
            migrationBuilder.DeleteData(table: "WAREHOUSES", keyColumn: "id", keyValue: new Guid("6f9a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"));
            migrationBuilder.DeleteData(table: "WAREHOUSES", keyColumn: "id", keyValue: new Guid("7f0a0b1c-2d3e-4f5a-6b7c-8d9e0f1a2b3c"));

            migrationBuilder.DropColumn(
                name: "region",
                table: "WAREHOUSES");
        }
    }
}
