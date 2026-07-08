using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class ProductTypesVietnamese : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000001"),
                column: "name",
                value: "Điện tử");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000002"),
                column: "name",
                value: "Quần áo");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000003"),
                column: "name",
                value: "Mỹ phẩm");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000004"),
                column: "name",
                value: "Phụ kiện");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000005"),
                column: "name",
                value: "Thực phẩm");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000006"),
                column: "name",
                value: "Sách");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000007"),
                column: "name",
                value: "Đồ chơi");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000008"),
                column: "name",
                value: "Đồ gia dụng");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000001"),
                column: "name",
                value: "Electronics");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000002"),
                column: "name",
                value: "Clothing");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000003"),
                column: "name",
                value: "Cosmetics");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000004"),
                column: "name",
                value: "Accessories");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000005"),
                column: "name",
                value: "Food");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000006"),
                column: "name",
                value: "Books");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000007"),
                column: "name",
                value: "Toys");

            migrationBuilder.UpdateData(
                table: "PRODUCT_TYPES",
                keyColumn: "id",
                keyValue: new Guid("11111111-0000-0000-0000-000000000008"),
                column: "name",
                value: "Household Goods");
        }
    }
}
