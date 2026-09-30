using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <summary>
    /// Bảng tham số hệ thống Admin chỉnh được + nhật ký thay đổi, seed giới hạn tạo đơn ký gửi / mua hộ
    /// bằng đúng các số cứng trước đây (hành vi không đổi tới khi Admin sửa).
    /// </summary>
    public partial class OrderLimitSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SYSTEM_SETTINGS",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    setting_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    setting_group = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    decimal_value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SYSTEM_SETTINGS", x => x.id);
                    table.ForeignKey(
                        name: "FK_SYSTEM_SETTINGS_UPDATED_BY",
                        column: x => x.updated_by,
                        principalTable: "USERS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SYSTEM_SETTING_HISTORIES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    setting_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    old_value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    new_value = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    changed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    changed_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SYSTEM_SETTING_HISTORIES", x => x.id);
                    table.ForeignKey(
                        name: "FK_SYSTEM_SETTING_HISTORIES_CHANGED_BY",
                        column: x => x.changed_by,
                        principalTable: "USERS",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_SETTING_HISTORIES_changed_by",
                table: "SYSTEM_SETTING_HISTORIES",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_SETTING_HISTORIES_KEY_TIME",
                table: "SYSTEM_SETTING_HISTORIES",
                columns: new[] { "setting_key", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_SETTINGS_updated_by",
                table: "SYSTEM_SETTINGS",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "UX_SYSTEM_SETTINGS_KEY",
                table: "SYSTEM_SETTINGS",
                column: "setting_key",
                unique: true);

            // Seed giới hạn mặc định = số cứng trước đây. Chỉ thêm khoá chưa có, không đè giá trị Admin đã sửa.
            migrationBuilder.Sql(@"
INSERT INTO [SYSTEM_SETTINGS] ([setting_key], [setting_group], [decimal_value], [description])
SELECT v.[setting_key], N'ORDER_LIMITS', v.[decimal_value], v.[description]
FROM (VALUES
    (N'ORDER_MAX_TOTAL_WEIGHT_KG',      CAST(5 AS decimal(18,2)),        N'Tổng cân nặng tối đa của đơn ký gửi'),
    (N'ORDER_MAX_TOTAL_DECLARED_VALUE', CAST(10000000 AS decimal(18,2)), N'Tổng giá trị khai báo tối đa của đơn ký gửi'),
    (N'ORDER_MAX_PARCEL_WEIGHT_KG',     CAST(3 AS decimal(18,2)),        N'Cân nặng tối đa mỗi kiện'),
    (N'ORDER_MAX_ITEM_DECLARED_VALUE',  CAST(6000000 AS decimal(18,2)),  N'Giá trị khai báo tối đa mỗi kiện'),
    (N'ORDER_MAX_PARCEL_QUANTITY',      CAST(5 AS decimal(18,2)),        N'Số sản phẩm tối đa mỗi kiện'),
    (N'ORDER_MAX_PARCEL_LENGTH_CM',     CAST(100 AS decimal(18,2)),      N'Chiều dài tối đa mỗi kiện'),
    (N'ORDER_MAX_PARCEL_WIDTH_CM',      CAST(200 AS decimal(18,2)),      N'Chiều rộng tối đa mỗi kiện'),
    (N'ORDER_MAX_PARCEL_HEIGHT_CM',     CAST(50 AS decimal(18,2)),       N'Chiều cao tối đa mỗi kiện'),
    (N'ORDER_MAX_PACKAGES',             CAST(50 AS decimal(18,2)),       N'Số kiện tối đa của một đơn ký gửi'),
    (N'PURCHASE_MAX_ITEMS',             CAST(50 AS decimal(18,2)),       N'Số dòng sản phẩm tối đa của một yêu cầu mua hộ'),
    (N'PURCHASE_MAX_ITEM_QUANTITY',     CAST(999 AS decimal(18,2)),      N'Số lượng tối đa mỗi sản phẩm mua hộ')
) AS v([setting_key], [decimal_value], [description])
WHERE NOT EXISTS (SELECT 1 FROM [SYSTEM_SETTINGS] s WHERE s.[setting_key] = v.[setting_key]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SYSTEM_SETTING_HISTORIES");

            migrationBuilder.DropTable(
                name: "SYSTEM_SETTINGS");
        }
    }
}
