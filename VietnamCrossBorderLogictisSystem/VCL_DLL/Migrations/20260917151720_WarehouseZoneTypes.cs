using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCL_DLL.Migrations
{
    /// <summary>
    /// Khai loại cho các khu kho đang có (trước đây ZONE_TYPE gần như để trống) và chuẩn hoá
    /// trạng thái khu, để luật "hàng lưu kho chỉ nằm ở khu STORAGE" áp được cho dữ liệu cũ.
    /// Khu không khớp tên nào giữ trống (chưa phân loại) — quản lý kho khai qua
    /// PUT /api/warehouse-zones/{zoneId}.
    /// </summary>
    public partial class WarehouseZoneTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE WAREHOUSE_ZONES SET zone_type = UPPER(LTRIM(RTRIM(zone_type)))
WHERE UPPER(LTRIM(RTRIM(zone_type))) IN (N'RECEIVING', N'QUARANTINE', N'STORAGE', N'OUTBOUND')
  AND zone_type <> UPPER(LTRIM(RTRIM(zone_type))) COLLATE Latin1_General_CS_AS;

UPDATE WAREHOUSE_ZONES SET zone_type = N'RECEIVING'
WHERE zone_type IS NULL AND LTRIM(RTRIM(zone_name)) IN (N'Khu nhận', N'Receiving');

UPDATE WAREHOUSE_ZONES SET zone_type = N'OUTBOUND'
WHERE zone_type IS NULL AND LTRIM(RTRIM(zone_name)) IN (N'Khu xuất', N'Xuất hàng');

UPDATE WAREHOUSE_ZONES SET zone_type = N'STORAGE'
WHERE zone_type IS NULL AND LTRIM(RTRIM(zone_name)) IN (N'Khu A', N'Khu Vực A', N'Khu Vực A - Updated');

UPDATE WAREHOUSE_ZONES SET status = N'ACTIVE'
WHERE status IS NULL OR UPPER(LTRIM(RTRIM(status))) = N'ACTIVE';

UPDATE WAREHOUSE_ZONES SET status = N'INACTIVE'
WHERE UPPER(LTRIM(RTRIM(status))) = N'INACTIVE';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dữ liệu khai loại khu không hoàn tác tự động.
        }
    }
}
