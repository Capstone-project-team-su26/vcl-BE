using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <summary>
    /// Thùng CUSTOM tính phí theo thể tích khách nhập:
    /// package_fee của row CUSTOM là ĐƠN GIÁ (VND cho mỗi 1.000 cm³, làm tròn lên),
    /// khác với các thùng tiêu chuẩn (phí cố định/thùng). Đặt mặc định 1.000đ/1.000cm³.
    /// </summary>
    public partial class SetCustomBoxUnitFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PACKAGE_CONFIGURATIONS",
                keyColumn: "id",
                keyValue: new Guid("99999999-4444-4444-4444-444444444444"),
                column: "package_fee",
                value: 1000m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PACKAGE_CONFIGURATIONS",
                keyColumn: "id",
                keyValue: new Guid("99999999-4444-4444-4444-444444444444"),
                column: "package_fee",
                value: 0m);
        }
    }
}
