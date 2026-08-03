using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitOfMeasureTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UNIT_OF_MEASURES",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "(newid())"),
                    unit_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    unit_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    display_order = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "(getutcdate())"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UNIT_OF_MEASURES", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_UNIT_OF_MEASURES_UNIT_CODE",
                table: "UNIT_OF_MEASURES",
                column: "unit_code",
                unique: true);

            // Seed các đơn vị tính thông dụng để danh mục dùng được ngay.
            // Chỉ chèn khi bảng còn rỗng, tránh trùng khoá nếu migration được chạy lại
            // trên môi trường đã có dữ liệu.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM UNIT_OF_MEASURES)
BEGIN
    INSERT INTO UNIT_OF_MEASURES (id, unit_code, unit_name, description, display_order, is_active)
    VALUES
        (NEWID(), 'PCS',  N'Cái',    N'Đơn vị đếm từng sản phẩm rời',        1, 1),
        (NEWID(), 'SET',  N'Bộ',     N'Nhóm sản phẩm bán theo bộ',          2, 1),
        (NEWID(), 'PAIR', N'Đôi',    N'Sản phẩm bán theo cặp',              3, 1),
        (NEWID(), 'BOX',  N'Hộp',    N'Hàng đóng theo hộp nhỏ',             4, 1),
        (NEWID(), 'PKG',  N'Gói',    N'Hàng đóng theo gói',                 5, 1),
        (NEWID(), 'CTN',  N'Thùng',  N'Hàng đóng theo thùng carton',        6, 1),
        (NEWID(), 'ROLL', N'Cuộn',   N'Hàng dạng cuộn như vải, dây',        7, 1),
        (NEWID(), 'KG',   N'Kilôgam',N'Hàng cân theo khối lượng',           8, 1),
        (NEWID(), 'M',    N'Mét',    N'Hàng đo theo chiều dài',             9, 1);
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UNIT_OF_MEASURES");
        }
    }
}
